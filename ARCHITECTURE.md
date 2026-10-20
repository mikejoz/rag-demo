# RagChatDemo — Architecture Documentation

RAG-powered chat assistant backed by Confluence knowledge, Ollama local LLMs, and pgvector embeddings. Deployed to a local Kubernetes cluster (Docker Desktop) with Hangfire-driven ingestion and SignalR streaming.

## Table of Contents

- [Overview](#overview)
- [Project Structure](#project-structure)
- [High-Level Architecture Diagram](#high-level-architecture-diagram)
- [Chat Request Flow](#chat-request-flow)
- [Knowledge Ingestion Flow](#knowledge-ingestion-flow)
- [RAG Retrieval Flow](#rag-retrieval-flow)
- [Data Model](#data-model)
- [External Dependencies](#external-dependencies)
- [SignalR Protocol](#signalr-protocol)
- [MCP Tool Integration](#mcp-tool-integration)
- [Frontend Architecture](#frontend-architecture)
- [Deployment Architecture](#deployment-architecture)
- [Configuration](#configuration)

---

## Overview

RagChatDemo is a full-stack RAG (Retrieval-Augmented Generation) chat system. It ingests Confluence pages into a vector database, answers user questions using semantic search, and streams LLM responses to an Angular frontend via SignalR. All components run as pods in a local Docker Desktop Kubernetes cluster.

| Layer | Technology |
|-------|-----------|
| Frontend | Angular 21 (standalone) with SignalR |
| API / Real-time | ASP.NET Core Web API + SignalR Hubs |
| Background Jobs | Hangfire (.NET job scheduler) |
| LLM & Embeddings | Ollama (`qwen3.6:27b`, `nomic-embed-text`) |
| Vector DB | PostgreSQL 16 + pgvector (HNSW cosine index) |
| Re-ranking | `BAAI/bge-reranker-base` cross-encoder via Hugging Face TEI (CPU); Cohere optional |
| Tool Calling | Model Context Protocol (MCP) over HTTP |
| Knowledge Source | Confluence REST API (stub or cloud) |
| Deployment | Helm charts → Docker Desktop Kubernetes |

All .NET projects target **.NET 10** (`net10.0`).

---

## Project Structure

```
RagChatDemo/
├── src/
│   ├── RagChatDemo.Shared/           # Shared library
│   ├── RagChatDemo.ChatApi/          # SignalR + RAG orchestration
│   ├── RagChatDemo.IngestionWorker/  # Hangfire ingestion pipeline
│   ├── RagChatDemo.McpServer/        # MCP tool server
│   └── RagChatDemo.ConfluenceStub/   # In-memory Confluence mock
├── tests/                            # xUnit projects (Shared, ChatApi, IngestionWorker, McpServer)
├── scripts/rerank-eval/              # Re-ranking on/off evaluation (questions + runner)
├── frontend/rag-chat-demo/           # Angular frontend
├── deploy/
│   ├── helm/                         # Helm charts per service
│   └── scripts/                      # Build and deployment scripts
└── specs/                            # Feature specifications
```

### Shared Library (`RagChatDemo.Shared`)

Common infrastructure shared across all backend services.

| Component | Purpose |
|-----------|---------|
| `Data/RagChatDemoDbContext` | EF Core context for `SourceDocuments` and `Chunks`; enables pgvector extension; configures HNSW cosine index on 768-dim vector column |
| `Data/SourceDocument` | Confluence page entity (external ID, title, plain-text content, URL, timestamps) |
| `Data/Chunk` | Text segment with ordinal and `Pgvector.Vector<768>` embedding |
| `Reranking/` | `IReranker` with `TeiReranker`, `CohereReranker`, `PassThroughReranker`; `RerankingService` (dedup, score, MinScore, top N, timeout/fallback); `RerankOptions` (`Reranker` section) |
| `Chunking/TextChunker` | Paragraph-aware chunker (2 000 char max, 10% overlap) |
| `Ollama/OllamaClient` | HTTP client for Ollama embeddings and streaming chat APIs |
| `Contracts/` | Chat messages, citations, activity events, MCP results |
| `Activity/` | Activity publisher abstractions and HTTP-based implementation |

### Chat API (`RagChatDemo.ChatApi`)

The public-facing backend hosting SignalR hubs, RAG orchestration, and OpenAPI documentation.

| Feature | Details |
|---------|---------|
| Hubs | `/hubs/chat` (chat streaming), `/hubs/activity` (live telemetry) |
| Endpoints | `GET /health` (all), `GET /health/live` (liveness), `GET /health/ready` (readiness), `POST /internal/activity-events`, `GET /swagger`, `GET /openapi/v1.json` (dev only) |
| RAG Orchestrator | Streams tokens via SignalR, dispatches tool calls to MCP server, emits activity events |
| Prompt grounding | Answers use only retrieved content (`GroundingPolicy`). Direct model answers are never streamed; if the model skips search, a knowledge-base search is forced (`forced_retrieval` event). Empty results return an "unable to find information" reply (`grounded_fallback` event). Reranker `MinScore` is the relevance floor |
| MCP Client | Connects to McpServer via `HttpClientTransport` for `search_knowledge_base` and `create_support_ticket` |
| Dev | Swagger UI + OpenAPI spec (gated behind `ASPNETCORE_ENVIRONMENT=Development`) |

### Ingestion Worker (`RagChatDemo.IngestionWorker`)

Hangfire-based background service that periodically ingests Confluence content.

| Feature | Details |
|---------|---------|
| Hangfire Server | Runs in-process; PostgreSQL-backed job storage; dashboard at `/hangfire` |
| Recurring Job | `knowledge-base-ingestion` — runs hourly via Hangfire schedule |
| Confluence Client | Supports stub (`ConfluenceStubClient`) or cloud (`ConfluenceCloudClient`) based on config |
| Pipeline | Fetch pages → strip HTML → chunk text → embed chunks → upsert to pgvector |
| Activity | Publishes ingestion events to ChatApi for real-time dashboard display |

### MCP Server (`RagChatDemo.McpServer`)

Standalone Model Context Protocol HTTP server exposing tools the LLM can invoke.

| Feature | Details |
|---------|---------|
| Transport | HTTP (via `ModelContextProtocol.AspNetCore`) |
| Tools | `search_knowledge_base` (pgvector candidate over-fetch + re-rank + live Confluence refresh), `create_support_ticket` (stub — returns fake ticket ID) |
| Project Reference | Depends on `IngestionWorker` to reuse Confluence clients and ingestion infrastructure |

### Reranker (`deploy/helm/reranker`)

A small CPU-only Hugging Face Text Embeddings Inference (TEI) pod serving the `BAAI/bge-reranker-base`
cross-encoder behind an HTTP `/rerank` endpoint. Used by McpServer's `RerankingService` (in
`RagChatDemo.Shared`) to re-score candidate chunks against the question before the top N are returned.
No GPU required; model weights are cached so repeated deploys don't need network access.

`IReranker` providers (selected by `Reranker:Provider`):

| Provider | Role |
|----------|------|
| `Tei` (default) | Calls the local reranker pod's `/rerank` endpoint — fully offline |
| `Cohere` (opt-in) | Calls the hosted Cohere rerank API; API key supplied only via a Kubernetes Secret / user-secrets reference, never a literal |
| `None` | Pass-through — returns candidates in their existing vector-similarity order (re-ranking disabled) |

### Confluence Stub (`RagChatDemo.ConfluenceStub`)

In-memory mock of the Confluence REST API for offline development.

| Feature | Details |
|---------|---------|
| Endpoints | `GET /wiki/api/v2/pages`, `GET /wiki/api/v2/pages/{id}`, `PUT /wiki/api/v2/pages/{id}` |
| Seed Data | IT/support articles covering topics like password reset, VPN setup, printer config |
| Change Detection | Incrementing version numbers and timestamps allow the ingestion job to detect updates |

---

## High-Level Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        Browser                                  │
│  ┌──────────┐   SignalR    ┌──────────────────────────────┐     │
│  │  Angular  │ ──────────► │           frontend:80         │     │
│  │  (UI)     │ ◄────────── │  (reverse proxy to chat-api)  │     │
│  └──────────┘             └──────────────┬───────────────┘     │
└──────────────────────────────────────────┼─────────────────────┘
                                           │
┌──────────────────────────────────────────┼─────────────────────┐
│                 Kubernetes Cluster        │                     │
│                                          ▼                     │
│  ┌──────────────────┐    HTTP     ┌──────────────────┐         │
│  │   chat-api:8080   │ ◄────────► │ mcp-server:8080  │         │
│  │                  │            │                   │         │
│  │ • ChatHub        │            │ • search_kb       │         │
│  │ • ActivityHub    │            │ • create_ticket   │         │
│  │ • RagOrchestrator│            └───┬──────┬───────┘         │
│  │ • Swagger UI     │                │      │                  │
│  └───┬──────┬───────┘                │      │ HTTP             │
│       │      │                       │      ▼                  │
│       │      │   HTTP                │  ┌──────────────────┐   │
│       │      │ ◄──────────   ┌───────┘  │  reranker:80     │   │
│       │      └─────────────► │          │  (TEI, bge-      │   │
│       │                      │          │   reranker-base) │   │
│       │                      ▼          └──────────────────┘   │
│       │               ┌──────────────────┐                     │
│       │               │  confluence-stub │                     │
│       │               │  :8080            │                     │
│       │               └──────────────────┘                     │
│       │                                                       │
│       ▼      ▼                                                │
│  ┌──────────────────────┐    ┌──────────────────┐             │
│  │ postgres:5432        │    │ ingestion-        │             │
│  │                      │    │ worker:8080       │             │
│  │ • ragchatdemo DB     │◄──►│                   │             │
│  │ • pgvector (HNSW)    │    │ • Hangfire jobs   │             │
│  │ • Hangfire storage   │    │ • /hangfire UI    │             │
│  └──────────────────────┘    └────────┬──────────┘             │
│                                       │                        │
│                                       │ HTTP                   │
│                                       ▼                        │
│                               ┌──────────────────┐             │
│                               │   Ollama:11434   │             │
│                               │                  │             │
│                               │ • qwen3.6:27b    │             │
│                               │ • nomic-embed-txt│             │
│                               └──────────────────┘             │
│                                       ▲                        │
│                     Ollama is on the host (host.docker.internal)│
└─────────────────────────────────────────────────────────────────┘
```

---

## Chat Request Flow

```
User types message in Angular chat UI
  │
  ▼
ChatSignalRService.sendMessage() ───► /hubs/chat (SignalR: SendMessage)
  │                                     │
  ▼                                     ▼
Local user message added              ChatHub.SendMessage()
                                      │
                                      ▼
                              RagOrchestrator.HandleMessageAsync()
                                      │
                                      ├── Build system prompt + conversation history
                                      │
                                      ▼
                              Ollama streaming chat call (qwen3.6:27b)
                                      │
                    ┌─────────────────┼─────────────────┐
                    │                 │                 │
              Direct text         Tool call:        Tool call:
              tokens streamed   search_kb           create_ticket
              to user             │                     │
                                  ▼                     ▼
                         MCP: SearchKnowledgeBase  MCP: CreateSupportTicket
                                  │                     │
                            Embed query via Ollama     Generate stub ticket ID
                                  │                     │
                        pgvector cosine search          Return { id, status }
                        (max dist 0.4; 0.5 w/ rerank)  │
                                  │                      │
                  Re-rank via reranker pod             Append tool result
                  (top N of 20 candidates)             to conversation
                                  │                      │
                  Live Confluence refresh              │
                  for kept pages                       │
                                  │                      │
                                  ▼                      │
                 Return re-ranked chunks                 │
                 with title, URL, score, text             │
                                  │                      │
                                  └──────────┬───────────┘
                                             │
                                    Call Ollama again with
                                    tool results appended
                                             │
                                             ▼
                                    Stream final response tokens
                                    to user via SignalR
                                             │
                                             ▼
                                    ResponseComplete event
                                    (includes citations)
```

**Key details:**

- The orchestrator currently handles **one tool call per turn** (no multi-turn loops in the same request).
- If pgvector returns no results above the similarity threshold (~0.6, or ~0.5 when re-ranking widens the candidate pool), the user sees: *"I couldn't find any relevant information in the knowledge base to answer that question."*
- Citations (source document titles, URLs, and snippets) are included in the `ResponseComplete` event.

---

## Knowledge Ingestion Flow

```
Hangfire recurring job (hourly schedule)
  │
  ▼
IngestionJob.Run()
  │
  ├── ConfluenceUseStub = true  ──► ConfluenceStubClient
  │   (in-memory mock articles)
  │
  └── ConfluenceUseStub = false ──► ConfluenceCloudClient
                                     (REST API v2, Basic auth,
                                      paginated storage-format HTML)
  │
  ▼
For each page:
  │
  ├── Lookup by ExternalId in SourceDocuments
  │   │
  │   ├── Not found → treat as new
  │   └── LastModifiedAt unchanged → skip (incremental update)
  │
  ▼
  Strip HTML tags and entities → normalize whitespace
  │
  ▼
  TextChunker splits into paragraphs-aware chunks (2000 chars, 10% overlap)
  │
  ▼
  Ollama embeddings endpoint embeds each chunk (768-dim vectors)
  │
  ▼
  Delete old chunks for changed documents
  Insert new chunks with embeddings
  Upsert SourceDocument row
  │
  ▼
  Publish activity events to ChatApi /internal/activity-events
    • Page fetch, Chunking, Embedding, Upsert (per stage)
```

---

## RAG Retrieval Flow

The `search_knowledge_base` MCP tool performs vector search, re-ranking, and live refresh:

| Step | Details |
|------|---------|
| 1. Embed query | Ollama `nomic-embed-text` produces a 768-dim vector from the user's question |
| 2. Cosine search | PostgreSQL pgvector operator `<=>` on `Chunks.Embedding` column with HNSW index |
| 3. Threshold filter | Maximum cosine distance of **0.4** (minimum similarity ≈ **0.6**); widened to `Reranker:CandidateMaxCosineDistance` (default **0.5**) when re-ranking is enabled so the re-ranker has real candidates to reorder |
| 4. Candidate over-fetch | Ordered by cosine distance; top `Reranker:CandidateCount` (default **20**, or `topK` when re-ranking is disabled) chunks fetched |
| 5. Re-rank | `RerankingService` scores every candidate against the question via `IReranker` (TEI/Cohere/None), drops chunks below `Reranker:MinScore`, and keeps the top `min(Reranker:TopN, topK)`; on error, timeout, or when disabled it falls back to the original vector-similarity order |
| 6. Enrich results | Extract document title and source URL from `SourceDocuments` join |
| 7. Live refresh | For each unique page ID among the **kept** chunks only, attempt a live fetch from Confluence; use fresh text if available, fall back to cached chunk text |

Re-ranking emits a `rerank` activity event (candidates in, kept, elapsed time, before/after order and scores) or, on
failure, a `rerank_fallback` event (category: retrieval) so the step is visible in the live activity log.

### Result Contract

```typescript
interface KnowledgeBaseSearchHit {
    chunkId: string;
    documentTitle: string;
    sourceUrl: string;
    score: number;        // re-rank relevance (0..1) when re-ranking succeeded, else cosine similarity
    text: string;         // chunk or live Confluence content
}

interface RerankOutcome {            // present only when Reranker:Enabled=true
    provider: 'Tei' | 'Cohere' | 'None';
    candidatesIn: number;
    kept: number;
    elapsedMs: number;
    fellBack: boolean;
    fallbackReason: string | null;
    before: { title: string; score: number }[];   // vector-similarity order/scores
    after: { title: string; score: number }[];     // final re-ranked order/scores
}
```

---

## Data Model

### PostgreSQL Schema

```sql
-- SourceDocuments: ingested Confluence pages
CREATE TABLE SourceDocuments (
    Id             UUID PRIMARY KEY,
    ExternalId     VARCHAR NOT NULL UNIQUE,   -- Confluence page ID
    Title          VARCHAR NOT NULL,
    Content        TEXT NOT NULL,              -- plain text (HTML stripped)
    SourceUrl      VARCHAR NOT NULL,
    LastModifiedAt TIMESTAMPTZ NOT NULL,
    IngestedAt     TIMESTAMPTZ NOT NULL
);

-- Chunks: text segments with vector embeddings
CREATE TABLE Chunks (
    Id                 UUID PRIMARY KEY,
    SourceDocumentId   UUID NOT NULL REFERENCES SourceDocuments(Id) ON DELETE CASCADE,
    Ordinal            INTEGER NOT NULL,       -- chunk ordering within document
    Text               TEXT NOT NULL,
    Embedding          vector(768) NOT NULL
);

-- HNSW index on embeddings (cosine distance)
CREATE INDEX ChunksEmbeddingIdx ON Chunks USING hnsw (Embedding vector_cosine_ops);
```

### Hangfire Storage

Hangfire uses its own schema within the same `ragchatdemo` database for job queues, recurring job schedules, and execution history.

---

## External Dependencies

| Dependency | Role | Connection Details |
|------------|------|-------------------|
| **Ollama** | LLM chat (`qwen3.6:27b`) + embeddings (`nomic-embed-text`) | `http://host.docker.internal:11434` (host-mounted from Docker Desktop) |
| **PostgreSQL + pgvector** | Application data, vector search, Hangfire storage | K8s service `postgres:5432`; database `ragchatdemo` |
| **Hangfire** | Background job scheduling (hourly ingestion recurrence) | In-process within `ingestion-worker` pod; dashboard at `/hangfire` |
| **SignalR** | Real-time chat streaming and activity telemetry | Hubs hosted in `chat-api:8080`; auto-reconnect with backoff |
| **MCP (Model Context Protocol)** | Structured tool calling between ChatApi ↔ McpServer | HTTP transport on `mcp-server:8080` |
| **Reranker (TEI)** | Cross-encoder re-scoring of candidate chunks (`BAAI/bge-reranker-base`) | K8s service `reranker:80`; CPU-only, `Reranker:Provider=Tei` is the default; `Cohere` is an opt-in hosted alternative |
| **Confluence** | Knowledge base content source | Stub at `confluence-stub:8080` or real Confluence Cloud via REST API v2 |

---

## SignalR Protocol

### Chat Hub (`/hubs/chat`)

| Direction | Method / Event | Payload |
|-----------|---------------|---------|
| Client → Server | `SendMessage(string message)` | User's question text |
| Server → Client | `ResponseToken(string token)` | Incremental LLM response token |
| Server → Client | `ResponseComplete(Citation[] citations)` | Final response with source citations |
| Server → Client | `ResponseError(string error)` | Error message on failure |

### Activity Hub (`/hubs/activity`)

| Direction | Event | Payload |
|-----------|-------|---------|
| Server → Client | `ActivityEvent(ActivityEventDto)` | Telemetry from ingestion, MCP tools, and generation |

**Reconnection:** The frontend uses the SignalR auto-reconnect feature with delays of 0 ms, 2 s, 5 s, 10 s, and 30 s. After a chat response completes (or fails), the activity feed is resynchronized to recover any events dropped during reconnection.

---

## MCP Tool Integration

ChatApi acts as an **MCP client**; McpServer acts as an **MCP server**. Communication uses HTTP transport via the `ModelContextProtocol` SDK (v2.2.0).

### Registered Tools

| Tool | Description |
|------|-------------|
| `search_knowledge_base(query: string, topK?: number)` | Embeds query → pgvector candidate over-fetch (`CandidateCount`) → re-rank → top N → live Confluence refresh → returns ranked hits with citations and an optional `rerank` outcome |
| `create_support_ticket(title: string, description: string)` | Stub — generates sequential ticket IDs (e.g., `TICKET-1001`) with status `created` |

### Tool Dispatch Flow

```
ChatApi (MCP Client)                    McpServer (MCP HTTP Server)
  │                                          │
  │  tools/call({ name, args })              │
  ├─────────────────────────────────────────►│
  │                                          │
  │                                    Resolve tool handler
  │                                    Execute against pgvector / stub
  │                                          │
  │  { content: [...] }                       │
  ◄──────────────────────────────────────────┤
  │                                          │
  │  Append result as "tool" message          │
  │  Call Ollama again for final answer       │
```

---

## Frontend Architecture

**Framework:** Angular 21 (standalone components)  
**UI Library:** Bootstrap 5 + Bootstrap Icons  
**Real-time Transport:** `@microsoft/signalr` (v10)  
**State Management:** Angular Signals

### Key Components

| Component | Path | Responsibility |
|-----------|------|----------------|
| Chat UI | `app/chat/chat.ts` | Reactive form; message input; message list display; citation rendering |
| Chat SignalR Service | `app/core/chat-signalr.service.ts` | Connection lifecycle; send/receive; token streaming; state management via signals |
| Activity SignalR Service | `app/core/activity-signalr.service.ts` | Connects to activity hub; buffers events for the dashboard |
| Activity Log | `app/activity-log/` | Renders live ingestion, MCP tool, and generation telemetry |

### Frontend Proxy Configuration

The frontend container proxies SignalR connections (`/hubs/*`) to the `chat-api:8080` K8s service so the browser can reach hubs through the frontend's public port without CORS issues.

---

## Deployment Architecture

All services are deployed as single-replica Deployments with ClusterIP Services in the `rag-demo` Kubernetes namespace via Helm charts under `deploy/helm/`.

### Helm Charts

| Chart | Image | Port | Role |
|-------|-------|------|------|
| `postgres` | `pgvector/pgvector` | 5432 | PostgreSQL + pgvector vector store + Hangfire storage; persistent volume claim |
| `confluence-stub` | `ragchatdemo/confluence-stub:local` | 8080 | In-memory Confluence mock API |
| `mcp-server` | `ragchatdemo/mcp-server:local` | 8080 | MCP HTTP tool server |
| `reranker` | `ghcr.io/huggingface/text-embeddings-inference` (CPU) | 80 | Cross-encoder re-ranking (`BAAI/bge-reranker-base`), single replica; CPU request 2 / limit 8, memory limit 4Gi (~0.8s for 20 chunks) |
| `ingestion-worker` | `ragchatdemo/ingestion-worker:local` | 8080 | Hangfire jobs + dashboard |
| `chat-api` | `ragchatdemo/chat-api:local` | 8080 | SignalR hubs, RAG orchestration, OpenAPI |
| `frontend` | `ragchatdemo/frontend:local` | 80 | Angular SPA with SignalR proxy |

### Service Connectivity Map

```
Browser ──► frontend:80
              │
              ├─► chat-api:8080
              │       │
              │       ├─► postgres:5432          (application data, vector search)
              │       ├─► Ollama:11434           (chat model, embeddings)
              │       └─► mcp-server:8080
              │               │
              │               ├─► postgres:5432  (vector search)
              │               ├─► Ollama:11434   (query embedding)
              │               ├─► reranker:80    (candidate re-ranking, TEI)
              │               └─► confluence-stub:8080 (live content refresh, kept chunks only)
              │
ingestion-worker:8080
    │
    ├─► postgres:5432          (document/chunk storage, Hangfire)
    ├─► Ollama:11434           (chunk embeddings)
    ├─► confluence-stub:8080   (page fetching)
    └─► chat-api:8080          (activity event publishing)
```

### Build & Deploy Workflow

```bash
# 1. Build all Docker images (tagged :local)
deploy/scripts/build-images.ps1

# 2. Deploy to Kubernetes via Helm
deploy/scripts/deploy-all.ps1
```

Images are built locally and consumed from Docker Desktop's embedded image registry — no external registry is required. All charts use `imagePullPolicy: IfNotPresent`.

---

## Configuration

Shared configuration is managed through a Kubernetes ConfigMap (`rag-chat-demo-config`) and Secrets (`rag-chat-demo-secrets`).

### ConfigMap Keys

| Key | Purpose |
|-----|---------|
| `Ollama__BaseUrl` | Ollama API endpoint (typically `http://host.docker.internal:11434`) |
| `Ollama__EmbedModel` | Embedding model name (`nomic-embed-text`) |
| `Ollama__ChatModel` | Chat completion model name (`qwen3.6:27b`) |
| `Mcp__ServerUrl` | MCP server endpoint (`http://mcp-server:8080`) |
| `Reranker__Provider` | `Tei` (default, local), `Cohere` (opt-in hosted), or `None` (disabled) |
| `Reranker__CandidateCount` | Chunks fetched by vector search before re-ranking (default **20**) |
| `Reranker__TopN` | Final chunk count kept after re-ranking (default **5**, also capped by the tool's `topK`) |
| `Reranker__MinScore` | Minimum re-rank relevance score to keep a chunk (default **0** - cross-encoder scores for correct paraphrased matches can be very low, so ranking order is used instead of an absolute cutoff) |
| `Reranker__CandidateMaxCosineDistance` | Vector cutoff applied only when re-ranking is enabled (default **0.5**; **0.4** otherwise) |
| `Reranker__TimeoutSeconds` | Re-ranker request timeout before falling back to vector order (default **3**) |
| `Reranker__Tei__BaseUrl` | TEI reranker endpoint (`http://reranker:80`) |
| `ChatApi__BaseUrl` | Chat API endpoint for activity publishing (`http://chat-api:8080`) |
| `Confluence__UseStub` | `true` = use in-memory stub, `false` = real Confluence Cloud |
| `Confluence__BaseUrl` | Confluence REST API base URL |
| `Confluence__Email` | Confluence Cloud email address |

### Secrets

| Key | Purpose |
|-----|---------|
| `postgres-password` | PostgreSQL password (auto-generated on first deploy) |
| `confluence-api-token` | Confluence Cloud API token (empty when using stub) |
| `Reranker__Cohere__ApiKey` | Cohere rerank API key; only referenced when `Reranker__Provider=Cohere`, never a committed literal |

### Environment-Specific Flags

- **Swagger UI + OpenAPI spec** are served only when `ASPNETCORE_ENVIRONMENT=Development`.
- **`DOTNET_VALIDATE_ON_BUILD=false`** is set in the ChatApi deployment to work around .NET 10's stricter DI validation rejecting certain third-party service registrations at startup.

---

## Operational Notes

- All pods use **one replica** — this is a demo architecture, not production-hardened.
- PostgreSQL has a **persistent volume claim** to survive pod restarts; other services are stateless.
- The ingestion job runs **hourly**. Use the Hangfire dashboard (`/hangfire` on the ingestion-worker, http://localhost:8082/hangfire via `deploy/scripts/port-forward.ps1`) to trigger it manually.
- `deploy/scripts/port-forward.ps1` exposes every service on localhost (keep it in sync with `deploy/helm/`): UI `8080`, Chat API Swagger `8081/swagger`, Hangfire `8082/hangfire`, MCP server `8083`, reranker `8084` (`/health`, `/rerank`), Postgres `5432`.
- Re-ranking quality can be checked with `scripts/rerank-eval/run-eval.ps1 -Label <name>` (32 questions; toggle with `Reranker__Enabled`); latest results are in `specs/002-rerank-search-results/quickstart.md`.
- Tests: `dotnet test` per project under `tests/` (Shared, ChatApi, IngestionWorker, McpServer). The McpServer integration test needs Postgres on `localhost:5432` (run the port-forward script first).
- If the Confluence stub is updated (e.g., via `PUT /wiki/api/v2/pages/{id}`), the next hourly run will detect the changed timestamp and re-ingest that page.
- Ollama runs as a standalone Docker container on the host — not inside the K8s cluster — accessed via `host.docker.internal`.
