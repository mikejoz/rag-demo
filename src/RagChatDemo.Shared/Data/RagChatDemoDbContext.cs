using Microsoft.EntityFrameworkCore;

namespace RagChatDemo.Shared.Data;

public class RagChatDemoDbContext(DbContextOptions<RagChatDemoDbContext> options) : DbContext(options)
{
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();

    public DbSet<Chunk> Chunks => Set<Chunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.HasIndex(d => d.ExternalId).IsUnique();
            entity.HasMany(d => d.Chunks)
                .WithOne(c => c.SourceDocument)
                .HasForeignKey(c => c.SourceDocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Chunk>(entity =>
        {
            entity.Property(c => c.Embedding).HasColumnType($"vector({Chunk.EmbeddingDimensions})");
            entity.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });
    }
}
