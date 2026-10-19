namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// An <see cref="IReranker"/> that performs no reranking, returning every document in its
/// original order with a maximum score.
/// </summary>
public sealed class PassThroughReranker : IReranker
{
    public Task<IReadOnlyList<RerankScore>> RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken cancellationToken = default)
    {
        var scores = new RerankScore[documents.Count];
        for (var i = 0; i < documents.Count; i++)
        {
            scores[i] = new RerankScore(i, 1f);
        }

        return Task.FromResult<IReadOnlyList<RerankScore>>(scores);
    }
}
