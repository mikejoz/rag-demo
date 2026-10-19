# Implementation Plan: Re-rank Vector Search Results

**Branch**: `002-rerank-search-results` | **Date**: 2026-10-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-rerank-search-results/spec.md`

## Summary

Add a cross-encoder re-ranking stage inside the MCP `search_knowledge_base` tool (the single
retrieval path used by both chat and external MCP clients). The tool over-fetches candidates from
pgvector, scores each against the query with a re-ranker behind a new `IReranker` abstraction, and
returns the top N. The local default is a BGE cross-encoder (`BAAI/bge-reranker-base`) served by one
small CPU-only Hugging Face Text Embeddings Inference (TEI) pod in the `rag-demo` namespace. Cohere
is an opt-in provider; a pass-through provider disables re-ranking. Failures fall back to vector order.
The tool result carries re-rank metadata so the Chat API can emit a re-ranking activity event.

## Technical Context

**Language/Version**: C# / .NET 10 (existing); no frontend changes required

**Primary Dependencies**: Existing stack plus `HttpClient`-based providers only (no new NuGet packages);
TEI container image `ghcr.io/huggingface/text-embeddings-inference` (CPU variant) for the local model

**Storage**: No schema change; scores are per-request and not persisted

**Testing**: xUnit unit tests for the re-ranking service (ordering, threshold, fallback, timeout,
dedup) using a fake `IReranker`; provider tests using a fake `HttpMessageHandler`; manual demo path
per the constitution

**Target Platform**: Docker Desktop Kubernetes; the TEI pod runs on CPU, no GPU required

**Project Type**: Web application (existing multi-service .NET + Angular); backend-only change

**Performance Goals**: Re-rank ~20 candidates in well under 2s on CPU (SC-002)

**Constraints**: Offline by default (Principle I); Cohere key only via Kubernetes Secret / user-secrets
(Principle IV); re-ranking observable in the activity log (Principle III); keep it light (Principle VI)

**Scale/Scope**: One re-ranker pod, single replica, ~20 candidates per question

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Local-First | PASS | Default provider is local TEI + BGE; Cohere is opt-in. Model weights are downloaded once, then cached on a PVC/emptyDir; a documented pre-pull keeps later runs offline. Automated tests use a fake `IReranker`. |
| II. Spec-Driven | PASS | Every change traces to FR-001..FR-012. |
| III. Observable | PASS | Re-rank and fallback events emitted via the existing activity publisher (FR-010). |
| IV. Secrets | PASS | `Reranker:Cohere:ApiKey` from user-secrets / Kubernetes Secret reference only. |
| V. Stack Conventions | PASS | DI-based services, options pattern, Helm chart per service. No new UI. |
| VI. Simplicity | PASS | One new pod, one interface, three small providers; no new datastore. |

Post-design re-check: PASS (no violations; Complexity Tracking not needed).

## Project Structure

### Documentation (this feature)

```text
specs/002-rerank-search-results/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── mcp-tools.md
│   └── reranker-provider.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── RagChatDemo.Shared/
│   └── Reranking/                 # NEW: IReranker, RerankOptions, RerankResult, providers, RerankingService
│       ├── IReranker.cs
│       ├── RerankOptions.cs
│       ├── TeiReranker.cs         # local default (BGE via TEI /rerank)
│       ├── CohereReranker.cs      # opt-in hosted
│       ├── PassThroughReranker.cs # disabled / stub
│       └── RerankingService.cs    # over-fetch, score, threshold, dedup, fallback
├── RagChatDemo.McpServer/
│   ├── Program.cs                 # register options + providers
│   └── Tools/SearchKnowledgeBaseTool.cs   # candidate over-fetch + call RerankingService
├── RagChatDemo.ChatApi/
│   └── Chat/RagOrchestrator.cs    # emit re-ranking activity event from tool result metadata
└── ../tests/                      # xUnit tests for RerankingService and providers

deploy/
├── helm/reranker/                 # NEW: TEI deployment + service (CPU, bge-reranker-base)
├── helm/mcp-server/values.yaml    # Reranker__* env settings
└── scripts/deploy-all.ps1         # include reranker chart
```

**Structure Decision**: Extend the existing services. Re-ranking lives in the MCP tool because chat
and external MCP clients share it (FR-011), with logic in `RagChatDemo.Shared` so it can be tested in
isolation. The model runs in its own small pod because Ollama cannot serve cross-encoders.

## Complexity Tracking

No constitution violations.
