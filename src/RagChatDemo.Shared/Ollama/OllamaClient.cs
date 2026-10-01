using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Pgvector;

namespace RagChatDemo.Shared.Ollama;

/// <summary>Thin wrapper over Ollama's HTTP API (embeddings + streaming chat).</summary>
public class OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Vector> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new EmbeddingRequest(options.Value.EmbedModel, text);
        using var response = await httpClient.PostAsJsonAsync("/api/embeddings", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOptions, cancellationToken);
        return new Vector(body?.Embedding ?? throw new InvalidOperationException("Ollama returned no embedding."));
    }

    public async IAsyncEnumerable<OllamaChatStreamChunk> StreamChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        string? model = null,
        IReadOnlyList<OllamaToolDefinition>? tools = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(
            model ?? options.Value.ChatModel,
            messages,
            true,
            tools?.Select(t => new ToolRequest(new ToolFunctionRequest(t.Name, t.Description, t.Parameters))).ToArray());

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(
            httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 } line)
        {
            var chunk = JsonSerializer.Deserialize<ChatResponseLine>(line, JsonOptions);
            if (chunk is null)
            {
                continue;
            }

            var toolCalls = chunk.Message?.ToolCalls?
                .Select(tc => new OllamaToolCall(tc.Function.Name, tc.Function.Arguments.GetRawText()))
                .ToArray();
            yield return new OllamaChatStreamChunk(chunk.Message?.Content, chunk.Done, toolCalls);
        }
    }

    private record EmbeddingRequest(string Model, string Prompt);

    private record EmbeddingResponse(float[] Embedding);

    private record ChatRequest(string Model, IReadOnlyList<OllamaChatMessage> Messages, bool Stream, ToolRequest[]? Tools);

    private record ToolRequest(ToolFunctionRequest Function)
    {
        public string Type => "function";
    }

    private record ToolFunctionRequest(string Name, string Description, object Parameters);

    private record ChatResponseLine(ChatResponseMessage? Message, bool Done);

    private record ChatResponseMessage(string? Content, [property: JsonPropertyName("tool_calls")] ToolCallLine[]? ToolCalls);

    private record ToolCallLine(ToolCallFunction Function);

    private record ToolCallFunction(string Name, JsonElement Arguments);
}
