# Implementation Plan: RAG Support Chatbot Demo

**Branch**: `001-rag-support-chatbot` | **Date**: 2026-10-01 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-rag-support-chatbot/spec.md`

## Summary

Build a fully local RAG support chatbot: an Angular front-end (chat + live activity log) talks
to a .NET Chat API over SignalR for streamed, source-grounded answers. The Chat API retrieves
context via an MCP server tool (`search_knowledge_base`, backed by Postgres/pgvector) and calls
a second stubbed MCP tool to demonstrate general tool-calling. A separate Hangfire worker
fetches knowledge-base pages (real Confluence Cloud free tier, or a local stub with the same
contract), chunks and embeds them via Ollama (`nomic-embed-text`), and upserts into pgvector.
Chat completion uses Ollama (`qwen3.6:27b` default, `llama3.1:8b` fallback). All services deploy
to the local Docker Desktop Kubernetes cluster via Helm, in the `rag-demo` namespace.

## Technical Context

**Language/Version**: C# / .NET 10 (ASP.NET Core); TypeScript 5 / Angular 21 (standalone,
signals)

**Primary Dependencies**: ASP.NET Core SignalR, Entity Framework Core + `Pgvector.EntityFrameworkCore`,
Hangfire (+ `Hangfire.PostgreSql`), official MCP C# SDK (`ModelContextProtocol`), Angular
`@microsoft/signalr` client, Ollama HTTP API (chat/completions + embeddings)

**Storage**: PostgreSQL 16 with the `pgvector` extension (documents, chunks with embedding
column); Hangfire job storage in the same Postgres instance (separate schema)

**Testing**: xUnit for .NET services (unit + a small set of integration tests against a test
Postgres); Karma/Jasmine (Angular CLI default) for front-end unit tests; manual end-to-end demo
script per user story (per constitution's Development Workflow)

**Target Platform**: Docker Desktop Kubernetes (local), container images built for linux/amd64;
Ollama remains on the Windows host, reached from pods via `host.docker.internal:11434`

**Project Type**: Web application (Angular frontend + multiple .NET backend services)

**Performance Goals**: First streamed token within 5s for a ~50-chunk knowledge base (SC-001);
not designed for concurrent load beyond a handful of simultaneous demo users

**Constraints**: Fully local/offline-capable (Principle I); no secrets in source control
(Principle IV); every cross-service operation must emit an activity event (Principle III)

**Scale/Scope**: Single demo knowledge-base space, ~10-30 seed articles, single-user
unauthenticated usage (per spec Assumptions)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|---|---|---|
| I. Local-First | All runtime deps (Postgres, Ollama, Hangfire, MCP, Angular) run in Docker Desktop K8s or on the local host; Confluence stub provides offline fallback | PASS |
| II. Spec-Driven Delivery | This plan derives from `spec.md`; tasks will reference FR-IDs | PASS |
| III. Observable by Design | SignalR activity hub is a first-class component in every service (Chat API, Ingestion Worker, MCP Server) | PASS |
| IV. Secure Secret Handling | Confluence token (if real) via k8s Secret + user-secrets; no literal secrets in Helm values | PASS |
| V. Stack Conventions | Angular plan uses standalone/signals/OnPush/reactive forms/inject(); .NET uses minimal hosting + DI | PASS |
| VI. Simplicity Over Completeness | No auth, no autoscaling, no multi-tenant; single namespace, single replica per service | PASS |

No violations requiring Complexity Tracking justification.

## Project Structure

### Documentation (this feature)

```text
specs/001-rag-support-chatbot/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output (MCP tool + REST/SignalR contracts)
├── checklists/
│   └── requirements.md
└── tasks.md              # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
RagChatDemo.sln

src/
├── RagChatDemo.Shared/              # EF Core entities (Document, Chunk w/ vector column),
│                                     # activity event contracts, Ollama/MCP DTOs
├── RagChatDemo.ChatApi/             # ASP.NET Core: /api/chat (SignalR hub "chat" + "activity"),
│                                     # Ollama chat client, MCP client, RAG orchestration
├── RagChatDemo.IngestionWorker/     # ASP.NET Core host + Hangfire server/dashboard,
│                                     # Confluence/stub client, chunker, embedding client
├── RagChatDemo.McpServer/           # MCP server: search_knowledge_base + create_support_ticket
└── RagChatDemo.ConfluenceStub/      # Minimal API mimicking Confluence REST contract (fallback)

tests/
├── RagChatDemo.ChatApi.Tests/
├── RagChatDemo.IngestionWorker.Tests/
└── RagChatDemo.McpServer.Tests/

frontend/
└── rag-chat-demo/                  # Angular app: chat panel, activity log panel, SignalR service
    └── src/app/
        ├── chat/
        ├── activity-log/
        └── core/ (services: chat-signalr.service.ts, etc.)

deploy/
├── helm/
│   ├── postgres/
│   ├── chat-api/
│   ├── ingestion-worker/
│   ├── mcp-server/
│   ├── confluence-stub/
│   └── frontend/
└── scripts/
    ├── setup-ollama-models.ps1     # ollama pull nomic-embed-text, etc.
    ├── seed-knowledge-base.ps1
    └── deploy-all.ps1              # helm install/upgrade for all charts, in dependency order
```

**Structure Decision**: Web application structure (Option 2) — `frontend/` (Angular) +
multiple focused .NET services under `src/` rather than one monolithic backend, so each service
maps 1:1 to a Helm chart/Deployment and can be demoed/scaled independently, per the architecture
agreed with the user (Chat API, Ingestion Worker/Hangfire, MCP Server as distinct deployables).
`RagChatDemo.Shared` avoids duplicating the EF Core model and DTOs across services.

## Complexity Tracking

*No constitution violations — table intentionally omitted.*
