using Pgvector;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.ChatApi.Tests;

/// <summary>
/// Simulates a tool-calling model without a live Ollama instance: the first call (no "tool" role
/// message yet) decides which tool to call based on the user's text; the second call (after a
/// tool result has been appended) streams a canned answer.
/// </summary>
internal sealed class FakeOllamaClient : IOllamaClient
{
    public Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();

        if (messages.Any(m => m.Role == "tool"))
        {
            foreach (var token in new[] { "Open ", "the ", "self-service ", "portal." })
            {
                await Task.Yield();
                yield return new OllamaChatStreamChunk(token, Done: false);
            }

            yield return new OllamaChatStreamChunk(null, Done: true);
            yield break;
        }

        var userText = messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        var toolCall = userText.Contains("ticket", StringComparison.OrdinalIgnoreCase)
            ? new OllamaToolCall("create_support_ticket", """{"summary":"VPN issue","details":"User cannot reset their VPN password."}""")
            : new OllamaToolCall("search_knowledge_base", """{"query":"vpn password reset","top_k":5}""");

        yield return new OllamaChatStreamChunk(null, Done: true, ToolCalls: [toolCall]);
    }
}
