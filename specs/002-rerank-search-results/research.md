# Research: Re-ranking

## Decision 1: Default model — `BAAI/bge-reranker-base`
- **Rationale**: ~280M parameters, strong accuracy, runs on CPU in well under a second for ~20 short
  chunks, permissive license, supported by TEI. Fits "light and local".
- **Alternatives**: `bge-reranker-v2-m3` (more accurate and multilingual, ~570M, heavier; selectable by
  changing the Helm `model` value); `ms-marco-MiniLM-L-6-v2` (lightest, English-only, lower quality);
  NVIDIA NeMo Retriever (requires NVIDIA GPU/NGC, rejected).

## Decision 2: Serving — Hugging Face TEI CPU container
- **Rationale**: Ships a ready `/rerank` HTTP endpoint (query + texts in, index + score out), so no
  Python code or tokenizer work in .NET. Ollama cannot host cross-encoders.
- **Alternatives**: ONNX Runtime in-process (no new pod but needs tokenization code and model
  packaging); custom Python sidecar (more code to maintain).
- **Offline note**: weights download from Hugging Face on first start; cache in an `emptyDir` or small
  PVC. A pre-pull step in the quickstart makes later runs network-free.

## Decision 3: Where re-ranking runs — inside `search_knowledge_base`
- **Rationale**: Both the chat pipeline and external MCP clients call this tool, so one change satisfies
  FR-011 and keeps citations consistent with supplied chunks (FR-004).
- **Alternatives**: re-rank in `RagOrchestrator` (would leave the MCP tool inconsistent).

## Decision 4: Provider abstraction — `IReranker`
- **Contract**: `RerankAsync(query, documents, ct)` → list of (index, score), shaped like both TEI and
  Cohere responses. Providers: `Tei` (default), `Cohere` (opt-in, key from secret), `None` (pass-through).
  Selected by `Reranker:Provider`.

## Decision 5: Candidate sizing and threshold
- **Defaults**: `CandidateCount=20`, `TopN=5` (the tool's `topK` still caps the final count),
  `MinScore` configurable. The existing 0.6 cosine-similarity floor stays for candidates so weak
  vectors are not even scored. TEI returns sigmoid-normalized scores (0–1) by default; `MinScore` defaults to 0 after eval (see quickstart results).
- **Dedup**: collapse candidates with identical normalized text before scoring.

## Decision 6: Failure handling
- Timeout default 3s (configurable) via `HttpClient` cancellation; any exception, non-2xx, or malformed
  response triggers fallback to vector order plus a warning event (FR-008).

## Decision 7: Observability
- The tool result gains an optional `rerank` block (provider, candidates, kept, elapsedMs, fallback
  flag, before/after rankings). `RagOrchestrator` turns it into one activity event (FR-010).

## Decision 8: Live Confluence refresh interaction
- Re-ranking scores the cached chunk text from pgvector. The existing live-page refresh continues to run
  on the final kept chunks only, which also reduces Confluence calls.

## Decision 9: Test dataset (SC-001)
- A small JSON file of ≥20 question → expected source title pairs against the seed articles, with a
  script that compares hit rates with re-ranking on vs. off.
