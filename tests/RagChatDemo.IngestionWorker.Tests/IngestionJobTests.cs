using Microsoft.EntityFrameworkCore;
using RagChatDemo.IngestionWorker.Confluence;
using RagChatDemo.IngestionWorker.Jobs;
using RagChatDemo.Shared.Data;

namespace RagChatDemo.IngestionWorker.Tests;

/// <summary>
/// Integration test against a real Postgres+pgvector instance (see repo memory /
/// quickstart.md for how to start one locally). Not a pure unit test.
/// </summary>
public class IngestionJobTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=ragchatdemo;Username=postgres;Password=postgres";

    private readonly string externalId = $"ingestion-test-{Guid.NewGuid()}";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CreateDbContext();
        var doc = await db.SourceDocuments.Include(d => d.Chunks).FirstOrDefaultAsync(d => d.ExternalId == externalId);
        if (doc is not null)
        {
            db.SourceDocuments.Remove(doc);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RunAsync_NewPage_CreatesDocumentAndChunks()
    {
        var page = new ConfluencePage(
            externalId, "Test Doc", "<p>Some content to chunk.</p>", "https://example.com/doc", Truncated(DateTimeOffset.UtcNow));

        await using (var db = CreateDbContext())
        {
            await CreateJob(db, page).RunAsync();
        }

        await using var verifyDb = CreateDbContext();
        var doc = await verifyDb.SourceDocuments.Include(d => d.Chunks).SingleAsync(d => d.ExternalId == externalId);
        var chunk = Assert.Single(doc.Chunks);
        Assert.Equal("Some content to chunk.", chunk.Text);
    }

    [Fact]
    public async Task RunAsync_UnchangedPage_IsNoOp()
    {
        var page = new ConfluencePage(
            externalId, "Test Doc", "<p>Some content to chunk.</p>", "https://example.com/doc", Truncated(DateTimeOffset.UtcNow));

        await using (var db = CreateDbContext())
        {
            await CreateJob(db, page).RunAsync();
        }

        Guid firstChunkId;
        await using (var verifyDb = CreateDbContext())
        {
            var doc = await verifyDb.SourceDocuments.Include(d => d.Chunks).SingleAsync(d => d.ExternalId == externalId);
            firstChunkId = doc.Chunks.Single().Id;
        }

        // Re-ingest the exact same page (same LastModifiedAt) - must be a no-op (FR-013).
        await using (var db = CreateDbContext())
        {
            await CreateJob(db, page).RunAsync();
        }

        await using var finalDb = CreateDbContext();
        var finalDoc = await finalDb.SourceDocuments.Include(d => d.Chunks).SingleAsync(d => d.ExternalId == externalId);
        var finalChunk = Assert.Single(finalDoc.Chunks);
        Assert.Equal(firstChunkId, finalChunk.Id);
    }

    [Fact]
    public async Task RunAsync_EditedPage_ReplacesChunks()
    {
        var firstModified = Truncated(DateTimeOffset.UtcNow);
        var firstPage = new ConfluencePage(externalId, "Test Doc", "<p>Original content.</p>", "https://example.com/doc", firstModified);

        await using (var db = CreateDbContext())
        {
            await CreateJob(db, firstPage).RunAsync();
        }

        Guid firstChunkId;
        await using (var verifyDb = CreateDbContext())
        {
            var doc = await verifyDb.SourceDocuments.Include(d => d.Chunks).SingleAsync(d => d.ExternalId == externalId);
            firstChunkId = doc.Chunks.Single().Id;
        }

        var secondModified = firstModified.AddMinutes(1);
        var secondPage = new ConfluencePage(
            externalId, "Test Doc", "<p>Edited content, now different.</p>", "https://example.com/doc", secondModified);

        await using (var db = CreateDbContext())
        {
            await CreateJob(db, secondPage).RunAsync();
        }

        await using var finalDb = CreateDbContext();
        var finalDoc = await finalDb.SourceDocuments.Include(d => d.Chunks).SingleAsync(d => d.ExternalId == externalId);
        var finalChunk = Assert.Single(finalDoc.Chunks);
        Assert.NotEqual(firstChunkId, finalChunk.Id);
        Assert.Equal("Edited content, now different.", finalChunk.Text);
        Assert.Equal(secondModified, finalDoc.LastModifiedAt);
    }

    private static RagChatDemoDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<RagChatDemoDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options);

    // Postgres' timestamptz column has microsecond precision; .NET DateTimeOffset.UtcNow has
    // tick (100ns) precision, so untruncated values would never compare equal after a round-trip.
    private static DateTimeOffset Truncated(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMillisecond), value.Offset);

    private static IngestionJob CreateJob(RagChatDemoDbContext db, params ConfluencePage[] pages) =>
        new(new FakeConfluenceClient(pages), db, new FakeOllamaClient(), new NoOpActivityPublisher());
}
