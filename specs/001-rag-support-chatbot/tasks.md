# Tasks: RAG Support Chatbot Demo

**Input**: Design documents from `/specs/001-rag-support-chatbot/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: Included for .NET services (xUnit) per constitution Development Workflow; Angular unit
tests limited to the activity log/chat components' core logic (not full e2e, given the one-week
demo horizon).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Create `RagChatDemo.sln` and solution folder layout (`src/`, `tests/`) at repo root
- [x] T002 `dotnet new classlib` for `src/RagChatDemo.Shared` (entities, DTOs, Ollama client, chunker)
- [x] T003 `dotnet new web` for `src/RagChatDemo.ChatApi`, `src/RagChatDemo.IngestionWorker`,
      `src/RagChatDemo.McpServer`, `src/RagChatDemo.ConfluenceStub`; add all to the .sln
- [x] T004 [P] `dotnet new xunit` for `tests/RagChatDemo.ChatApi.Tests`,
      `tests/RagChatDemo.IngestionWorker.Tests`, `tests/RagChatDemo.McpServer.Tests`; reference
      their respective projects
- [x] T005 [P] `ng new rag-chat-demo` (standalone, SCSS, no SSR) under `frontend/`
- [x] T006 [P] Add `.editorconfig` / `Directory.Build.props` (nullable enabled, implicit usings)
      at repo root for consistent .NET formatting
- [x] T007 Add `ollama pull nomic-embed-text` to `deploy/scripts/setup-ollama-models.ps1`

**Checkpoint (done 2026-10-01)**: Solution builds (`dotnet build`), Angular app builds
(`npm run build`), both clean, 0 warnings. Key NuGet packages installed (EF Core+Npgsql+Pgvector,
Hangfire+Postgres, ModelContextProtocol[.AspNetCore]); `@microsoft/signalr` added to the Angular
app. Git repo initialized, initial commit made. **Next: Phase 2 (T008-T015).**

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure every user story depends on (Principle III requires activity
events everywhere, so the hub + event contract must exist before any story-specific logic).

- [x] T008 [P] Define `ActivityEventDto`, `ChatMessageDto`, `SourceCitationDto` records in
      `src/RagChatDemo.Shared/Contracts/`
- [x] T009 [P] Implement `SourceDocument` and `Chunk` EF Core entities + `RagChatDemoDbContext` in
      `src/RagChatDemo.Shared/Data/` per [data-model.md](./data-model.md), using
      `Pgvector.EntityFrameworkCore` for the `Chunk.Embedding` column
- [x] T010 Add initial EF Core migration (`InitialCreate`) enabling the `vector` extension and an
      HNSW index on `Chunk.Embedding`
- [x] T011 [P] Implement `OllamaClient` in `src/RagChatDemo.Shared/Ollama/` with
      `GetEmbeddingAsync(text)` and `StreamChatAsync(messages, model)` (NDJSON streaming) per
      [research.md](./research.md) D2
- [x] T012 [P] Implement pure chunking function `TextChunker.Chunk(text)` in
      `src/RagChatDemo.Shared/Chunking/` (paragraph-aware, ~2000 chars, 10% overlap) with unit
      tests in `tests/RagChatDemo.IngestionWorker.Tests/TextChunkerTests.cs`
- [x] T013 Implement `ActivityHub` (SignalR) in `src/RagChatDemo.ChatApi/Hubs/ActivityHub.cs`
      with a last-50-events replay buffer, and `POST /internal/activity-events` endpoint that
      broadcasts to it, per [contracts/signalr-hubs.md](./contracts/signalr-hubs.md)
- [x] T014 [P] Implement `IActivityPublisher` in `src/RagChatDemo.Shared/Activity/` with an
      HTTP-posting implementation used by `IngestionWorker`/`McpServer`, and a direct-broadcast
      implementation used by `ChatApi` itself
- [x] T015 Configure environment-based config (Ollama base URL, Postgres connection string,
      Confluence base URL/token, model names) via `appsettings.json` + `dotnet user-secrets` in
      each service, per Principle IV

**Checkpoint (done 2026-10-01)**: Verified against a local `pgvector/pgvector:pg16` Docker
container (`ragchatdemo-postgres`) — `InitialCreate` migration applies cleanly, `Chunks`/
`SourceDocuments` tables exist with the HNSW cosine index. Posted a test `ActivityEventDto` to
`POST /internal/activity-events` on a running `ChatApi` and got `202 Accepted` (enums serialize
as strings via `JsonStringEnumConverter`, configured for both minimal-API JSON and the SignalR
JSON hub protocol). Confirmed `nomic-embed-text` (pulled into the local `ollama` container)
returns 768-dimension embeddings, matching `Chunk.Embedding`'s `vector(768)` column. All 8 xUnit
tests pass; solution builds with 0 warnings/0 errors. `dotnet user-secrets` initialized for
ChatApi/IngestionWorker/McpServer holding the Postgres connection string (with password) and a
Confluence API token placeholder — none of these are in source control. **Next: Phase 3 (US1,
T016-T024).**

Note: `DirectActivityPublisher` (the direct-broadcast `IActivityPublisher` impl) lives in
`src/RagChatDemo.ChatApi/Activity/` rather than `Shared/Activity/`, since it depends on
`IHubContext<ActivityHub>` and `ActivityHub` is a ChatApi type (classlib `Shared` can't reference
ASP.NET Core SignalR hub types cleanly). `IActivityPublisher` + `HttpActivityPublisher` are in
`Shared/Activity/` as specified.

---

## Phase 3: User Story 1 - Ask a question and get a grounded, streamed answer (P1) 🎯 MVP

**Goal**: Chat UI → ChatApi → MCP `search_knowledge_base` → Ollama streamed response, with
citations.

**Independent Test**: With seed chunks already in Postgres (can be inserted directly for this
story's test), ask a question via the API/UI and see a streamed, cited answer.

### Tests for User Story 1

- [ ] T016 [P] [US1] Contract test: MCP `search_knowledge_base` returns ranked results for a
      known seeded chunk, in `tests/RagChatDemo.McpServer.Tests/SearchKnowledgeBaseToolTests.cs`
- [ ] T017 [P] [US1] Integration test: `ChatHub.SendMessage` with a seeded chunk produces at
      least one `ResponseToken` and a `ResponseComplete` with a citation, in
      `tests/RagChatDemo.ChatApi.Tests/ChatHubTests.cs`

### Implementation for User Story 1

- [ ] T018 [US1] Implement `search_knowledge_base` MCP tool in
      `src/RagChatDemo.McpServer/Tools/SearchKnowledgeBaseTool.cs` (embeds query via
      `OllamaClient`, cosine-similarity query via EF Core/pgvector) — depends on T009, T011
- [ ] T019 [US1] Implement MCP client wiring in `src/RagChatDemo.ChatApi/Mcp/McpToolClient.cs`
      (connects to `RagChatDemo.McpServer`, lists tools, invokes by name) — depends on T018
- [ ] T020 [US1] Implement `ChatHub` in `src/RagChatDemo.ChatApi/Hubs/ChatHub.cs` per
      [contracts/signalr-hubs.md](./contracts/signalr-hubs.md) `SendMessage` → orchestration
- [ ] T021 [US1] Implement `RagOrchestrator` in `src/RagChatDemo.ChatApi/Chat/RagOrchestrator.cs`:
      publish `Retrieval` activity event → call MCP tool → publish `McpTool` activity event →
      build grounded prompt → `OllamaClient.StreamChatAsync` → publish `Generation` activity
      events → stream tokens to `ChatHub` caller — depends on T013, T014, T019, T020
- [ ] T022 [US1] Handle "no relevant chunks found" path (FR-012): return a fixed
      no-knowledge-found response instead of calling the LLM with empty context
- [ ] T023 [P] [US1] Angular: `ChatSignalrService` in
      `frontend/rag-chat-demo/src/app/core/chat-signalr.service.ts` wrapping `@microsoft/signalr`
      for `/hubs/chat`
- [ ] T024 [US1] Angular: `ChatComponent` (standalone, OnPush, signals, reactive form for the
      message input) in `frontend/rag-chat-demo/src/app/chat/` rendering streamed tokens and
      citations — depends on T023

**Checkpoint**: User Story 1 fully functional and demoable independent of ingestion/US2/US3/US4.

---

## Phase 4: User Story 2 - Observe live background activity (P1)

**Goal**: Front-end activity log panel renders the events already being published by Foundational
+ US1 (and later US3/US4) work, in real time and in order.

**Independent Test**: Open the activity panel, ask a chat question (US1), and watch retrieval /
MCP tool / generation events appear live and in order.

### Tests for User Story 2

- [ ] T025 [P] [US2] Integration test: posting events to `/internal/activity-events` results in
      ordered delivery to a connected `/hubs/activity` client, in
      `tests/RagChatDemo.ChatApi.Tests/ActivityHubTests.cs`

### Implementation for User Story 2

- [ ] T026 [P] [US2] Angular: `ActivitySignalrService` in
      `frontend/rag-chat-demo/src/app/core/activity-signalr.service.ts`
- [ ] T027 [US2] Angular: `ActivityLogComponent` (standalone, OnPush, signals, `@for` over a
      signal of events, categorized styling, ARIA live region for accessibility) in
      `frontend/rag-chat-demo/src/app/activity-log/` — depends on T026
- [ ] T028 [US2] Wire `ActivityLogComponent` + `ChatComponent` side-by-side in the root `App`
      component layout (`frontend/rag-chat-demo/src/app/app.ts`/`app.html`)
- [ ] T029 [US2] Verify/adjust ordering guarantee: events carry a server-assigned monotonic
      sequence number (add `Sequence: long` to `ActivityEventDto`/hub) so same-timestamp events
      still render in the correct order (edge case in spec.md)

**Checkpoint**: Activity log visibly narrates US1's retrieval → MCP → generation steps live.

---

## Phase 5: User Story 3 - Keep the knowledge base current via scheduled ingestion (P2)

**Goal**: Hangfire worker fetches (stub or real Confluence) pages, chunks, embeds, upserts.

**Independent Test**: Trigger the ingestion job from the Hangfire dashboard and confirm new/edited
stub articles are reflected in subsequent US1 answers.

### Tests for User Story 3

- [ ] T030 [P] [US3] Unit test: re-ingesting an unchanged `SourceDocument` is a no-op; an edited
      one replaces its `Chunk` rows, in `tests/RagChatDemo.IngestionWorker.Tests/IngestionJobTests.cs`

### Implementation for User Story 3

- [ ] T031 [P] [US3] Implement `RagChatDemo.ConfluenceStub` minimal API with ~15-20 seeded
      IT/support articles (`GET /wiki/api/v2/pages`, `GET /wiki/api/v2/pages/{id}`)
- [ ] T032 [P] [US3] Implement `IConfluenceClient` + `ConfluenceStubClient` and
      `ConfluenceCloudClient` in `src/RagChatDemo.IngestionWorker/Confluence/` per
      [research.md](./research.md) D1
- [ ] T033 [US3] Implement `IngestionJob` in
      `src/RagChatDemo.IngestionWorker/Jobs/IngestionJob.cs`: fetch → publish activity event per
      page → chunk (T012) → embed (T011) → upsert `SourceDocument`/`Chunk` (replace-on-change
      per FR-013) → publish activity events — depends on T009, T011, T012, T014, T032
- [ ] T034 [US3] Configure Hangfire server + Postgres storage + recurring job registration + the
      built-in Hangfire Dashboard in `src/RagChatDemo.IngestionWorker/Program.cs`
- [ ] T035 [US3] Add a manually-triggerable Hangfire job button/link documented in
      [quickstart.md](./quickstart.md) (dashboard already supports manual trigger out of the box)

**Checkpoint**: Ingestion populates/updates Postgres; US1 answers reflect edited content after a
manual trigger.

---

## Phase 6: User Story 4 - Generic MCP tool-calling pattern (P3)

**Goal**: A second MCP tool (`create_support_ticket`) is available and gets invoked by the LLM
when relevant, visible distinctly in the activity log.

**Independent Test**: Ask "can you log a ticket for this?" and confirm the activity log shows
`create_support_ticket` (not `search_knowledge_base`) being invoked.

### Implementation for User Story 4

- [ ] T036 [P] [US4] Implement `create_support_ticket` MCP tool (stubbed, fake ticket id) in
      `src/RagChatDemo.McpServer/Tools/CreateSupportTicketTool.cs` per
      [contracts/mcp-tools.md](./contracts/mcp-tools.md)
- [ ] T037 [US4] Ensure `RagOrchestrator` (T021) passes both tool definitions to Ollama's `tools`
      parameter and publishes an `McpTool` activity event naming whichever tool the model chose

**Checkpoint**: Both MCP tools are selectable by the model and distinctly visible in the log.

---

## Phase 7: Kubernetes/Helm Deployment & Polish

**Purpose**: Package and deploy everything to the local Docker Desktop cluster; cross-cutting
hardening.

- [ ] T038 [P] Helm chart `deploy/helm/postgres` (pgvector image, PVC, `Secret` for password)
- [ ] T039 [P] Helm chart `deploy/helm/confluence-stub`
- [ ] T040 [P] Helm chart `deploy/helm/mcp-server`
- [ ] T041 [P] Helm chart `deploy/helm/ingestion-worker` (env: Ollama host, Postgres conn,
      Confluence base URL + `Secret` reference for API token)
- [ ] T042 [P] Helm chart `deploy/helm/chat-api`
- [ ] T043 [P] Helm chart `deploy/helm/frontend` (nginx-served Angular build)
- [ ] T044 `deploy/scripts/deploy-all.ps1` — `helm upgrade --install` all charts into `rag-demo`
      namespace in dependency order
- [ ] T045 `deploy/scripts/seed-knowledge-base.ps1` — convenience script to trigger the Hangfire
      ingestion job once after first deploy
- [ ] T046 Run axe accessibility checks against the deployed Angular app; fix any
      critical/serious violations (SC-005)
- [ ] T047 Execute the full [quickstart.md](./quickstart.md) verification checklist against the
      cluster deployment and record results

---

## Dependencies & Execution Order

- **Setup (Phase 1)** → **Foundational (Phase 2)** → User Stories (Phases 3-6, in priority order
  P1 → P1 → P2 → P3, though US2 needs US1 or US3 running to have events to display) →
  **Deployment/Polish (Phase 7)**.
- US1 (Phase 3) and US2 (Phase 4) are both P1; implement US1 first since US2 only has value once
  something is publishing events, but US2's own tasks (T025-T029) don't block US1.
- US3 (Phase 5) and US4 (Phase 6) can proceed in parallel with each other once Foundational is
  done, independent of US1/US2 except for shared `RagOrchestrator` touch-point in T037.

## Implementation Strategy

**MVP** = Phase 1 + Phase 2 + Phase 3 (US1), demoable with directly-seeded Postgres rows (skip
ingestion). Add Phase 4 (US2) immediately after for the "wow factor" log panel — these two
together are the minimum credible interview demo. Phases 5-6 add the ingestion story and the
second MCP tool. Phase 7 makes it deployable to Kubernetes and demo-polished.
