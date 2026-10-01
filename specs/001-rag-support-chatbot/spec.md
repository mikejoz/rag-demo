# Feature Specification: RAG Support Chatbot Demo

**Feature Branch**: `001-rag-support-chatbot`

**Created**: 2026-10-01

**Status**: Draft

**Input**: User description: "An AI support chat bot that uses live data from Confluence or a
similar system. A background pipeline pulls, chunks, and vectorizes content to a vector
database. A front-end handles user interaction and also displays a live log of background
activity (e.g., ingestion steps, MCP skill calls) so an observer can see what the system is
doing. Chat responses must stream. Everything runs locally (Docker Desktop Kubernetes, local
Ollama models, local Postgres)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ask a question and get a grounded, streamed answer (Priority: P1)

A support user types a question into the chat UI. The system retrieves relevant knowledge-base
content, calls the local LLM, and streams the answer back token-by-token, citing which source
pages were used.

**Why this priority**: This is the core value proposition and the centerpiece of the interview
demo — without it there is no chatbot.

**Independent Test**: With the knowledge base already populated (via User Story 2 or seed data),
send a question through the chat UI and confirm a streamed, source-grounded answer appears
without needing the ingestion pipeline or MCP server to be actively running at that moment.

**Acceptance Scenarios**:

1. **Given** the knowledge base contains relevant chunks, **When** the user submits a question,
   **Then** the response begins streaming within a few seconds and the completed answer
   references at least one retrieved source page.
2. **Given** the knowledge base has no relevant content for the question, **When** the user
   submits it, **Then** the system responds that it could not find relevant information rather
   than fabricating an answer.
3. **Given** a response is streaming, **When** the user views the UI, **Then** text appears
   incrementally rather than all at once.

---

### User Story 2 - Observe live background activity while the system works (Priority: P1)

An observer (the interviewer) watches a live activity log panel that shows, in real time, what
the system is doing behind the scenes: ingestion steps (fetch/chunk/embed/store), the chat
pipeline's retrieval step, and MCP tool invocations (tool name, and a summary of its result).

**Why this priority**: This is the explicit "wow factor" requirement — the demo's differentiator
is making invisible AI plumbing visible.

**Independent Test**: Trigger an ingestion run and, separately, a chat question, and confirm the
activity panel shows a live, ordered sequence of events for each, without needing to inspect
server logs.

**Acceptance Scenarios**:

1. **Given** the activity log panel is open, **When** a background ingestion job runs, **Then**
   events appear in order for fetching, chunking, embedding, and storing, each with a source
   identifier.
2. **Given** the activity log panel is open, **When** the chat pipeline calls an MCP tool,
   **Then** an event shows the tool name and a short result summary before the final answer is
   shown.
3. **Given** multiple events occur within the same second, **When** they are displayed, **Then**
   their relative order is preserved.

---

### User Story 3 - Keep the knowledge base current via scheduled ingestion (Priority: P2)

A background job periodically (and on-demand, via a dashboard trigger) pulls knowledge-base
pages, splits them into chunks, generates embeddings, and upserts them into the vector store so
new or changed content becomes searchable without redeploying the system.

**Why this priority**: Demonstrates the Hangfire-based pipeline and keeps retrieval content
fresh, but the chatbot (US1/US2) can be demoed against a one-time seeded dataset if this story
is not yet running.

**Independent Test**: Trigger the ingestion job manually from the Hangfire dashboard and verify
new/changed source pages are reflected in subsequent chat answers.

**Acceptance Scenarios**:

1. **Given** a source page is added or edited, **When** the ingestion job next runs, **Then** the
   vector store contains updated chunks for that page and stale chunks for removed pages are not
   returned in future searches.
2. **Given** an operator opens the Hangfire dashboard, **When** they trigger the ingestion job
   manually, **Then** job progress and history are visible there.

---

### User Story 4 - Demonstrate a generic MCP tool-calling pattern (Priority: P3)

Beyond knowledge-base search, the chat pipeline can invoke a second, distinct MCP tool (e.g.
creating or looking up a support ticket) to show that the architecture supports general
tool-calling, not just retrieval.

**Why this priority**: Reinforces the MCP talking point for the interview but is not required
for the core RAG narrative.

