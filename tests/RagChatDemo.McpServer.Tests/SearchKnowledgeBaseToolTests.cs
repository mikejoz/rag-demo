using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using RagChatDemo.McpServer.Tools;
using RagChatDemo.Shared.Data;

namespace RagChatDemo.McpServer.Tests;

/// <summary>
/// Integration test against a real Postgres+pgvector instance (see repo memory /
/// quickstart.md for how to start one locally). Not a pure unit test.
/// </summary>
public class SearchKnowledgeBaseToolTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=ragchatdemo;Username=postgres;Password=postgres";

    private RagChatDemoDbContext _db = null!;
    private Guid _documentId;
    private Guid _chunkId;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<RagChatDemoDbContext>()
            .UseNpgsql(ConnectionString, npgsql => npgsql.UseVector())
            .Options;
        _db = new RagChatDemoDbContext(options);

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
        var tool = new SearchKnowledgeBaseTool(_db, new FakeOllamaClient(sameEmbedding));

        var result = await tool.SearchKnowledgeBaseAsync("How do I reset my VPN password?", topK: 5);

        Assert.NotEmpty(result.Results);
        var topHit = result.Results[0];
        Assert.Equal(_chunkId, topHit.ChunkId);
        Assert.Equal("VPN Password Reset", topHit.DocumentTitle);
        Assert.Equal("https://example.com/vpn-reset", topHit.SourceUrl);
        Assert.True(topHit.Score > 0.99);
    }

    // A non-uniform-component vector, so it won't collide (same cosine direction) with other
    // all-equal-component test/seed vectors that might exist in the shared dev database.
    private static float[] CreateTestEmbedding() =>
        [.. Enumerable.Range(0, Chunk.EmbeddingDimensions).Select(i => (float)Math.Sin(i * 0.37))];
}
