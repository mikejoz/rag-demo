# MCP Tool Contract Changes

## `search_knowledge_base` (updated)

**Input**: unchanged (`query`, `top_k` default 5). `top_k` now caps the final re-ranked count.

**Output**: existing `results` array unchanged except `score` semantics:
- `score`: re-rank relevance (0–1) when re-ranking succeeded; cosine similarity when fell back or disabled.

**New optional field**:
```json
{
  "rerank": {
    "provider": "Tei | Cohere | None",
    "candidates_in": 20,
    "kept": 5,
    "elapsed_ms": 180,
    "fell_back": false,
    "fallback_reason": null,
    "before": [ { "title": "string", "vector_score": 0.82 } ],
    "after":  [ { "title": "string", "rerank_score": 0.97 } ]
  }
}
```
Absent when `Reranker:Enabled=false`. Existing clients ignoring unknown fields are unaffected.

## Activity events (emitted by Chat API)
- `Rerank` (category: retrieval): "Re-ranked {in} candidates → kept {kept} in {ms}ms" with before/after details.
- `RerankFallback` (warning): "Re-ranker unavailable ({reason}); using vector order".
