# Quickstart: Validate Re-ranking

## Prerequisites
- Existing demo deployed (`deploy/scripts/deploy-all.ps1`), Ollama models pulled, knowledge base seeded.
- One-time internet access to download the `BAAI/bge-reranker-base` weights (cached afterwards).

## 1. Deploy the re-ranker
Deploy the new `reranker` Helm chart (included in `deploy-all.ps1`). Wait for the pod to be Ready,
then check its `/rerank` endpoint via port-forward (see [contracts/reranker-provider.md](./contracts/reranker-provider.md)).

## 2. Demo: re-ranking changes the result
1. Ask a question where a loosely related article ranks high by vector similarity.
2. In the activity log, find the `Rerank` event: candidates in, kept, elapsed time, before/after order.
3. Confirm the answer's cited sources match the "after" list (FR-004).

## 3. Compare on/off
Run the question set (≥20 pairs) with `Reranker__Enabled=true` and `false`; compare how often the
expected source is cited (SC-001). Tune `MinScore` here.

## 4. Fallback
Scale the reranker deployment to 0 and ask a question: an answer still arrives and a
`RerankFallback` warning appears (SC-003).

## 5. Optional: Cohere
Set `Reranker__Provider=Cohere` and supply the key via a Kubernetes Secret reference; repeat step 2.

## 6. Unit tests
`dotnet test` runs ordering, threshold, dedup, timeout, and fallback tests with a fake re-ranker.

## Eval results (2026-10-19, 32 questions, 16 seed articles, bge-reranker-base on CPU)

| Setting | Hit rate | Notes |
|---------|----------|-------|
| Re-ranking off (cosine floor 0.4) | 96.9% | baseline |
| On, MinScore 0.1, cosine floor 0.4 | 59.4% | too few candidates (1-2) and correct chunks scored below 0.1 |
| On, MinScore 0, cosine floor 0.5 (defaults) | 100% | avg 7.75 candidates; top result reordered for 3 questions |

- Cross-encoder scores for correct chunks can be as low as 0.0005 on paraphrased questions, so an absolute MinScore is not reliable; ranking order is what matters.
- Off-topic question ("chocolate chip cookies") still returns no results.
- Re-rank latency measured by the tool: avg 1030 ms, max 3012 ms (SC-002).
- Run: `kubectl port-forward -n rag-demo svc/mcp-server 8083:8080` then `scripts/rerank-eval/run-eval.ps1 -Label <name>`; toggle with `kubectl set env deployment/mcp-server -n rag-demo Reranker__Enabled=false`.
