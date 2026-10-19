using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.Shared.Tests;

public sealed class CohereRerankerTests
{
    private static CohereReranker CreateReranker(FakeHttpMessageHandler handler, string? apiKey = "test-key", string baseUrl = "https://fake.cohere.test/")
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        var options = Options.Create(new RerankOptions
        {
            Cohere = new CohereRerankOptions { ApiKey = apiKey, Model = "rerank-v3.5", BaseUrl = baseUrl },
        });
        return new CohereReranker(http, options);
    }

    [Fact]
    public async Task RerankAsync_SendsCorrectRequest_AndParsesScores()
    {
        var responseJson = """{"results":[{"index":2,"relevance_score":0.98},{"index":0,"relevance_score":0.2}]}""";
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
        });
        var reranker = CreateReranker(handler);

        var result = await reranker.RerankAsync("what is rag?", new[] { "doc a", "doc b", "doc c" });

        Assert.NotNull(handler.CapturedRequest);
        Assert.Contains("v2/rerank", handler.CapturedRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.CapturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("test-key", handler.CapturedRequest.Headers.Authorization.Parameter);

        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var root = doc.RootElement;
        Assert.Equal("rerank-v3.5", root.GetProperty("model").GetString());
        Assert.Equal("what is rag?", root.GetProperty("query").GetString());
        Assert.Equal(3, root.GetProperty("documents").GetArrayLength());
        Assert.Equal(3, root.GetProperty("top_n").GetInt32());

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Index);
        Assert.Equal(0.98f, result[0].Score);
        Assert.Equal(0, result[1].Index);
        Assert.Equal(0.2f, result[1].Score);
    }

    [Fact]
    public async Task RerankAsync_WithEmptyDocuments_ReturnsEmptyWithoutCallingHttp()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var reranker = CreateReranker(handler);

        var result = await reranker.RerankAsync("q", Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task RerankAsync_WithMissingApiKey_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("should not be called"));
        var reranker = CreateReranker(handler, apiKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reranker.RerankAsync("q", new[] { "d" }));
    }

    [Fact]
    public async Task RerankAsync_WithNonSuccessStatus_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(string.Empty),
        });
        var reranker = CreateReranker(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => reranker.RerankAsync("q", new[] { "d" }));
    }

    [Fact]
    public async Task RerankAsync_WithMalformedResponseBody_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"foo":"bar"}""", System.Text.Encoding.UTF8, "application/json"),
        });
        var reranker = CreateReranker(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reranker.RerankAsync("q", new[] { "d" }));
    }

    [Fact]
    public async Task RerankAsync_WithOutOfRangeIndex_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"results":[{"index":99,"relevance_score":0.5}]}""", System.Text.Encoding.UTF8, "application/json"),
        });
        var reranker = CreateReranker(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reranker.RerankAsync("q", new[] { "d" }));
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? CapturedRequest;
        public string? CapturedBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = request;
            CapturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return responder(request);
        }
    }
}
