using System.ComponentModel;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Pgvector.EntityFrameworkCore;
using RagChatDemo.IngestionWorker.Confluence;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.McpServer.Tools;

[McpServerToolType]
public sealed class SearchKnowledgeBaseTool(
    RagChatDemoDbContext db,
    IOllamaClient ollama,
    IConfluenceClient confluence,
    RerankingService reranking,
    ILogger<SearchKnowledgeBaseTool> logger,
    IOptions<RerankOptions> rerankOptions)
{
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    [McpServerTool(Name = "search_knowledge_base")]
    [Description("Searches the knowledge base for chunks of text relevant to a natural language query.")]
    public async Task<SearchKnowledgeBaseResult> SearchKnowledgeBaseAsync(
        [Description("Natural language question or topic to search for.")] string query,
        [Description("Maximum number of chunks to return.")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await ollama.GetEmbeddingAsync(query, cancellationToken);

        const float defaultMaxCosineDistance = 0.4f; // minimum similarity threshold: 1 - 0.4 = 0.6 (60%)

        var maxCosineDistance = rerankOptions.Value.Enabled
            ? (float)rerankOptions.Value.CandidateMaxCosineDistance
            : defaultMaxCosineDistance;

        var candidateCount = rerankOptions.Value.Enabled
            ? Math.Max(topK, rerankOptions.Value.CandidateCount)
            : topK;

        var hits = await db.Chunks
            .Where(c => c.Embedding.CosineDistance(queryEmbedding) <= maxCosineDistance)
            .OrderBy(c => c.Embedding.CosineDistance(queryEmbedding))
            .Take(candidateCount)
            .Select(c => new
            {
                ChunkId = c.Id,
                CachedText = c.Text,
                c.SourceDocument!.ExternalId,
                c.SourceDocument.Title,
                c.SourceDocument.SourceUrl,
                Distance = c.Embedding.CosineDistance(queryEmbedding),
            })
            .ToListAsync(cancellationToken);

        var candidates = hits.Select(h => new RerankCandidate(h.ChunkId, h.Title, h.SourceUrl, h.CachedText, 1 - h.Distance)).ToList();

        var rerankResult = await reranking.RerankAsync(query, candidates, topK, cancellationToken);
        var kept = rerankResult.Items;

        if (kept.Count == 0)
        {
            return new SearchKnowledgeBaseResult([], rerankResult.Outcome);
        }

        var chunkIdToExternalId = hits.ToDictionary(h => h.ChunkId, h => h.ExternalId);

        // Collect unique page IDs for live fetch (avoid duplicate calls for hits from the same page)
        var pageIdsToFetch = kept.Select(k => chunkIdToExternalId[k.Candidate.ChunkId]).Distinct().ToList();
        var freshPages = new Dictionary<string, string>();

        foreach (var pageId in pageIdsToFetch)
        {
            try
            {
                logger.LogInformation("Fetching live content for page {PageId}", pageId);
                var livePage = await confluence.GetPageByIdAsync(pageId, cancellationToken);
                if (livePage is not null)
                {
                    freshPages[pageId] = StripHtml(livePage.HtmlContent);
                    logger.LogInformation("Live content fetched for {PageId} ({Length} chars)", pageId, freshPages[pageId].Length);
                }
                else
                {
                    logger.LogWarning("Confluence returned null for page {PageId}, falling back to cached text", pageId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch live content for page {PageId}, falling back to cached text", pageId);
            }
        }

        var freshCount = freshPages.Count;
        if (freshCount > 0 || pageIdsToFetch.Count > 0)
        {
            logger.LogInformation("Live refresh: {Fresh}/{Total} pages updated from Confluence", freshCount, pageIdsToFetch.Count);
        }

        var results = kept
            .Select(item =>
            {
                // Use fresh content if available, otherwise fall back to cached chunk text
                var externalId = chunkIdToExternalId[item.Candidate.ChunkId];
                string text = freshPages.TryGetValue(externalId, out var liveContent)
                    ? liveContent
                    : item.Candidate.Text;

                return new KnowledgeBaseSearchHit(item.Candidate.ChunkId, item.Candidate.Title, item.Candidate.SourceUrl, item.Score, text);
            })
            .ToArray();

        return new SearchKnowledgeBaseResult(results, rerankResult.Outcome);
    }

    private static string StripHtml(string html)
    {
        var withoutTags = HtmlTagRegex.Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex.Replace(decoded, " ").Trim();
    }
}
