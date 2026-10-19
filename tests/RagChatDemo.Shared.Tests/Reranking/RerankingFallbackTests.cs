using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.Shared.Tests.Reranking;

public sealed class RerankingFallbackTests
{
    private static RerankCandidate Candidate(string title, double vectorScore)
        => new(Guid.NewGuid(), title, $"https://example.com/{title}", $"text for {title}", vectorScore);

    private static RerankingService CreateService(IReranker reranker, RerankOptions? options = null)
        => new(reranker, Options.Create(options ?? new RerankOptions()), NullLogger<RerankingService>.Instance);

    [Fact]
    public async Task RerankAsync_HttpRequestException_FallsBackToVectorOrder()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new ThrowingReranker(() => throw new HttpRequestException("boom"));
        var options = new RerankOptions { TopN = 5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 5);

        Assert.Equal(["A", "B"], result.Items.Select(i => i.Candidate.Title));
        Assert.NotNull(result.Outcome);
        Assert.True(result.Outcome!.FellBack);
        Assert.Equal("boom", result.Outcome.FallbackReason);
    }

    [Fact]
    public async Task RerankAsync_Timeout_FallsBackWithTimeoutReason()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new DelayingReranker();
        var options = new RerankOptions { TopN = 5, TimeoutSeconds = 1 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 5);

        Assert.Equal(["A", "B"], result.Items.Select(i => i.Candidate.Title));
        Assert.NotNull(result.Outcome);
        Assert.True(result.Outcome!.FellBack);
        Assert.Equal("timeout", result.Outcome.FallbackReason);
    }

    [Fact]
    public async Task RerankAsync_MalformedResponse_FallsBack()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new ThrowingReranker(() => throw new InvalidOperationException("bad payload"));
        var options = new RerankOptions { TopN = 5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 5);

        Assert.Equal(["A", "B"], result.Items.Select(i => i.Candidate.Title));
        Assert.True(result.Outcome!.FellBack);
        Assert.Equal("bad payload", result.Outcome.FallbackReason);
    }

    [Fact]
    public async Task RerankAsync_Disabled_RerankerNeverCalledAndNoOutcome()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var called = false;
        var reranker = new ThrowingReranker(() =>
        {
            called = true;
            throw new InvalidOperationException("should not be called");
        });
        var options = new RerankOptions { Enabled = false };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 5);

        Assert.False(called);
        Assert.Null(result.Outcome);
        Assert.Equal(["A", "B"], result.Items.Select(i => i.Candidate.Title));
    }

    [Fact]
    public async Task RerankAsync_CallerCancellation_Propagates()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new DelayingReranker(TimeSpan.FromSeconds(10));
        var options = new RerankOptions { TopN = 5, TimeoutSeconds = 30 };
        var service = CreateService(reranker, options);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        var ex = await Record.ExceptionAsync(() => service.RerankAsync("query", [a, b], topK: 5, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    internal sealed class ThrowingReranker(Action onCall) : IReranker
    {
        public Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
        {
            onCall();
            throw new InvalidOperationException("unreachable");
        }
    }

    internal sealed class DelayingReranker(TimeSpan? delay = null) : IReranker
    {
        public async Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay ?? TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            return [];
        }
    }
}
