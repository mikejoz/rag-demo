namespace RagChatDemo.Shared.Contracts;

public record KnowledgeBaseSearchHit(Guid ChunkId, string DocumentTitle, string SourceUrl, double Score, string Text);

public record SearchKnowledgeBaseResult(IReadOnlyList<KnowledgeBaseSearchHit> Results);

public record CreateSupportTicketResult(string TicketId, string Status);
