using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// A reranker backed by the Cohere rerank API.
/// </summary>
public sealed class CohereReranker(HttpClient http, IOptions<RerankOptions> options) : IReranker
{
    public async Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
    {
        if (documents.Count == 0)
        {
            return Array.Empty<RerankScore>();
        }

        var apiKey = options.Value.Cohere.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Cohere API key is not configured");
        }

        var model = options.Value.Cohere.Model;

        var requestBody = new CohereRerankRequest(model, query, documents, documents.Count);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("v2/rerank", UriKind.Relative))
        {
            Content = JsonContent.Create(requestBody),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Cohere rerank request failed with status {(int)response.StatusCode}");
        }

        var parsed = await response.Content.ReadFromJsonAsync<CohereRerankResponse>(cancellationToken);
        if (parsed is null || parsed.Results is null)
        {
            throw new InvalidOperationException("Cohere rerank response was malformed");
        }

        var scores = new List<RerankScore>(parsed.Results.Count);
        foreach (var result in parsed.Results)
        {
            if (result.Index < 0 || result.Index >= documents.Count)
            {
                throw new InvalidOperationException($"Cohere rerank response contained an out-of-range document index: {result.Index}");
            }

            scores.Add(new RerankScore(result.Index, result.RelevanceScore));
        }

        return scores;
    }

    private sealed record CohereRerankRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("documents")] IReadOnlyList<string> Documents,
        [property: JsonPropertyName("top_n")] int TopN);

    private sealed record CohereRerankResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<CohereRerankResult>? Results);

    private sealed record CohereRerankResult(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("relevance_score")] float RelevanceScore);
}
