using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.Shared.Tests.Reranking;

public sealed class RerankingServiceTests
{
    private static RerankCandidate Candidate(string title, double vectorScore, string? text = null)
        => new(Guid.NewGuid(), title, $"https://example.com/{title}", text ?? $"text for {title}", vectorScore);

    private static RerankingService CreateService(IReranker reranker, RerankOptions? options = null)
        => new(reranker, Options.Create(options ?? new RerankOptions()), NullLogger<RerankingService>.Instance);

    [Fact]
    public async Task RerankAsync_ReordersByScore()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);
        var c = Candidate("C", 0.7);

        // Reranker inverts the vector order: C is best, A is worst.
        var reranker = new FakeReranker(docs => [new(0, 0.2f), new(1, 0.5f), new(2, 0.9f)]);
        var options = new RerankOptions { TopN = 5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b, c], topK: 5);

        Assert.Equal(["C", "B", "A"], result.Items.Select(i => i.Candidate.Title));
        Assert.NotNull(result.Outcome);
        Assert.False(result.Outcome!.FellBack);
        Assert.Equal(3, result.Outcome.Kept);
    }

    [Fact]
    public async Task RerankAsync_CapsAtTopN()
    {
        var candidates = Enumerable.Range(0, 10).Select(i => Candidate($"doc{i}", 1.0 - i * 0.01)).ToList();
        var reranker = new FakeReranker(docs => docs.Select((_, i) => new RerankScore(i, 1f - i * 0.01f)).ToList());
        var options = new RerankOptions { TopN = 3 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", candidates, topK: 10);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(3, result.Outcome!.Kept);
    }

    [Fact]
    public async Task RerankAsync_TopKSmallerThanTopNCaps()
    {
        var candidates = Enumerable.Range(0, 10).Select(i => Candidate($"doc{i}", 1.0 - i * 0.01)).ToList();
        var reranker = new FakeReranker(docs => docs.Select((_, i) => new RerankScore(i, 1f - i * 0.01f)).ToList());
        var options = new RerankOptions { TopN = 5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", candidates, topK: 2);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task RerankAsync_ExcludesBelowMinScore()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);
        var c = Candidate("C", 0.7);

        var reranker = new FakeReranker(docs => [new(0, 0.9f), new(1, 0.05f), new(2, 0.5f)]);
        var options = new RerankOptions { TopN = 5, MinScore = 0.1 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b, c], topK: 5);

        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, i => i.Candidate.Title == "B");
    }

    [Fact]
    public async Task RerankAsync_AllBelowMinScore_ReturnsEmptyWithOutcome()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new FakeReranker(docs => [new(0, 0.01f), new(1, 0.02f)]);
        var options = new RerankOptions { TopN = 5, MinScore = 0.5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 5);

        Assert.Empty(result.Items);
        Assert.NotNull(result.Outcome);
        Assert.Equal(0, result.Outcome!.Kept);
    }

    [Fact]
    public async Task RerankAsync_DedupsIdenticalText()
    {
        var a = Candidate("A", 0.9, text: "  Hello   World  ");
        var b = Candidate("B", 0.8, text: "hello world");
        var c = Candidate("C", 0.7, text: "different text");

        List<string>? seenDocs = null;
        var reranker = new FakeReranker(docs =>
        {
            seenDocs = docs.ToList();
            return docs.Select((_, i) => new RerankScore(i, 1f - i * 0.1f)).ToList();
        });
        var options = new RerankOptions { TopN = 5 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b, c], topK: 5);

        Assert.Equal(2, seenDocs!.Count);
        Assert.Equal(2, result.Outcome!.CandidatesIn);
        // "A" wins the dedup slot since it was first / best vector rank.
        Assert.Contains(result.Items, i => i.Candidate.Title == "A");
        Assert.DoesNotContain(result.Items, i => i.Candidate.Title == "B");
    }

    [Fact]
    public async Task RerankAsync_EmptyCandidates_ReturnsEmptyNoOutcome()
    {
        var reranker = new FakeReranker(docs => []);
        var service = CreateService(reranker);

        var result = await service.RerankAsync("query", [], topK: 5);

        Assert.Empty(result.Items);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public async Task RerankAsync_FewerCandidatesThanTopN_KeepsAllAndReorders()
    {
        var a = Candidate("A", 0.9);
        var b = Candidate("B", 0.8);

        var reranker = new FakeReranker(docs => [new(0, 0.1f), new(1, 0.9f)]);
        var options = new RerankOptions { TopN = 10 };
        var service = CreateService(reranker, options);

        var result = await service.RerankAsync("query", [a, b], topK: 10);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(["B", "A"], result.Items.Select(i => i.Candidate.Title));
    }

    internal sealed class FakeReranker(Func<IReadOnlyList<string>, IReadOnlyList<RerankScore>> score) : IReranker
    {
        public Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
            => Task.FromResult(score(documents));
    }
}
