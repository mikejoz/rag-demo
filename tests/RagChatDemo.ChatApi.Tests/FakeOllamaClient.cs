using Pgvector;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.ChatApi.Tests;

/// <summary>Streams a few canned tokens without calling a live Ollama instance.</summary>
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
        foreach (var token in new[] { "Open ", "the ", "self-service ", "portal." })
        {
            await Task.Yield();
            yield return new OllamaChatStreamChunk(token, Done: false);
        }

        yield return new OllamaChatStreamChunk(null, Done: true);
    }
}
