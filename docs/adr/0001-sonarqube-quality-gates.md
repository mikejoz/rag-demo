# ADR-0001: SonarQube Quality Gates for RagChatDemo

**Status**: Accepted
**Date**: 2026-10-26

## Context

The RagChatDemo repository (`mikejoz/rag-demo`) contains two independent applications in a single Git repository: a .NET backend (RagChatDemo.ChatApi and related projects) and an Angular/TypeScript frontend. A centralized SonarQube instance was adopted for cross-repo quality analysis. See `Micromix.Calculator.License.Api: docs/adr/0001-sonarqube-community-build.md` for the platform-level decision.

This ADR documents RagChatDemo-specific decisions for integrating with that shared SonarQube instance.

## Decision

### Two Sonar Projects from One Repository

One GitHub repository feeds two independent Sonar projects:

| Project | Key | Language | Coverage |
|---|---|---|---|
| .NET Backend | `mikejoz_ragchatdemo` | C# / .NET 10 | OpenCover (4 test projects) |
| Angular Frontend | `mikejoz_ragchatdemo_frontend` | TypeScript / Angular | Vitest V8 LCOV |

Each project has its own GitHub Actions workflow and its own concurrency group to prevent scan serialization conflicts.

### Shared Global Analysis Token

A single repository secret `SONAR_TOKEN` (sqa_ prefix Global Analysis Token) grants Execute Analysis permission on both projects. A project-scoped token (sqp_ prefix) was tried first for the frontend but failed because it could not access the second project. The global token is stored as a GitHub secret alongside `SONAR_HOST_URL`.

### Hosted Ubuntu Workflows

Both workflows use GitHub-hosted Ubuntu runners with full Git history (`fetch-depth: 0`), develop-push triggers, and manual dispatch:

- **Backend** (`sonarqube-backend.yml`): Runs on Ubuntu latest, provisions a `pgvector/pgvector:pg16` ephemeral service for database integration tests, uses .NET 10, JDK 21, and SonarScanner for .NET 12.0.0. All four xUnit test projects collect OpenCover coverage through Coverlet; reports are validated for structure before upload. The scanner disables multi-language auto-discovery so the backend scan does not analyze frontend files. Test projects execute serially (`-m:1`) to avoid cross-project initialization races. TRX and coverage artifacts are retained for 7 days.

- **Frontend** (`sonarqube-frontend.yml`): Runs on Ubuntu latest with Node 24. Installs dependencies via `npm ci`, runs Vitest with `@vitest/coverage-v8` for LCOV report generation, then builds the Angular app in production configuration. The LCOV report at `coverage/rag-chat-demo/lcov.info` is validated before scanning. Uses the pinned official SonarSource scan action (`SonarSource/sonarqube-scan-action@d209202...`).

### Develop-Only Scans with Manual Promotion

Both workflows guard execution to `develop` only (`if: github.ref == 'refs/heads/develop'`). Each sets `sonar.qualitygate.wait=true` with a 300-second timeout. Because Community Build provides one analysis slot per project (no branch isolation), scanning any other branch would overwrite the develop baseline. Before promoting `develop` to `main`, manually confirm both workflows passed for the exact latest develop SHA.

## Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Project-scoped token (sqp_) | Cannot access multiple Sonar projects; failed on first frontend scan |
| PR decoration analysis | Requires Developer Edition; Community Build does not support it |
| Branch-level scanning | Not available in Community Build; would require separate projects per branch |

## Consequences

### Positive

- Both projects passed their first Quality Gate scans: backend reported 73 tests with 4 validated OpenCover reports; frontend reported 4 Vitest tests with LCOV coverage.
- The ephemeral pgvector test database in CI eliminates dependency on application secrets while providing real integration testing.
- Coverage validation prevents false confidence from malformed or zero-mapping reports.
- Separate concurrency groups prevent the backend and frontend scans from serializing against each other.

### Negative

- Community Build's single-analysis-slot limit means local development scans overwrite the same baseline as CI for a given project. Developers must be careful not to overlap local and hosted runs.
- The shared SonarQube instance must remain online during CI scan windows; if the tunnel is down, both workflows fail at scanner begin.
- Manual promotion adds a process step that relies on human diligence rather than automated enforcement.

## Follow-ups

- When Developer Edition becomes available or costs justify it, enable PR decoration for pre-merge quality checks.
- Periodically review and raise Quality Gate thresholds for backend and frontend independently as coverage and code health improve.
- Consider consolidating the shared `Validate-SonarCoverage.ps1` script into a reusable workflow action if other repositories adopt similar validation patterns.
