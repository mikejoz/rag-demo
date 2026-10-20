# Implementation Plan: Prompt Grounding

**Branch**: `003-prompt-grounding` | **Date**: 2026-10-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-prompt-grounding/spec.md`

## Summary

Make the support chatbot answer only from retrieved knowledge-base content, say it could not find information when retrieval is empty or irrelevant, and decline off-topic requests. Today `RagOrchestrator` lets the model decide whether to call `search_knowledge_base`, and a direct (no-tool) answer is streamed straight to the user, which bypasses grounding. Approach: (1) strengthen the system prompt and add an explicit grounding instruction to the final generation turn, (2) never stream a direct first-turn answer — buffer and discard it, then force a knowledge-base search, (3) treat retrieved text as data, (4) enforce a relevance floor so weak matches count as "not found". See [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET (existing solution); Angular front-end unchanged

**Primary Dependencies**: Existing ASP.NET Core, SignalR, MCP client, Ollama (`qwen3.6:27b`), pgvector, reranker service

**Storage**: N/A (no schema change)

**Testing**: xUnit in `tests/RagChatDemo.ChatApi.Tests` (pattern: `RagOrchestratorRerankFallbackTests`) plus manual end-to-end demo

**Target Platform**: Docker Desktop Kubernetes, `rag-demo` namespace

**Project Type**: Web service (Chat API) in a multi-service solution

**Performance Goals**: No meaningful latency regression; forced search adds one search call only when the model skips tools

**Constraints**: Local Ollama only; responses still stream token-by-token; activity events for new steps

**Scale/Scope**: Demo scale; changes confined to Chat API orchestrator, reranker threshold config, Helm values

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Local-first | Pass | No new external dependencies |
| II. Spec-driven | Pass | Traces to FR-001..FR-009 |
| III. Observable | Pass | Activity events for forced retrieval and grounded fallback |
| IV. Secrets | Pass | Only a numeric threshold in Helm values |
| V. Stack conventions | Pass | Backend only |
| VI. Simplicity | Pass | Prompt and control-flow change, no new service |
| Port-forward upkeep | N/A | No service added or re-ported |

Post-design re-check: still passes.

## Project Structure

### Documentation (this feature)

```text
specs/003-prompt-grounding/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
└── tasks.md             # created by /speckit-tasks
```

No `contracts/`: SignalR hub and MCP tool contracts are unchanged.

### Source Code (repository root)

```text
src/
├── RagChatDemo.ChatApi/Chat/RagOrchestrator.cs             # forced retrieval, buffered direct answers, grounded turn
├── RagChatDemo.ChatApi/Chat/GroundingPolicy.cs             # new: prompts and fallback/off-topic messages
├── RagChatDemo.McpServer/Tools/SearchKnowledgeBaseTool.cs  # relevance floor wiring
└── RagChatDemo.Shared/Reranking/RerankOptions.cs           # MinScore default

deploy/helm/reranker, deploy/helm/mcp-server                # threshold values

tests/RagChatDemo.ChatApi.Tests/
└── RagOrchestratorGroundingTests.cs                        # new
```

**Structure Decision**: Keep the existing layout; centralize grounding text in one small `GroundingPolicy` class so it is testable and reviewable.

## Complexity Tracking

No constitution violations.
