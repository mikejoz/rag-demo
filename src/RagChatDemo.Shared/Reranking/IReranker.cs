namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// Reranks candidate documents against a query, producing relevance scores.
/// </summary>
public interface IReranker
{
    /// <summary>
    /// Scores the given documents against the query. Scores are in the range 0-1, with higher
    /// values indicating greater relevance. Implementations may omit documents from the result.
    /// </summary>
    Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default);
}

/// <summary>
/// A relevance score for a document, identified by its original index in the input list.
/// </summary>
public readonly record struct RerankScore(int Index, float Score);
