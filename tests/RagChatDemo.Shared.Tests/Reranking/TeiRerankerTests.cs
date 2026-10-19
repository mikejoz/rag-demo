using System.Net;
using System.Text;
using System.Text.Json;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.Shared.Tests.Reranking;

public sealed class TeiRerankerTests
{
    private static HttpClient CreateClient(FakeHttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri("http://reranker:80/") };

    [Fact]
    public async Task RerankAsync_SendsExpectedRequest_AndParsesScores()
    {
        var responseJson = """[{"index":2,"score":0.98},{"index":0,"score":0.5}]""";
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, responseJson);
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        var result = await reranker.RerankAsync("my query", ["doc a", "doc b", "doc c"]);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("rerank", handler.LastRequest!.RequestUri!.AbsolutePath.TrimStart('/'));

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var root = doc.RootElement;
        Assert.Equal("my query", root.GetProperty("query").GetString());
        Assert.True(root.GetProperty("truncate").GetBoolean());
        var texts = root.GetProperty("texts").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["doc a", "doc b", "doc c"], texts);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Index == 2 && Math.Abs(s.Score - 0.98f) < 0.0001f);
        Assert.Contains(result, s => s.Index == 0 && Math.Abs(s.Score - 0.5f) < 0.0001f);
    }

    [Fact]
    public async Task RerankAsync_NonSuccessStatus_ThrowsHttpRequestException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError, "oops");
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => reranker.RerankAsync("q", ["a"]));
    }

    [Fact]
    public async Task RerankAsync_MalformedBody_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "{ not an array }");
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("q", ["a"]));
    }

    [Fact]
    public async Task RerankAsync_IndexOutOfRange_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """[{"index":5,"score":0.9}]""");
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("q", ["a"]));
    }

    [Fact]
    public async Task RerankAsync_MissingScore_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """[{"index":0}]""");
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reranker.RerankAsync("q", ["a"]));
    }

    [Fact]
    public async Task RerankAsync_EmptyDocuments_MakesNoCall()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = CreateClient(handler);
        var reranker = new TeiReranker(client);

        var result = await reranker.RerankAsync("q", []);

        Assert.Empty(result);
        Assert.Null(handler.LastRequest);
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
