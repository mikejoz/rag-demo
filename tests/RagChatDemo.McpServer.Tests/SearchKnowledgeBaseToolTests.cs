using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RagChatDemo.IngestionWorker.Confluence;
using RagChatDemo.McpServer.Tools;
using RagChatDemo.Shared.Data;
using RagChatDemo.Shared.Reranking;

namespace RagChatDemo.McpServer.Tests;

internal sealed class FakeConfluenceClient(params ConfluencePage[] pages) : IConfluenceClient
{
    private readonly Dictionary<string, ConfluencePage> _pagesById = new(pages.ToDictionary(p => p.ExternalId));

    public Task<IReadOnlyList<ConfluencePage>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConfluencePage>>(pages);

    public Task<ConfluencePage?> GetPageByIdAsync(string externalId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pagesById.GetValueOrDefault(externalId));
}

/// <summary>
/// Integration test against a real Postgres+pgvector instance (see repo memory /
/// quickstart.md for how to start one locally). Not a pure unit test.
/// </summary>
// NOTE: An additional test exercising the reranker actually reordering results (e.g.
// SearchKnowledgeBaseAsync_WithRerankerReversingOrder_ReturnsRerankOutcome) was intentionally
// omitted. The IAsyncLifetime fixture below seeds only a single chunk, which is insufficient to
// observe a reorder between two or more candidates, and adding a second seeded chunk risks
// polluting/affecting the shared live Postgres+pgvector instance this integration test runs
// against.
public class SearchKnowledgeBaseToolTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("RAGCHATDEMO_TEST_CONNECTION_STRING")
        ?? throw new InvalidOperationException(
            "RAGCHATDEMO_TEST_CONNECTION_STRING environment variable is not set. " +
            "Set it to a PostgreSQL+pgvector connection string, e.g.: " +
            "'Host=localhost;Port=5432;Database=ragchatdemo_test;Username=postgres;Password=your_password'");
    private RagChatDemoDbContext _db = null!;
    private Guid _documentId;
    private Guid _chunkId;
    private FakeConfluenceClient _confluence = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<RagChatDemoDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;
        _db = new RagChatDemoDbContext(options);

        await _db.Database.MigrateAsync();

        _documentId = Guid.NewGuid();
        _chunkId = Guid.NewGuid();

        _db.SourceDocuments.Add(new SourceDocument
        {
            Id = _documentId,
            ExternalId = $"contract-test-{_documentId}",
            Title = "VPN Password Reset",
            Content = "irrelevant for this test",
            SourceUrl = "https://example.com/vpn-reset",
            LastModifiedAt = DateTimeOffset.UtcNow,
            IngestedAt = DateTimeOffset.UtcNow,
        });
        _db.Chunks.Add(new Chunk
        {
            Id = _chunkId,
            SourceDocumentId = _documentId,
            Ordinal = 0,
            Text = "To reset your VPN password, open the self-service portal and click 'Reset'.",
            Embedding = new Vector(CreateTestEmbedding()),
        });

        await _db.SaveChangesAsync();

        _confluence = new FakeConfluenceClient(new ConfluencePage(
            $"contract-test-{_documentId}",
            "VPN Password Reset (live)",
            "<p>To reset your VPN password, <strong>open the self-service portal</strong> and click 'Reset'.</p>",
            "https://example.com/vpn-reset",
            DateTimeOffset.UtcNow));
    }

    public async Task DisposeAsync()
    {
        _db.Chunks.RemoveRange(_db.Chunks.Where(c => c.SourceDocumentId == _documentId));
        _db.SourceDocuments.RemoveRange(_db.SourceDocuments.Where(d => d.Id == _documentId));
        await _db.SaveChangesAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task SearchKnowledgeBaseAsync_ReturnsSeededChunk_RankedFirst()
    {
        // The fake embedding matches the seeded chunk exactly (cosine distance 0), so it's
        // guaranteed to rank first regardless of whatever else is in the table.
        var sameEmbedding = new Vector(CreateTestEmbedding());
        var reranking = new RerankingService(
            new PassThroughReranker(),
            Options.Create(new RerankOptions()),
            NullLogger<RerankingService>.Instance);
        var tool = new SearchKnowledgeBaseTool(
            _db,
            new FakeOllamaClient(sameEmbedding),
            _confluence,
            reranking,
            NullLogger<SearchKnowledgeBaseTool>.Instance,
            Options.Create(new RerankOptions()));

        var result = await tool.SearchKnowledgeBaseAsync("How do I reset my VPN password?", topK: 5);

        Assert.NotEmpty(result.Results);
        var topHit = result.Results[0];
        Assert.Equal(_chunkId, topHit.ChunkId);
        Assert.True(topHit.Score > 0.99);
        // Content should be the LIVE version from Confluence (not the cached chunk text)
        Assert.Contains("self-service portal", topHit.Text);
        Assert.DoesNotContain("&lt;", topHit.Text);
    }

    // A non-uniform-component vector, so it won't collide (same cosine direction) with other
    // all-equal-component test/seed vectors that might exist in the shared dev database.
    private static float[] CreateTestEmbedding() =>
        [.. Enumerable.Range(0, Chunk.EmbeddingDimensions).Select(i => (float)Math.Sin(i * 0.37))];
}
