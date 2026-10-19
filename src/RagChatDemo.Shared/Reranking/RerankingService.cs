using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Contracts;

namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// A vector-search candidate awaiting (optional) reranking.
/// </summary>
public sealed record RerankCandidate(Guid ChunkId, string Title, string SourceUrl, string Text, double VectorScore);

/// <summary>
/// A candidate paired with its final relevance score.
/// </summary>
public sealed record RerankedCandidate(RerankCandidate Candidate, double Score);

/// <summary>
/// The result of a (possibly skipped or fallen-back) reranking pass.
/// </summary>
public sealed record RerankingResult(IReadOnlyList<RerankedCandidate> Items, RerankOutcomeDto? Outcome);

/// <summary>
/// Orchestrates reranking of vector-search candidates: deduplication, scoring via an
/// <see cref="IReranker"/>, filtering, truncation, and graceful fallback on failure.
/// </summary>
public sealed class RerankingService(IReranker reranker, IOptions<RerankOptions> options, ILogger<RerankingService> logger)
{
    public async Task<RerankingResult> RerankAsync(string query, IReadOnlyList<RerankCandidate> candidates, int topK, CancellationToken ct = default)
    {
        if (candidates.Count == 0)
        {
            return new RerankingResult([], null);
        }

        var opts = options.Value;

        if (!opts.Enabled)
        {
            var passThrough = candidates
                .Take(topK)
                .Select(c => new RerankedCandidate(c, c.VectorScore))
                .ToList();
            return new RerankingResult(passThrough, null);
        }

        var deduped = Dedup(candidates);
        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(opts.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var texts = deduped.Select(c => c.Text).ToList();
            var scores = await reranker.RerankAsync(query, texts, linkedCts.Token).ConfigureAwait(false);

            var scored = new List<RerankedCandidate>(scores.Count);
            foreach (var score in scores)
            {
                if (score.Index < 0 || score.Index >= deduped.Count)
                {
                    continue;
                }

                if (score.Score < opts.MinScore)
                {
                    continue;
                }

                scored.Add(new RerankedCandidate(deduped[score.Index], score.Score));
            }

            var kept = scored
                .OrderByDescending(r => r.Score)
                .Take(Math.Min(topK, opts.TopN))
                .ToList();

            stopwatch.Stop();

            var keptIds = kept.Select(r => r.Candidate.ChunkId).ToHashSet();
            var beforeOrdered = deduped.Where(c => keptIds.Contains(c.ChunkId));

            var outcome = new RerankOutcomeDto(
                Provider: opts.Provider,
                CandidatesIn: deduped.Count,
                Kept: kept.Count,
                ElapsedMs: stopwatch.ElapsedMilliseconds,
                FellBack: false,
                FallbackReason: null,
                Before: beforeOrdered.Select(c => new RerankedItemDto(c.Title, c.VectorScore)).ToList(),
                After: kept.Select(r => new RerankedItemDto(r.Candidate.Title, r.Score)).ToList());

            return new RerankingResult(kept, outcome);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            stopwatch.Stop();

            var reason = ex is OperationCanceledException ? "timeout" : ex.Message;
            logger.LogWarning(ex, "Reranking failed; falling back to vector order. Reason: {Reason}", reason);

            var fallbackCount = Math.Min(topK, opts.TopN);
            var fallback = deduped
                .Take(fallbackCount)
                .Select(c => new RerankedCandidate(c, c.VectorScore))
                .ToList();

            var fallbackItems = fallback.Select(r => new RerankedItemDto(r.Candidate.Title, r.Score)).ToList();

            var outcome = new RerankOutcomeDto(
                Provider: opts.Provider,
                CandidatesIn: deduped.Count,
                Kept: fallback.Count,
                ElapsedMs: stopwatch.ElapsedMilliseconds,
                FellBack: true,
                FallbackReason: reason,
                Before: fallbackItems,
                After: fallbackItems);

            return new RerankingResult(fallback, outcome);
        }
    }

    private static List<RerankCandidate> Dedup(IReadOnlyList<RerankCandidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RerankCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var normalized = CollapseWhitespace(candidate.Text);
            if (seen.Add(normalized))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static string CollapseWhitespace(string text)
        => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
