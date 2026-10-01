using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using RagChatDemo.ChatApi.Hubs;
using RagChatDemo.ChatApi.Mcp;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.ChatApi.Chat;

/// <summary>Orchestrates the retrieval-augmented chat flow: MCP retrieval -> grounded prompt -> streamed LLM response.</summary>
/// <summary>
/// Orchestrates the chat flow as genuine LLM-driven tool-calling: the model decides whether to
/// call <c>search_knowledge_base</c>, <c>create_support_ticket</c>, both, or neither (research.md D4).
/// </summary>
public class RagOrchestrator(IMcpToolClient mcpClient, IOllamaClient ollama, IActivityPublisher activityPublisher)
{
    private const string NoKnowledgeFoundResponse =
        "I couldn't find any relevant information in the knowledge base to answer that question.";

    private const string SystemPrompt =
        "You are an IT support assistant. When the user asks a question, call the " +
        "search_knowledge_base tool to find relevant information before answering - never answer " +
        "from general knowledge. If the user explicitly wants to log/report an issue or asks for a " +
        "ticket to be created, call create_support_ticket instead. Only use information returned by " +
        "your tools; if a search finds nothing relevant, say you don't have that information.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyList<OllamaToolDefinition> Tools =
    [
        new(
            "search_knowledge_base",
            "Searches the internal IT/support knowledge base for content relevant to a natural language question.",
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Natural language question or topic to search for." },
                    top_k = new { type = "integer", description = "Max number of chunks to return." },
                },
                required = new[] { "query" },
            }),
        new(
            "create_support_ticket",
            "Creates a support ticket. Only call this when the user explicitly wants to log, report, " +
            "or escalate an issue to IT support.",
            new
            {
                type = "object",
                properties = new
                {
                    summary = new { type = "string", description = "Short summary of the issue." },
                    details = new { type = "string", description = "Full details of the issue." },
                },
                required = new[] { "summary", "details" },
            }),
    ];

    public async Task HandleMessageAsync(
        string userText, Guid messageId, IChatClient caller, CancellationToken cancellationToken)
    {
        List<OllamaChatMessage> messages =
        [
            new("system", SystemPrompt),
            new("user", userText),
        ];

        await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Started, null, cancellationToken);

        IReadOnlyList<OllamaToolCall> toolCalls;
        try
        {
            toolCalls = await StreamDirectAnswerOrCollectToolCallsAsync(messages, messageId, caller, cancellationToken);
        }
        catch (Exception ex)
        {
            await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Failed, ex.Message, cancellationToken);
            await caller.ResponseError(messageId, "Failed to generate a response from the local model.");
            return;
        }

        if (toolCalls.Count == 0)
        {
            // The model answered directly (tokens already streamed); no tool was needed.
            await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Succeeded, null, cancellationToken);
            await caller.ResponseComplete(messageId, []);
            return;
        }

        // This demo handles a single tool call per turn - enough to demonstrate the pattern (US4).
        var toolCall = toolCalls[0];
        await PublishAsync(ActivityCategory.McpTool, toolCall.Name, ActivityStatus.Started, null, cancellationToken);

        SourceCitationDto[] citations;
        try
        {
            var arguments = ParseArguments(toolCall.ArgumentsJson);
            var callResult = await mcpClient.CallToolAsync(toolCall.Name, arguments, cancellationToken);

            if (toolCall.Name == "search_knowledge_base")
            {
                var searchResult = ExtractToolResult<SearchKnowledgeBaseResult>(callResult) ?? new SearchKnowledgeBaseResult([]);
                await PublishAsync(
                    ActivityCategory.McpTool, toolCall.Name, ActivityStatus.Succeeded,
                    $"{searchResult.Results.Count} chunk(s) found", cancellationToken);

                if (searchResult.Results.Count == 0)
                {
                    await caller.ResponseToken(messageId, NoKnowledgeFoundResponse);
                    await caller.ResponseComplete(messageId, []);
                    return;
                }

                citations = searchResult.Results
                    .Select(r => new SourceCitationDto { DocumentTitle = r.DocumentTitle, SourceUrl = r.SourceUrl })
                    .DistinctBy(c => c.SourceUrl)
                    .ToArray();
                messages.Add(new OllamaChatMessage("tool", JsonSerializer.Serialize(searchResult, JsonOptions)));
            }
            else
            {
                var ticketResult = ExtractToolResult<CreateSupportTicketResult>(callResult);
                await PublishAsync(
                    ActivityCategory.McpTool, toolCall.Name, ActivityStatus.Succeeded,
                    ticketResult is null ? null : $"{ticketResult.TicketId} ({ticketResult.Status})", cancellationToken);

                citations = [];
                messages.Add(new OllamaChatMessage("tool", JsonSerializer.Serialize(ticketResult, JsonOptions)));
            }
        }
        catch (Exception ex)
        {
            await PublishAsync(ActivityCategory.McpTool, toolCall.Name, ActivityStatus.Failed, ex.Message, cancellationToken);
            await caller.ResponseError(messageId, $"Failed to call the {toolCall.Name} tool.");
            return;
        }

        var generatedLength = 0;
        try
        {
            await foreach (var chunk in ollama.StreamChatAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.ContentToken))
                {
                    generatedLength += chunk.ContentToken.Length;
                    await caller.ResponseToken(messageId, chunk.ContentToken);
                }
            }
        }
        catch (Exception ex)
        {
            await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Failed, ex.Message, cancellationToken);
            await caller.ResponseError(messageId, "Failed to generate a response from the local model.");
            return;
        }

        await PublishAsync(
            ActivityCategory.Generation,
            "generate_response",
            ActivityStatus.Succeeded,
            $"{generatedLength} character(s) generated",
            cancellationToken);
        await caller.ResponseComplete(messageId, citations);
    }

    /// <summary>
    /// Streams the model's first response. If it calls a tool, returns the tool call(s) without
    /// streaming anything (Ollama emits empty content alongside tool_calls). Otherwise streams the
    /// direct answer to the caller and returns an empty list.
    /// </summary>
    private async Task<IReadOnlyList<OllamaToolCall>> StreamDirectAnswerOrCollectToolCallsAsync(
        IReadOnlyList<OllamaChatMessage> messages, Guid messageId, IChatClient caller, CancellationToken cancellationToken)
    {
        await foreach (var chunk in ollama.StreamChatAsync(messages, tools: Tools, cancellationToken: cancellationToken))
        {
            if (chunk.ToolCalls is { Count: > 0 })
            {
                return chunk.ToolCalls;
            }

            if (!string.IsNullOrEmpty(chunk.ContentToken))
            {
                await caller.ResponseToken(messageId, chunk.ContentToken);
            }
        }

        return [];
    }

    private static Dictionary<string, object?> ParseArguments(string argumentsJson)
    {
        var element = JsonSerializer.Deserialize<JsonElement>(argumentsJson, JsonOptions);
        return element.EnumerateObject().ToDictionary(p => p.Name, object? (p) => p.Value);
    }

    private static T? DeserializeStructuredContent<T>(JsonElement? structuredContent) =>
        structuredContent is { } element ? element.Deserialize<T>(JsonOptions) : default;

    /// <summary>
    /// Prefers the MCP result's structured content; falls back to parsing the first text content
    /// block as JSON, since not every client/server protocol version negotiation populates
    /// <see cref="CallToolResult.StructuredContent"/>.
    /// </summary>
    private static T? ExtractToolResult<T>(CallToolResult callResult)
    {
        var fromStructuredContent = DeserializeStructuredContent<T>(callResult.StructuredContent);
        if (fromStructuredContent is not null)
        {
            return fromStructuredContent;
        }

        var textBlock = callResult.Content.OfType<TextContentBlock>().FirstOrDefault();
        return textBlock is not null ? JsonSerializer.Deserialize<T>(textBlock.Text, JsonOptions) : default;
    }

    private Task PublishAsync(
        ActivityCategory category, string operation, ActivityStatus status, string? detail, CancellationToken cancellationToken) =>
        activityPublisher.PublishAsync(
            new ActivityEventDto
            {
                Id = Guid.NewGuid(),
                Category = category,
                Source = "ChatApi",
                Operation = operation,
                Target = null,
                Status = status,
                Detail = detail,
                Timestamp = DateTimeOffset.UtcNow,
            },
            cancellationToken);
}
