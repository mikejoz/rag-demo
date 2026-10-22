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
observed by the user in real time, it is not considered complete. All production services MUST
provide structured logging with sufficient context (source, operation name, status, timestamp),
dedicated error logging for failure paths, and health check endpoints.

### IV. Secure Secret Handling (NON-NEGOTIABLE)
All inputs are untrusted. Secrets (API tokens, connection strings with credentials) MUST NEVER be
committed to source control or hard-coded. Local development uses `dotnet user-secrets`; cluster
deployments use Kubernetes `Secret` objects referenced via environment variables or mounted files.
Helm values files MUST only contain references (secret names/keys), never literal secret values.

Security requirements for all endpoints:
- **Validate** all user input before processing
- **Enforce** authorization on every operation that accesses protected resources
- **Sanitize** outputs rendered to clients (HTML, Markdown, dynamic content)
- Any violation found in review MUST block the change.

### V. Stack Conventions Are Mandatory
.NET services target the current LTS/STS SDK, use minimal hosting APIs, and prefer dependency
injection over static state. The Angular front-end MUST follow this organization's standing
Angular conventions: standalone components (no NgModules), signals for state, `input()`/`output()`
functions, `ChangeDetectionStrategy.OnPush`, native control flow (`@if`/`@for`/`@switch`),
reactive forms, and `inject()` over constructor injection. Accessibility is non-negotiable: every
screen MUST pass AXE checks and meet WCAG AA minimums.

### VI. Simplicity Over Reinvention
Prefer existing libraries, existing patterns, and existing services over custom implementations.
Avoid reinventing functionality already present in the codebase or ecosystem. This is also an
interview-demo system with a one-week horizon: prefer the simplest design that demonstrates the
architecture end-to-end over a production-hardened one. Defer non-demo-critical concerns
(multi-tenant auth, horizontal autoscaling, full retry/backoff policies) by recording them as
assumptions rather than building them speculatively. Every simplification MUST still satisfy
Principles I-V.

### VII. Test Coverage Mandatory (NON-NEGOTIABLE)
All business logic MUST be tested. Minimum requirements:
- **Unit tests** for all business logic and data transformation code
- **Critical path integration tests** covering end-to-end user flows
- A minimum of **90% line coverage** across the xUnit test projects, measured per-project and at
  the solution level

Test projects follow the same naming conventions as their source counterparts (e.g.,
`RagChatDemo.ChatApi.Tests` for `RagChatDemo.ChatApi`). Mock external dependencies (Ollama,
Confluence API) rather than hitting live services during automated test runs. Integration tests
MUST cover the RAG retrieval pipeline end-to-end using the Confluence stub.

### VIII. Architecture First (NON-NEGOTIABLE)
All solutions MUST prioritize maintainability over short-term speed. Architecture decisions MUST
be documented whenever introducing:
- New services
- New databases or data stores
- New APIs (internal or external)
- New technologies into the stack

Documentation takes the form of concise Architectural Decision Records (ADRs) stored in the
repository under `docs/adr/` and published to Personal Confluence under the appropriate workspace
hierarchy.

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

## API Contract Conventions

- **HTTP Semantics**: All endpoints MUST use correct HTTP methods (GET for queries, POST for
  commands, PUT/PATCH for updates) and return appropriate status codes. `4xx` for client
  errors, `5xx` for server failures. SignalR hub methods follow verb+noun naming convention
  (e.g., `SendMessage`, `GetActivity`).
- **Error Response Shape**: All API errors MUST return a consistent envelope:
  `{ "error": { "code": string, "message": string, "details?: object } }`. Use problem details
  (`ApplicationProblemDetails`) as the standard format. Never leak stack traces to clients in
  non-development environments.
- **API Versioning**: If an API surface changes incompatibly, version it via URL path prefix
  (`/api/v1/...`). The SignalR hub protocol is considered v1 and breaking changes require a new
  hub method rather than modifying the existing contract.

## Documentation Expectations

New features and architectural changes require documentation at multiple levels:

- **Inline documentation**: Public-facing types, methods, and SignalR hub methods MUST have
  XML doc comments summarizing purpose, parameters, and return values. Internal helpers may
  omit docs if self-explanatory.
- **README updates**: When a feature introduces new setup steps, environment variables, or
  configuration options, the relevant `README.md` files MUST be updated in the same commit.
- **API documentation**: New or modified endpoints MUST include OpenAPI annotations (Swagger)
  with descriptions, parameter types, and response schemas. SignalR hub methods must document
  client-facing contracts inline.
- **Configuration documentation**: Any new configuration setting (appsettings, Helm values,
  environment variables) MUST be documented in the relevant configuration reference file.
- **Architecture updates**: Any structural change (new service, new database table, changed
  deployment topology) MUST update `ARCHITECTURE.md` in the same commit.
- **Confluence publishing**: Every completed feature MUST be published to Personal Confluence
  under the hierarchy `Software Development → [Workspace] → [Project] → [Feature Name]`. See
  the "Confluence Integration" section below for automation details.

## Development Workflow

- Each feature proceeds: `spec.md` → `plan.md` → `tasks.md` → implementation → verification.
- Tasks are organized by independently testable user story; a story is not "done" until it can be
  demonstrated standalone.
- Changes that touch secret-handling, Helm values, or Kubernetes manifests require an explicit
  self-review against Principle IV before merge.
- Any change that adds, removes, or re-ports a deployed service (a Helm chart under deploy/helm/)
  MUST update deploy/scripts/port-forward.ps1 in the same change so every service stays reachable
  from localhost, and update the URL list in ARCHITECTURE.md if affected.
- Verification for each user story MUST include a manual end-to-end demo path, not just unit
  tests, since the deliverable is a live interview demonstration.

### Confluence Integration

After verification, each completed feature MUST be documented in Personal Confluence:

1. **Hierarchy**: `Software Development → [Workspace Name] → [Project Name] → [Feature Name]`
2. **Script location**: `scripts/publish-confluence.ps1` handles publishing via the Confluence Cloud API
3. **Authentication**: Credentials are stored in `dotnet user-secrets` (keys: `confluence:email`,
   `confluence:apiToken`) — never committed to source control
4. **Trigger**: The script runs after implementation completes, publishing the feature spec and
   summary as a Confluence page in Markdown format

## Governance

This constitution supersedes ad hoc conventions for this repository. Amendments require an
updated version line, a recorded rationale in the Sync Impact Report, and must not silently drop
a non-negotiable principle (IV, VII, VIII) without an explicit MAJOR version bump and justification.
Versioning: MAJOR for incompatible governance/principle removal, MINOR for new/expanded
principles or sections, PATCH for clarifications/wording. Compliance is self-reviewed by the
implementer at each task checkpoint; use this file as the source of truth when a decision in
`plan.md` or `tasks.md` appears to conflict with it.

**Version**: 1.3.0 | **Ratified**: 2026-10-01 | **Last Amended**: 2026-10-22
