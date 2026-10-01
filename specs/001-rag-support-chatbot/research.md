# Phase 0 Research: RAG Support Chatbot Demo

## Decisions

### D1. Confluence data source
**Decision**: Implement `RagChatDemo.ConfluenceStub` (ASP.NET Core minimal API) returning
Confluence-REST-shaped JSON (`/wiki/api/v2/pages`, `/wiki/api/v2/pages/{id}`) for a small seeded
set of articles. `RagChatDemo.IngestionWorker` is coded against this contract via a typed
`IConfluenceClient` interface with two implementations: `ConfluenceCloudClient` (real, via API
token) and `ConfluenceStubClient` (points at the stub service URL). Default demo configuration
uses the stub so the system has zero dependency on signing up for an external account; switching
to a real Confluence Cloud free-tier space later is a configuration change only (base URL + API
token secret), not a code change.
**Rationale**: Satisfies FR-009 and Principle I (local-first, no required external dependency)
while leaving the "real Confluence" option fully wired for extra demo credibility.
**Alternatives considered**: Calling Confluence Cloud directly with no stub — rejected because it
makes the whole demo depend on an external account being available and working on interview day.

### D2. Embedding & chat model access
**Decision**: A thin `OllamaClient` (in `RagChatDemo.Shared`) wraps `POST /api/embeddings` (model
`nomic-embed-text`) and `POST /api/chat` with `"stream": true` (model `qwen3.6:27b` default,
`llama3.1:8b` via config override), using `HttpClient` with a long response timeout and reading
the streamed NDJSON response line-by-line, forwarding each token to the SignalR hub as it
arrives.
**Rationale**: Ollama's native `/api/chat` streaming (NDJSON) is simpler than standing up the
OpenAI-compatible endpoint for this use case and matches the "models already pulled" setup.
**Alternatives considered**: OpenAI-compatible `/v1/chat/completions` SSE endpoint — works
equally well but adds translation overhead for no benefit here.

### D3. Vector storage
**Decision**: PostgreSQL 16 + `pgvector` extension, accessed via EF Core with
`Pgvector.EntityFrameworkCore`. `Chunk.Embedding` is a `Vector(768)`-typed column (nomic-embed-text
dimensionality) with an HNSW index for cosine similarity search exposed through a repository
method used only by the MCP server's `search_knowledge_base` tool (never called directly by the
Chat API, per FR-007).
**Rationale**: Keeps retrieval logic behind the MCP boundary, matching the demo's "MCP skill
being called" narrative, and pgvector was already the agreed vector store.

### D4. MCP server tools
**Decision**: `RagChatDemo.McpServer` hosts two tools via the official `ModelContextProtocol`
C# SDK:
- `search_knowledge_base(query: string, top_k: int = 5)` → ranked chunk results with source
  page title/id and similarity score.
- `create_support_ticket(summary: string, details: string)` → stubbed ticket creation returning a
  fake ticket id/status (no real ticketing system integration).
The Chat API is an MCP client that lists available tools and lets the LLM decide (via Ollama's
`tools` capability) which to call, rather than hard-coding "always call search first".
**Rationale**: Matches FR-007/FR-008 and demonstrates a realistic, LLM-driven tool-calling loop
rather than a scripted pipeline.

### D5. Activity log transport
**Decision**: A single SignalR hub (`ActivityHub`, path `/hubs/activity`) hosted in
`RagChatDemo.ChatApi`. `IngestionWorker` and `McpServer` do not hold a SignalR connection
themselves; instead they `POST` activity events to an internal `RagChatDemo.ChatApi` endpoint
(`POST /internal/activity-events`, not exposed outside the cluster) which re-broadcasts to all
connected hub clients. A second hub method streams chat tokens (`ChatHub`, `/hubs/chat`) — these
can be the same hub with two methods if simpler during implementation.
**Rationale**: Avoids introducing a message broker (Kafka/RabbitMQ) purely for a demo (Principle
VI), while still giving every service a way to publish observable events (Principle III).
**Alternatives considered**: Redis pub/sub backplane — unnecessary at single-replica demo scale.

### D6. Chunking strategy
**Decision**: Simple paragraph-aware fixed-size chunking (~500 tokens/~2000 chars per chunk with
~10% overlap), implemented as a pure function in `RagChatDemo.Shared` so it's unit-testable
without external services.
**Rationale**: Good enough fidelity for a 10-30 article demo corpus; avoids pulling in a heavier
NLP library.

### D7. Kubernetes/Helm layout
**Decision**: One Helm chart per deployable (`postgres`, `chat-api`, `ingestion-worker`,
`mcp-server`, `confluence-stub`, `frontend`), each a small standalone chart (not subcharts of an
umbrella chart) sharing a common `rag-demo` namespace and a `ConfigMap`/`Secret` naming
convention (`rag-chat-demo-config`, `rag-chat-demo-secrets`). A `deploy/scripts/deploy-all.ps1`
script runs `helm upgrade --install` for each in dependency order (postgres → confluence-stub →
mcp-server → ingestion-worker → chat-api → frontend).
**Rationale**: Independent charts are simpler to reason about and redeploy individually during
iterative development than a single umbrella chart, matching Principle VI.

## Open follow-ups (non-blocking)
- Confirm nomic-embed-text's actual embedding dimensionality once pulled (expected 768) before
  fixing the pgvector column width in `data-model.md`.
