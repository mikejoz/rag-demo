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
/// Direct text is never streamed; if no tool call is made, a knowledge base search is forced.
/// </summary>
public class RagOrchestrator(IMcpToolClient mcpClient, IOllamaClient ollama, IActivityPublisher activityPublisher)
{
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
            new("system", GroundingPolicy.SystemPrompt),
            new("user", userText),
        ];

        await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Started, null, cancellationToken);

        IReadOnlyList<OllamaToolCall> toolCalls;
        try
        {
            toolCalls = await CollectToolCallsAsync(messages, cancellationToken);
        }
        catch (Exception ex)
        {
            await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Failed, ex.Message, cancellationToken);
            await caller.ResponseError(messageId, "Failed to generate a response from the local model.");
            return;
        }

        // If no tool call was made, force a knowledge base search.
        if (toolCalls.Count == 0)
        {
            await PublishAsync(
                ActivityCategory.Retrieval, "forced_retrieval", ActivityStatus.Succeeded,
                "Model skipped search; forcing knowledge base search", cancellationToken);

            var forcedToolCall = new OllamaToolCall(
                "search_knowledge_base",
                JsonSerializer.Serialize(new { query = userText, top_k = 5 }, JsonOptions));

            await HandleToolCallAsync(forcedToolCall, messageId, messages, caller, cancellationToken);
            return;
        }

        // This demo handles a single tool call per turn - enough to demonstrate the pattern (US4).
        var toolCall = toolCalls[0];
        await HandleToolCallAsync(toolCall, messageId, messages, caller, cancellationToken);
    }

    /// <summary>Handles tool execution, result processing, and final generation for a single tool call.</summary>
    private async Task HandleToolCallAsync(
        OllamaToolCall toolCall, Guid messageId, List<OllamaChatMessage> messages, IChatClient caller, CancellationToken cancellationToken)
    {
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

                if (searchResult.Rerank is { } rerank)
                {
                    if (rerank.FellBack)
                    {
                        await PublishAsync(
                            ActivityCategory.Retrieval, "rerank_fallback", ActivityStatus.Failed,
                            $"Re-ranker unavailable ({rerank.FallbackReason}); using vector order", cancellationToken);
                    }
                    else
                    {
                        var before = string.Join("; ", rerank.Before.Select(FormatRerankedItem));
                        var after = string.Join("; ", rerank.After.Select(FormatRerankedItem));
                        await PublishAsync(
                            ActivityCategory.Retrieval, "rerank", ActivityStatus.Succeeded,
                            $"Re-ranked {rerank.CandidatesIn} candidates -> kept {rerank.Kept} in {rerank.ElapsedMs}ms. Before: [{before}] After: [{after}]",
                            cancellationToken);
                    }
                }

                if (searchResult.Results.Count == 0)
                {
                    await PublishAsync(
                        ActivityCategory.Retrieval, "grounded_fallback", ActivityStatus.Succeeded,
                        "No relevant content; returning not-found response", cancellationToken);
                    await caller.ResponseToken(messageId, GroundingPolicy.NotFoundMessage);
                    await caller.ResponseComplete(messageId, []);
                    return;
                }

                citations = searchResult.Results
                    .Select(r => new SourceCitationDto { DocumentTitle = r.DocumentTitle, SourceUrl = r.SourceUrl })
                    .DistinctBy(c => c.SourceUrl)
                    .ToArray();

                // Wrap the search result in context delimiters and add to messages
                messages.Add(new OllamaChatMessage("tool", $"<context>{JsonSerializer.Serialize(searchResult, JsonOptions)}</context>"));

                // Add grounding instruction for final generation
                messages.Add(new OllamaChatMessage("system", GroundingPolicy.GroundedAnswerInstruction));
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
        var filter = new ThinkTagFilter();
        try
        {
            await foreach (var chunk in ollama.StreamChatAsync(messages, cancellationToken: cancellationToken))
            {
                if (!string.IsNullOrEmpty(chunk.ContentToken))
                {
                    var visible = filter.Feed(chunk.ContentToken);
                    if (visible.Length > 0)
                    {
                        generatedLength += visible.Length;
                        await caller.ResponseToken(messageId, visible);
                    }
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
    /// Calls the model with tool definitions and collects any tool calls. Direct text responses
    /// are buffered and discarded - only tool calls are returned (prompt grounding enforcement).
    /// </summary>
    private async Task<IReadOnlyList<OllamaToolCall>> CollectToolCallsAsync(
        IReadOnlyList<OllamaChatMessage> messages, CancellationToken cancellationToken)
    {
        await foreach (var chunk in ollama.StreamChatAsync(messages, tools: Tools, cancellationToken: cancellationToken))
        {
            if (chunk.ToolCalls is { Count: > 0 })
            {
                return chunk.ToolCalls;
            }

            // Discard direct text content (it may contain the model attempting to answer without retrieval)
        }

        return [];
    }

    private static string FormatRerankedItem(RerankedItemDto item) =>
        $"{item.Title} (score:{item.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})";

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
