namespace RagChatDemo.Shared.Contracts;

public record KnowledgeBaseSearchHit(Guid ChunkId, string DocumentTitle, string SourceUrl, double Score, string Text);

public record RerankedItemDto(string Title, double Score);

public record RerankOutcomeDto(
    string Provider,
    int CandidatesIn,
    int Kept,
    long ElapsedMs,
    bool FellBack,
    string? FallbackReason,
    IReadOnlyList<RerankedItemDto> Before,
    IReadOnlyList<RerankedItemDto> After);

public record SearchKnowledgeBaseResult(IReadOnlyList<KnowledgeBaseSearchHit> Results, RerankOutcomeDto? Rerank = null);

public record CreateSupportTicketResult(string TicketId, string Status);
