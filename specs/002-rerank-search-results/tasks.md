# Tasks: Re-rank Vector Search Results

**Input**: Design documents from `/specs/002-rerank-search-results/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Unit tests are included (plan.md Testing section). Format: `- [ ] T### [P?] [Story?] Description with file path`

## Phase 1: Setup

- [X] T001 Create xUnit test project `tests/RagChatDemo.Shared.Tests/RagChatDemo.Shared.Tests.csproj` referencing `src/RagChatDemo.Shared`, and add it to `RagChatDemo.slnx`
- [X] T002 [P] Create Helm chart `deploy/helm/reranker/` (Chart.yaml, values.yaml, templates/deployment.yaml, templates/service.yaml) running the TEI CPU image with `--model-id BAAI/bge-reranker-base` (model as a values entry), port 80, an `emptyDir` model cache, readiness probe on `/health`, in the `rag-demo` namespace
- [X] T003 [P] Add the `reranker` chart to `deploy/scripts/deploy-all.ps1` and a model pre-pull/warm-up note to `deploy/scripts/setup-ollama-models.ps1` (or a new `deploy/scripts/warm-reranker.ps1`) so later runs are offline (research Decision 2)

## Phase 2: Foundational (blocks all stories)

- [X] T004 [P] Create `RerankOptions` in `src/RagChatDemo.Shared/Reranking/RerankOptions.cs` with section `Reranker` and defaults from data-model.md: `Enabled=true`, `Provider=Tei`, `CandidateCount=20`, `TopN=5`, `MinScore`, `TimeoutSeconds=3`, `Tei:BaseUrl=http://reranker:80`, `Cohere:Model=rerank-v3.5`, `Cohere:ApiKey` (secret reference only, never a literal)
- [X] T005 [P] Create `IReranker` (`RerankAsync(string query, IReadOnlyList<string> documents, CancellationToken)` returning `(int Index, float Score)` list, scores 0-1) in `src/RagChatDemo.Shared/Reranking/IReranker.cs` per contracts/reranker-provider.md
- [X] T006 [P] Create `PassThroughReranker` (input order, score = 1) in `src/RagChatDemo.Shared/Reranking/PassThroughReranker.cs`
- [X] T007 [P] Add the optional `rerank` outcome block (provider, candidates_in, kept, elapsed_ms, fell_back, fallback_reason, before[], after[]) to `SearchKnowledgeBaseResult` in `src/RagChatDemo.Shared/Contracts/McpToolContracts.cs` per contracts/mcp-tools.md

## Phase 3: User Story 1 - More relevant answers via re-ranked context (P1) MVP

**Goal**: Over-fetch candidates, re-score against the question, give only the top N to the LLM, with matching citations.
**Independent Test**: Ask a question where vector order puts a loosely related chunk first; the relevant chunk is cited and the other dropped.

- [X] T008 [P] [US1] Create `TeiReranker` in `src/RagChatDemo.Shared/Reranking/TeiReranker.cs`: `POST {BaseUrl}/rerank` with `{query, texts, truncate:true}`, parse `[{index, score}]`, throw on non-2xx or malformed body
- [X] T009 [P] [US1] Create `RerankingService` in `src/RagChatDemo.Shared/Reranking/RerankingService.cs`: dedup candidates by normalized text before scoring, score via `IReranker`, drop chunks below `MinScore`, sort by score descending, keep `min(TopN, topK)`, preserve source/document links (FR-001..FR-005, FR-012)
- [X] T010 [P] [US1] Unit tests for ordering, TopN/topK cap, `MinScore` exclusion, dedup, empty candidates, and candidates <= TopN in `tests/RagChatDemo.Shared.Tests/RerankingServiceTests.cs` using a fake `IReranker`
- [X] T011 [P] [US1] Unit tests for `TeiReranker` request/response mapping and error handling using a fake `HttpMessageHandler` in `tests/RagChatDemo.Shared.Tests/TeiRerankerTests.cs`
- [X] T012 [US1] Register `RerankOptions`, `IReranker` (selected by `Reranker:Provider`), and `RerankingService` with a typed `HttpClient` in `src/RagChatDemo.McpServer/Program.cs`
- [X] T013 [US1] Update `src/RagChatDemo.McpServer/Tools/SearchKnowledgeBaseTool.cs`: fetch `CandidateCount` candidates (keep the existing 0.6 cosine floor), call `RerankingService`, run the live Confluence refresh only on the kept chunks, return re-ranked `score` and the `rerank` block; if no chunks remain return the existing no-relevant-content result (FR-005, FR-011)
- [X] T014 [P] [US1] Add `Reranker__*` env settings to `deploy/helm/mcp-server/values.yaml` and its `templates/deployment.yaml` (Provider, CandidateCount, TopN, MinScore, TimeoutSeconds, Tei BaseUrl)

