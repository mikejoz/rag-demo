using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using RagChatDemo.IngestionWorker.Confluence;
using RagChatDemo.Shared.Activity;
using RagChatDemo.Shared.Chunking;
using RagChatDemo.Shared.Contracts;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Ollama;

namespace RagChatDemo.IngestionWorker.Jobs;

/// <summary>Fetches knowledge-base pages, chunks + embeds new/changed ones, and upserts them into pgvector.</summary>
public partial class IngestionJob(
    IConfluenceClient confluenceClient,
    RagChatDemoDbContext db,
    IOllamaClient ollama,
    IActivityPublisher activityPublisher)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var pages = await confluenceClient.GetPagesAsync(cancellationToken);
        foreach (var page in pages)
        {
            await ProcessPageAsync(page, cancellationToken);
        }
    }

    private async Task ProcessPageAsync(ConfluencePage page, CancellationToken cancellationToken)
    {
        await PublishAsync("fetch_page", ActivityStatus.Started, page.Title, null, cancellationToken);

        var existing = await db.SourceDocuments
            .Include(d => d.Chunks)
            .FirstOrDefaultAsync(d => d.ExternalId == page.ExternalId, cancellationToken);

        if (existing is not null && existing.LastModifiedAt == page.LastModifiedAt)
        {
            await PublishAsync("fetch_page", ActivityStatus.Succeeded, page.Title, "Unchanged, skipped", cancellationToken);
            return;
        }

        var plainText = StripHtml(page.HtmlContent);
        var chunkTexts = TextChunker.Chunk(plainText);
        await PublishAsync("chunk", ActivityStatus.Succeeded, page.Title, $"{chunkTexts.Count} chunk(s)", cancellationToken);

        var embeddings = new List<Vector>(chunkTexts.Count);
        foreach (var chunkText in chunkTexts)
        {
            embeddings.Add(await ollama.GetEmbeddingAsync(chunkText, cancellationToken));
        }

        await PublishAsync("embed", ActivityStatus.Succeeded, page.Title, $"{embeddings.Count} embedding(s)", cancellationToken);

        if (existing is not null)
        {
            // RemoveRange already marks these for deletion; don't also call existing.Chunks.Clear()
            // (navigation fixup would re-stage the same deletes and trip EF's concurrency check).
            db.Chunks.RemoveRange(existing.Chunks.ToList());
            existing.Title = page.Title;
            existing.Content = plainText;
            existing.SourceUrl = page.WebUrl;
            existing.LastModifiedAt = page.LastModifiedAt;
            existing.IngestedAt = DateTimeOffset.UtcNow;
            AddChunks(db, existing, chunkTexts, embeddings);
        }
        else
        {
            var document = new SourceDocument
            {
                Id = Guid.NewGuid(),
                ExternalId = page.ExternalId,
                Title = page.Title,
                Content = plainText,
                SourceUrl = page.WebUrl,
                LastModifiedAt = page.LastModifiedAt,
                IngestedAt = DateTimeOffset.UtcNow,
            };
            AddChunks(db, document, chunkTexts, embeddings);
            db.SourceDocuments.Add(document);
        }

        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync("upsert", ActivityStatus.Succeeded, page.Title, $"{chunkTexts.Count} chunk(s) stored", cancellationToken);
    }

    private static void AddChunks(
        RagChatDemoDbContext db, SourceDocument document, IReadOnlyList<string> chunkTexts, IReadOnlyList<Vector> embeddings)
    {
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            // db.Chunks.Add (not just document.Chunks.Add) forces EntityState.Added explicitly;
            // otherwise EF's change detection sees a pre-assigned non-default Guid key on a child
            // discovered via navigation fixup and assumes it already exists, generating an UPDATE.
            var chunk = new Chunk
            {
                Id = Guid.NewGuid(),
                SourceDocumentId = document.Id,
                Ordinal = i,
                Text = chunkTexts[i],
                Embedding = embeddings[i],
            };
            document.Chunks.Add(chunk);
            db.Chunks.Add(chunk);
        }
    }

    private static string StripHtml(string html)
    {
        var withoutTags = HtmlTagRegex().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    private Task PublishAsync(
        string operation, ActivityStatus status, string target, string? detail, CancellationToken cancellationToken) =>
        activityPublisher.PublishAsync(
            new ActivityEventDto
            {
                Id = Guid.NewGuid(),
                Category = ActivityCategory.Ingestion,
                Source = "IngestionWorker",
                Operation = operation,
                Target = target,
                Status = status,
                Detail = detail,
                Timestamp = DateTimeOffset.UtcNow,
            },
            cancellationToken);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
