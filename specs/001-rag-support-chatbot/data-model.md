# Phase 1 Data Model: RAG Support Chatbot Demo

## SourceDocument

Represents one knowledge-base page (real or stubbed Confluence content).

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| ExternalId | string | Source system's page id (Confluence page id) |
| Title | string | Page title |
| Content | string | Raw page body (plain text, HTML stripped at ingestion time) |
| SourceUrl | string | Link back to the source page (real or stub) for citation display |
| LastModifiedAt | DateTimeOffset | From source system; used to detect changes for re-ingestion |
| IngestedAt | DateTimeOffset | Last time this row was (re-)processed |

Relationships: one `SourceDocument` has many `Chunk`.

## Chunk

A segment of a `SourceDocument`'s text with its embedding, used for retrieval.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | PK |
| SourceDocumentId | Guid | FK → SourceDocument |
| Ordinal | int | Position within the document (for ordering/debug) |
| Text | string | Chunk text (~2000 chars) |
| Embedding | Vector(768) | pgvector column, `nomic-embed-text` output; HNSW cosine index |

Constraint: re-ingesting an unchanged `SourceDocument` (same `LastModifiedAt`) is a no-op;
re-ingesting a changed one deletes its existing `Chunk` rows and inserts fresh ones in the same
transaction (satisfies FR-013 — no unbounded duplicate growth).

## ChatMessage

A single turn in a conversation (not persisted long-term for this demo beyond the active
session; in-memory per connection is acceptable).

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Role | enum (User, Assistant) | |
| Text | string | Full text (assistant text accumulates as streaming completes) |
| CitedChunkIds | Guid[] | Populated for Assistant messages when grounded by retrieval |
| CreatedAt | DateTimeOffset | |

## ActivityEvent

A single observable background step, broadcast over the activity SignalR hub. Not persisted
(in-memory ring buffer for "replay last N on connect" is acceptable); this is a live log, not an
audit trail.

| Field | Type | Notes |
|---|---|---|
| Id | Guid | |
| Category | enum (Ingestion, Retrieval, McpTool, Generation) | Drives icon/grouping in UI |
| Source | string | e.g. "IngestionWorker", "McpServer", "ChatApi" |
| Operation | string | e.g. "fetch_page", "chunk", "embed", "upsert", "search_knowledge_base", "create_support_ticket", "generate_response" |
| Target | string? | e.g. page title, tool name |
| Status | enum (Started, Succeeded, Failed) | |
| Detail | string? | Short human-readable summary (e.g. "3 chunks stored") |
| Timestamp | DateTimeOffset | |

## McpToolInvocation (transient, not persisted)

Represents one call from the Chat API (MCP client) to the MCP server, logged as an
`ActivityEvent` with `Category = McpTool` rather than its own table.

| Field | Type | Notes |
|---|---|---|
| ToolName | string | `search_knowledge_base` or `create_support_ticket` |
| Arguments | JSON | Tool input as sent |
| ResultSummary | string | Short text summary of the tool's result for the activity log |
