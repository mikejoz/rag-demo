using Pgvector;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.McpServer.Tests;

/// <summary>Returns a fixed embedding regardless of input text, for deterministic retrieval tests.</summary>
internal sealed class FakeOllamaClient(Vector embeddingToReturn) : IOllamaClient
{
    public Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(embeddingToReturn);

    public IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
