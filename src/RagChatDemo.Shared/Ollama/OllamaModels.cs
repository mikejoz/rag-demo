namespace RagChatDemo.Shared.Ollama;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public required string BaseUrl { get; set; }

    public string EmbedModel { get; set; } = "nomic-embed-text";

    public string ChatModel { get; set; } = "qwen3.6:27b";
}

public interface IOllamaClient
{
    Task<Pgvector.Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        CancellationToken cancellationToken = default);
}

public record OllamaChatMessage(string Role, string Content);

public record OllamaToolDefinition(string Name, string Description, object Parameters)
{
    public string Type => "function";
}

public record OllamaToolCall(string Name, string ArgumentsJson);

/// <summary>One NDJSON line from Ollama's streaming <c>/api/chat</c> response.</summary>
public record OllamaChatStreamChunk(string? ContentToken, bool Done, IReadOnlyList<OllamaToolCall>? ToolCalls = null);
