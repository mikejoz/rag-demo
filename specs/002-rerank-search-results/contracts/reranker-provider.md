# Re-ranker Provider Contract

## .NET abstraction
`IReranker.RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken ct)` →
`IReadOnlyList<(int Index, float Score)>` (scores 0–1, higher is better; may omit documents).

## Local provider: TEI (default)
`POST {BaseUrl}/rerank`
```json
{ "query": "string", "texts": ["..."], "truncate": true }
```
Response: `[ { "index": 2, "score": 0.98 }, ... ]` sorted by score descending.

## Hosted provider: Cohere (opt-in)
`POST https://api.cohere.com/v2/rerank` with `Authorization: Bearer <key from secret>`
```json
{ "model": "rerank-v3.5", "query": "string", "documents": ["..."], "top_n": 5 }
```
Response `results: [ { "index": 2, "relevance_score": 0.98 } ]`.

## None provider
Returns documents in input order with score = 1; used when disabled or as the test stub.

## Error rules
Non-2xx, timeout, or unparsable body → throw; `RerankingService` catches and falls back (FR-008).
