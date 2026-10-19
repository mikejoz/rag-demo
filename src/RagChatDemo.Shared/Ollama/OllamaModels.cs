namespace RagChatDemo.Shared.Ollama;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public required string BaseUrl { get; set; }

    public string EmbedModel { get; set; } = "nomic-embed-text";

    public string ChatModel { get; set; } = "qwen3.6:27b";

    public bool Think { get; set; } = false;
}

public interface IOllamaClient
{
    Task<Pgvector.Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat response from Ollama.
    /// </summary>
    /// <param name="messages">Conversation messages.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="tools">Optional tool definitions.</param>
    /// <param name="think">Whether to enable thinking mode. Null means use the value from OllamaOptions.Think.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        bool? think = null,
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
