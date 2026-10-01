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
public class RagOrchestrator(IMcpToolClient mcpClient, IOllamaClient ollama, IActivityPublisher activityPublisher)
{
    private const string NoKnowledgeFoundResponse =
        "I couldn't find any relevant information in the knowledge base to answer that question.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleMessageAsync(
        string userText, Guid messageId, IChatClient caller, CancellationToken cancellationToken)
    {
        await PublishAsync(ActivityCategory.Retrieval, "search_knowledge_base", ActivityStatus.Started, null, cancellationToken);

        SearchKnowledgeBaseResult searchResult;
        try
        {
            var callResult = await mcpClient.CallToolAsync(
                "search_knowledge_base",
                new Dictionary<string, object?> { ["query"] = userText, ["top_k"] = 5 },
                cancellationToken);
            searchResult = ExtractToolResult<SearchKnowledgeBaseResult>(callResult) ?? new SearchKnowledgeBaseResult([]);
        }
        catch (Exception ex)
        {
            await PublishAsync(ActivityCategory.McpTool, "search_knowledge_base", ActivityStatus.Failed, ex.Message, cancellationToken);
            await caller.ResponseError(messageId, "Failed to search the knowledge base.");
            return;
        }

        await PublishAsync(
            ActivityCategory.McpTool,
            "search_knowledge_base",
            ActivityStatus.Succeeded,
            $"{searchResult.Results.Count} chunk(s) found",
            cancellationToken);

        if (searchResult.Results.Count == 0)
        {
            await caller.ResponseToken(messageId, NoKnowledgeFoundResponse);
            await caller.ResponseComplete(messageId, []);
            return;
        }

        await PublishAsync(ActivityCategory.Generation, "generate_response", ActivityStatus.Started, null, cancellationToken);

        var prompt = BuildGroundedPrompt(userText, searchResult.Results);
        var generatedLength = 0;
        try
        {
            await foreach (var chunk in ollama.StreamChatAsync(prompt, cancellationToken: cancellationToken))
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

        var citations = searchResult.Results
            .Select(r => new SourceCitationDto { DocumentTitle = r.DocumentTitle, SourceUrl = r.SourceUrl })
            .DistinctBy(c => c.SourceUrl)
            .ToArray();
        await caller.ResponseComplete(messageId, citations);
    }

    private static IReadOnlyList<OllamaChatMessage> BuildGroundedPrompt(
        string userText, IReadOnlyList<KnowledgeBaseSearchHit> hits)
    {
        var context = string.Join(
            "\n\n",
            hits.Select(h => $"Source: {h.DocumentTitle}\n{h.Text}"));

        var systemPrompt =
            "You are a support assistant. Answer the user's question using ONLY the knowledge base " +
            "excerpts below. If the excerpts don't contain the answer, say so rather than guessing.\n\n" +
            $"Knowledge base excerpts:\n{context}";

        return
        [
            new OllamaChatMessage("system", systemPrompt),
            new OllamaChatMessage("user", userText),
        ];
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
