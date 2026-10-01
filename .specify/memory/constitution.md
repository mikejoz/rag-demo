<!--
Sync Impact Report
- Version change: (none) → 1.0.0 (initial ratification)
- Modified principles: N/A (initial version)
- Added sections: Core Principles (I-VI), Technology & Environment Constraints,
  Development Workflow, Governance
- Removed sections: none
- Deferred TODOs: none
-->

# RagChatDemo Constitution

## Core Principles

### I. Local-First, Fully Offline-Capable
Every component MUST run entirely on local infrastructure (Docker Desktop Kubernetes, local
Ollama models, local Postgres). No feature may introduce a hard dependency on a paid cloud
service; where an external SaaS (e.g., Confluence Cloud) is used for realism, the system MUST
also work against a local stub implementing the same contract, and the stub MUST be the default
for automated verification.

### II. Spec-Driven Delivery
Features are defined by a spec (`spec.md`) and plan (`plan.md`) before implementation tasks are
generated. Code changes MUST trace back to a functional requirement or task ID. Scope creep
beyond the current spec's user stories requires a spec update first, not ad hoc implementation.

### III. Observable by Design
Every background or cross-service operation (ingestion steps, MCP tool invocations, LLM calls)
MUST emit a structured activity event visible in the live activity log. If an operation cannot be
observed by the user in real time, it is not considered complete. Logs and events MUST include
enough context (source, operation name, status, timestamp) to narrate the system's behavior to a
non-technical observer.

### IV. Secure Secret Handling (NON-NEGOTIABLE)
Secrets (API tokens, connection strings with credentials) MUST NEVER be committed to source
control or hard-coded. Local development uses `dotnet user-secrets`; cluster deployments use
Kubernetes `Secret` objects referenced via environment variables or mounted files. Helm values
files MUST only contain references (secret names/keys), never literal secret values. Any
violation found in review MUST block the change.

### V. Stack Conventions Are Mandatory
.NET services target the current LTS/STS SDK, use minimal hosting APIs, and prefer dependency
injection over static state. The Angular front-end MUST follow this organization's standing
Angular conventions: standalone components (no NgModules), signals for state, `input()`/`output()`
functions, `ChangeDetectionStrategy.OnPush`, native control flow (`@if`/`@for`/`@switch`),
reactive forms, and `inject()` over constructor injection. Accessibility is non-negotiable: every
screen MUST pass AXE checks and meet WCAG AA minimums.

### VI. Simplicity Over Completeness (Demo Scope)
This is an interview-demo system with a one-week horizon. Prefer the simplest design that
demonstrates the architecture end-to-end over a production-hardened one. Defer
non-demo-critical concerns (multi-tenant auth, horizontal autoscaling, full retry/backoff
policies) by recording them as assumptions rather than building them speculatively. Every
simplification MUST still satisfy Principles I-V.

## Technology & Environment Constraints

- **Runtime targets**: ASP.NET Core (Chat API, Ingestion Worker, MCP Server), Angular
  (front-end), PostgreSQL + pgvector (vector store), Ollama (LLM + embeddings), Hangfire
  (scheduled/background ingestion), Kubernetes on Docker Desktop (deployment target), Helm
  (packaging).
- **LLM access**: all models are served by the existing local Ollama instance on
  `localhost:11434` (reachable from pods as `host.docker.internal:11434`). Chat completion
  defaults to `qwen3.6:27b` (GPU-accelerated) with `llama3.1:8b` as a configurable fallback.
  Embeddings use `nomic-embed-text`.
- **Streaming**: user-facing chat responses MUST stream token-by-token to the client (SignalR)
  rather than returning a single final message.
- **Namespace**: Kubernetes resources are deployed to the `rag-demo` namespace.

## Development Workflow

- Each feature proceeds: `spec.md` → `plan.md` → `tasks.md` → implementation → verification.
- Tasks are organized by independently testable user story; a story is not "done" until it can be
  demonstrated standalone.
- Changes that touch secret-handling, Helm values, or Kubernetes manifests require an explicit
  self-review against Principle IV before merge.
- Verification for each user story MUST include a manual end-to-end demo path, not just unit
  tests, since the deliverable is a live interview demonstration.

## Governance

This constitution supersedes ad hoc conventions for this repository. Amendments require an
updated version line, a recorded rationale in the Sync Impact Report, and must not silently drop
a non-negotiable principle (III, IV) without an explicit MAJOR version bump and justification.
Versioning: MAJOR for incompatible governance/principle removal, MINOR for new/expanded
principles or sections, PATCH for clarifications/wording. Compliance is self-reviewed by the
implementer at each task checkpoint; use this file as the source of truth when a decision in
`plan.md` or `tasks.md` appears to conflict with it.

**Version**: 1.0.0 | **Ratified**: 2026-10-01 | **Last Amended**: 2026-10-01
