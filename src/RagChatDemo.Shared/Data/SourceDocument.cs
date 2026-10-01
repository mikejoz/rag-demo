namespace RagChatDemo.Shared.Data;

/// <summary>One knowledge-base page (real or stubbed Confluence content).</summary>
public class SourceDocument
{
    public Guid Id { get; set; }

    /// <summary>Source system's page id (Confluence page id).</summary>
    public required string ExternalId { get; set; }

    public required string Title { get; set; }

    /// <summary>Raw page body (plain text, HTML stripped at ingestion time).</summary>
    public required string Content { get; set; }

    public required string SourceUrl { get; set; }

    /// <summary>From source system; used to detect changes for re-ingestion.</summary>
    public required DateTimeOffset LastModifiedAt { get; set; }

    public DateTimeOffset IngestedAt { get; set; }

    public List<Chunk> Chunks { get; set; } = [];
}
