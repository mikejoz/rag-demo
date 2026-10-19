using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// An <see cref="IReranker"/> backed by a self-hosted Text Embeddings Inference (TEI) rerank endpoint.
/// </summary>
public sealed class TeiReranker(HttpClient http) : IReranker
{
    public async Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
        {
            return [];
        }

        var request = new TeiRerankRequest(query, documents, true);
        using var response = await http.PostAsJsonAsync("rerank", request, TeiJsonContext.Default.TeiRerankRequest, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        TeiRerankResponseItem[]? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync(TeiJsonContext.Default.TeiRerankResponseItemArray, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("TEI rerank response body was not a valid array.", ex);
        }

        if (payload is null)
        {
            throw new InvalidOperationException("TEI rerank response body was not a valid array.");
        }

        var scores = new RerankScore[payload.Length];
        for (var i = 0; i < payload.Length; i++)
        {
            var item = payload[i];
            if (item is null)
            {
                throw new InvalidOperationException("TEI rerank response contained a null entry.");
            }

            if (item.Index < 0 || item.Index >= documents.Count)
            {
                throw new InvalidOperationException($"TEI rerank response index {item.Index} is out of range for {documents.Count} documents.");
            }

            if (item.Score is null)
            {
                throw new InvalidOperationException("TEI rerank response entry was missing a score.");
            }

            scores[i] = new RerankScore(item.Index, item.Score.Value);
        }

        return scores;
    }
}

internal sealed record TeiRerankRequest(string Query, IReadOnlyList<string> Texts, bool Truncate);

internal sealed record TeiRerankResponseItem(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("score")] float? Score);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TeiRerankRequest))]
[JsonSerializable(typeof(TeiRerankResponseItem[]))]
internal sealed partial class TeiJsonContext : JsonSerializerContext
{
}
