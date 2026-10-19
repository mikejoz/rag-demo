namespace RagChatDemo.Shared.Reranking;

/// <summary>
/// Configuration options for the reranking pipeline.
/// </summary>
public sealed class RerankOptions
{
    public const string SectionName = "Reranker";

    public bool Enabled { get; set; } = true;

    public string Provider { get; set; } = "Tei";

    public int CandidateCount { get; set; } = 20;

    public int TopN { get; set; } = 5;

    // Cross-encoder scores are low in absolute terms for paraphrased questions, so the default does not filter.
    public double MinScore { get; set; } = 0;

    // Wider than the non-reranked 0.4 cutoff so the re-ranker has real candidates to reorder.
    public double CandidateMaxCosineDistance { get; set; } = 0.5;

    public int TimeoutSeconds { get; set; } = 3;

    public TeiRerankOptions Tei { get; set; } = new();

    public CohereRerankOptions Cohere { get; set; } = new();
}

/// <summary>
/// Options for a self-hosted Text Embeddings Inference (TEI) reranker.
/// </summary>
public sealed class TeiRerankOptions
{
    public string BaseUrl { get; set; } = "http://reranker:80";
}

/// <summary>
/// Options for the Cohere rerank API.
/// </summary>
public sealed class CohereRerankOptions
{
    public string Model { get; set; } = "rerank-v3.5";

    // NOTE: ApiKey must be supplied only via user-secrets (local dev) or a Kubernetes Secret
    // (deployed environments). Never set or commit a literal value here.
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.cohere.com";
}
