using Pgvector;

namespace RagChatDemo.Shared.Data;

/// <summary>A segment of a <see cref="SourceDocument"/>'s text with its embedding, used for retrieval.</summary>
public class Chunk
{
    public const int EmbeddingDimensions = 768;

    public Guid Id { get; set; }

    public Guid SourceDocumentId { get; set; }

    public SourceDocument? SourceDocument { get; set; }

    /// <summary>Position within the document (for ordering/debug).</summary>
    public int Ordinal { get; set; }

    public required string Text { get; set; }

    /// <summary>nomic-embed-text output (768 dimensions), HNSW cosine index.</summary>
    public required Vector Embedding { get; set; }
}
