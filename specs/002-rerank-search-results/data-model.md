# Data Model: Re-ranking

No database changes. All entities are in-memory per request.

## CandidateChunk
- `ChunkId` (guid), `DocumentTitle`, `SourceUrl`, `Text`, `VectorScore` (cosine similarity 0–1), `VectorRank` (1-based)

## RerankedChunk
- All `CandidateChunk` fields plus `RerankScore` (0–1) and `FinalRank` (1-based)
- Rules: sorted by `RerankScore` desc; excluded if `RerankScore < MinScore`; duplicates by normalized text removed before scoring.

## RerankOptions (configuration, section `Reranker`)
| Key | Default | Notes |
|-----|---------|-------|
| `Enabled` | `true` | `false` restores pre-feature behavior (FR-009) |
| `Provider` | `Tei` | `Tei`, `Cohere`, or `None` |
| `CandidateCount` | `20` | Vector over-fetch size |
| `TopN` | `5` | Max chunks passed on (capped by tool `topK`) |
| `MinScore` | `0` | Relevance floor; cross-encoder scores are low in absolute terms, so the default does not filter |
| `CandidateMaxCosineDistance` | `0.5` | Vector cutoff used only when re-ranking is enabled (0.4 otherwise) |
| `TimeoutSeconds` | `3` | Per call |
| `Tei:BaseUrl` | `http://reranker:80` | Local provider |
| `Cohere:Model` | `rerank-v3.5` | Hosted provider |
| `Cohere:ApiKey` | none | Secret reference only |

## RerankOutcome (returned with tool result)
- `Provider`, `CandidatesIn`, `Kept`, `ElapsedMs`, `FellBack` (bool), `FallbackReason?`
- `Before[]` / `After[]`: (document title, score) for kept chunks.