**Checkpoint**: Chat answers use re-ranked context; US1 is demoable on its own.

## Phase 4: User Story 2 - Visible re-ranking step in the activity log (P2)

**Goal**: Show a re-ranking event with counts, timing and before/after order.
**Independent Test**: Ask any question; a `Rerank` event appears between retrieval and generation.

- [X] T015 [US2] In `src/RagChatDemo.ChatApi/Chat/RagOrchestrator.cs` emit a `Rerank` activity event ("Re-ranked {in} candidates -> kept {kept} in {ms}ms") with before/after titles and scores from the tool result's `rerank` block (FR-010)
- [X] T016 [P] [US2] Ensure the new event category/details render in `frontend/rag-chat-demo/src/app/activity-log/activity-log.ts` (only if existing rendering does not show the details)
- [X] T017 [P] [US2] Extend `tests/RagChatDemo.ChatApi.Tests/FakeMcpToolClient.cs` to return a `rerank` block and add a test in `tests/RagChatDemo.ChatApi.Tests/ChatHubTests.cs` asserting the `Rerank` event is published

## Phase 5: User Story 3 - Graceful fallback and disable (P3)

**Goal**: Chat keeps working when the re-ranker is down, slow, or disabled.
**Independent Test**: Scale the reranker to 0; an answer still arrives and a fallback warning appears.

- [X] T018 [US3] In `RerankingService` add timeout (`TimeoutSeconds`) and catch-all fallback to vector order, setting `fell_back` and `fallback_reason` (FR-008)
- [X] T019 [US3] Honor `Reranker:Enabled=false`: skip re-ranking, return vector order, omit the `rerank` block (FR-009) in `RerankingService` and `SearchKnowledgeBaseTool.cs`
- [X] T020 [US3] Emit a `RerankFallback` warning event in `src/RagChatDemo.ChatApi/Chat/RagOrchestrator.cs` when `fell_back` is true
- [X] T021 [P] [US3] Unit tests for timeout, exception, malformed response, and disabled paths in `tests/RagChatDemo.Shared.Tests/RerankingFallbackTests.cs`
- [X] T022 [P] [US3] Create `CohereReranker` in `src/RagChatDemo.Shared/Reranking/CohereReranker.cs` (`POST https://api.cohere.com/v2/rerank`, bearer key from configuration/secret, map `relevance_score`) with tests in `tests/RagChatDemo.Shared.Tests/CohereRerankerTests.cs`; wire provider selection in `src/RagChatDemo.McpServer/Program.cs` and document the Kubernetes Secret reference in `deploy/helm/mcp-server/values.yaml` (FR-006, FR-007)

## Phase 6: Polish

- [X] T023 [P] Create the evaluation set `scripts/rerank-eval/questions.json` (>=20 question -> expected source title pairs from the seed articles) and `scripts/rerank-eval/run-eval.ps1` comparing citation hit rate with `Reranker__Enabled` true vs false (SC-001)
- [X] T024 Run the eval, tune the `MinScore` default in `RerankOptions` and `deploy/helm/mcp-server/values.yaml`, and record results in `specs/002-rerank-search-results/quickstart.md`
- [X] T025 [P] Update `ARCHITECTURE.md` (new reranker pod, `IReranker` providers, updated `search_knowledge_base` flow) and `specs/001-rag-support-chatbot/contracts/mcp-tools.md` reference
- [X] T026 Run `dotnet test` and the quickstart.md demo path end to end (re-rank event, fallback with reranker scaled to 0, optional Cohere); confirm SC-002 latency

## Dependencies & Execution Order

- Phase 1 -> Phase 2 -> user stories. T001 gates the test tasks; T002/T003 are independent of code.
- US1 is the MVP. US2 needs T007 and T013 (the `rerank` block). US3 builds on T009 and T013. T022 depends on T004/T005 and T012.
- Within US1: T008, T009, T010, T011, T014 can run in parallel; T012 then T013 are sequential.

## Parallel Example

After Phase 2: T008 + T009 + T014 together, then tests T010 + T011, then T012 -> T013.

## Implementation Strategy

1. MVP: Phases 1-3, then run the quickstart demo for US1.
2. Add US2 (observability), then US3 (fallback, Cohere).
3. Finish with the evaluation and docs (Phase 6).