**Independent Test**: Ask a question that plausibly triggers the second tool (e.g., "can you log
a ticket for this issue?") and confirm the activity log shows that specific tool being invoked
with a stubbed result.

**Acceptance Scenarios**:

1. **Given** a user message matches the second tool's intent, **When** the chat pipeline
   processes it, **Then** the activity log shows that tool (not the search tool) being invoked.

### Edge Cases

- What happens when the local LLM (Ollama) is unreachable? The chat UI MUST show a clear error
  in both the answer area and the activity log rather than hanging indefinitely.
- What happens when the vector store is empty (no ingestion has ever run)? The system MUST
  respond that no knowledge base content is available instead of calling the LLM with empty
  context.
- What happens when an MCP tool call fails or times out? The activity log MUST show the failure
  and the chat pipeline MUST still attempt to answer using whatever context it already has.
- What happens when the ingestion source (real or stub Confluence) is temporarily unreachable?
  The job MUST log the failure as an activity event and leave existing vector data untouched.
- How does the system behave with duplicate/overlapping chunks from re-ingesting an unchanged
  page? Re-ingestion MUST update existing chunks in place rather than growing unbounded
  duplicates.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a chat interface where a user submits a natural-language
  question and receives a streamed, token-by-token response.
- **FR-002**: The system MUST retrieve relevant knowledge-base chunks for each question using
  vector similarity search before generating a response.
- **FR-003**: The system MUST ground its answer in retrieved content and indicate which source
  page(s) informed the answer.
- **FR-004**: The system MUST display a live, real-time activity log of background operations,
  including ingestion steps and MCP tool invocations, each with an operation name, source/target
  identifier, status, and timestamp.
- **FR-005**: The system MUST run a background ingestion pipeline that fetches knowledge-base
  pages, splits them into chunks, generates vector embeddings, and stores/updates them in a
  vector database, on a schedule and on manual trigger.
- **FR-006**: The ingestion pipeline MUST expose an operator dashboard showing job run history
  and allowing manual trigger.
- **FR-007**: The system MUST expose knowledge-base search as a callable tool through an MCP
  server, invoked by the chat pipeline rather than querying the vector store directly inline.
- **FR-008**: The system MUST expose at least one additional, distinct MCP tool (beyond
  knowledge-base search) to demonstrate general tool-calling, using stubbed/fake data.
- **FR-009**: The knowledge-base content source MAY be a real Confluence Cloud instance or a
  local stub implementing an equivalent page-listing/content contract; the rest of the system
  MUST behave identically regardless of which is configured.
- **FR-010**: The system MUST run entirely on local infrastructure: local Kubernetes (Docker
  Desktop), local Postgres, and the existing local Ollama models — no required calls to paid
  external AI services.
- **FR-011**: The system MUST NOT store secrets (API tokens, credentials) in source control; real
  Confluence credentials, if used, MUST be supplied via Kubernetes Secrets (cluster) or
  `dotnet user-secrets` (local dev).
- **FR-012**: When no relevant knowledge-base content is found, the system MUST say so rather
  than generating an unsupported answer.
- **FR-013**: Re-running ingestion for an already-ingested, unchanged source page MUST NOT create
  duplicate chunks.

### Key Entities

- **Source Document**: A knowledge-base page (real or stubbed Confluence content) with an
  identifier, title, body content, and last-modified timestamp.
- **Chunk**: A segment of a Source Document's text, with its vector embedding and a reference
  back to the originating document, used for retrieval.
- **Chat Message**: A user question or assistant answer within a conversation, including which
  Chunks (if any) were used to ground an assistant answer.
- **Activity Event**: A single observable step in the system's background behavior (ingestion
  step, retrieval step, MCP tool invocation), with a category, description, status, and
  timestamp, broadcast to any connected observers.
- **MCP Tool Invocation**: A record of a tool name, its input parameters, and its result summary,
  as called by the chat pipeline.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user receives the start of a streamed answer within 5 seconds of submitting a
  question, for a knowledge base of at least 50 chunks.
- **SC-002**: An observer can watch the activity log and correctly narrate, without looking at
  server logs, each distinct step the system performs for a single chat question (retrieval, MCP
  tool call, generation).
- **SC-003**: After editing or adding a source page and running ingestion, a subsequent question
  about that content is answered using the updated information, verified without redeploying any
  service.
- **SC-004**: The entire system (front-end, chat API, ingestion worker, MCP server, Postgres)
  runs successfully on the local Docker Desktop Kubernetes cluster with zero required external
  network calls to paid services.
- **SC-005**: The chat UI and all its states pass automated accessibility (AXE) checks with zero
  critical/serious violations.

## Assumptions

- A single demo "knowledge base" space with on the order of 10-30 seeded support/IT articles is
  sufficient to make the retrieval and ingestion stories convincing; large-scale corpus size is
  out of scope.
- Single-user, unauthenticated demo usage is acceptable; multi-tenant auth/authorization is out
  of scope for this feature.
- "Live data from Confluence or a similar system" is satisfied by either a real free-tier
  Confluence Cloud space or a local stub exposing an equivalent read API; both are acceptable
  and the choice does not change any other functional requirement.
- The existing local Ollama instance (with `qwen3.6:27b`, `llama3.1:8b`, and a to-be-added
  `nomic-embed-text`) remains available on `localhost:11434` throughout development and the
  demo, and is shared with other unrelated local projects (must not be reconfigured in a way
  that breaks them).
- Network/ingress exposure is local-only (port-forward or local-only ingress); public internet
  exposure of the demo is out of scope.
