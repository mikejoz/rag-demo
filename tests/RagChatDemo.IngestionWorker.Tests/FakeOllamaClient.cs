using Pgvector;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.IngestionWorker.Tests;

/// <summary>Returns a fixed embedding regardless of input text; the ingestion tests only care about chunk/document upserts.</summary>
internal sealed class FakeOllamaClient : IOllamaClient
{
    public Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(new Vector(Enumerable.Repeat(0.01f, Chunk.EmbeddingDimensions).ToArray()));

    public IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
