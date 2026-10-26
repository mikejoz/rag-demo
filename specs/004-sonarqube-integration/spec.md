# Feature Specification: SonarQube Quality Gates for RagChatDemo

**Status**: Complete (2026-10-26)

**Feature Branch**: `feature/sonarqube-community-build`

**Created**: 2026-10-26

**Input**: Integrate the RagChatDemo repository with the shared SonarQube Community Build instance, configuring separate backend and frontend quality scans via GitHub Actions, real coverage collection, and develop-first enforcement. Platform-level decisions are documented in [docs/adr/0001-sonarqube-quality-gates.md](../docs/adr/0001-sonarqube-quality-gates.md).

## Overview

The RagChatDemo repository contains a .NET backend (multiple xUnit test projects with database integration via pgvector) and an Angular frontend (Vitest tests with LCOV coverage). This feature establishes two independent SonarQube analysis pipelines — one per application — that run on push to `develop`, collect validated coverage data, wait for the real Quality Gate result, and fail the workflow if the gate is not met. A failing gate blocks manual promotion to `main`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Backend scans with test database (Priority: P1)

A developer pushes backend changes to `develop`. The GitHub Actions workflow provisions an ephemeral pgvector service container, runs all four xUnit test projects with OpenCover coverage collection, validates reports, uploads analysis to SonarQube, and waits for the Quality Gate. A failing gate causes the workflow to fail.

**Why this priority**: The backend contains core business logic; untested or low-quality changes risk data integrity in production.

**Independent Test**: Push a commit to `develop` that modifies backend code and verify the workflow completes with Quality Gate PASSED. Then push a commit that reduces coverage below threshold and confirm failure at the gate step, not at upload.

**Acceptance Scenarios**:

1. **Given** backend quality meets thresholds, **When** pushed to `develop`, **Then** the SonarQube backend workflow completes with Quality Gate PASSED and 73 tests passing.
2. **Given** the pgvector service container is unavailable at startup, **When** the workflow runs, **Then** it waits for health checks before proceeding or fails if the container cannot start.
3. **Given** an invalid OpenCover report, **When** validation runs, **Then** the workflow errors with details about the malformed file before upload.

---

### User Story 2 — Frontend scans with LCOV coverage (Priority: P1)

A developer pushes frontend changes to `develop`. The GitHub Actions workflow installs dependencies, runs Vitest with V8 coverage, builds the Angular app in production configuration, validates the LCOV report, uploads analysis, and waits for Quality Gate.

**Why this priority**: Frontend quality gates provide visibility into user-facing code quality for every develop push; a failing gate fails the workflow and blocks manual promotion to `main`.

**Independent Test**: Push a frontend change to `develop` and verify the workflow runs Vitest, generates LCOV coverage, validates it, and passes Quality Gate with 4 tests.

**Acceptance Scenarios**:

1. **Given** frontend quality meets thresholds, **When** pushed to `develop`, **Then** the SonarQube frontend workflow completes with Quality Gate PASSED and 4 Vitest tests passing.
2. **Given** a malformed LCOV file, **When** validation runs, **Then** the workflow errors before scanning.

---

### User Story 3 — Manual promotion verification (Priority: P1)

Before merging `develop` to `main`, a reviewer confirms both backend and frontend Sonar workflows passed Quality Gate for the exact latest develop SHA. Any new commits require fresh validation.

**Why this priority**: The GitHub private plan cannot enforce branch protection; manual gate checks are the only merge control.

**Independent Test**: Identify the latest develop commit SHA, navigate to each Sonar project, and verify the Quality Gate status is PASSED for that exact analysis.

**Acceptance Scenarios**:

1. **Given** both workflows passed for the latest develop SHA, **When** the reviewer verifies gate status, **Then** they confirm readiness to promote.
2. **Given** new commits landed after last scans, **When** preparing to promote, **Then** the reviewer waits for fresh workflow runs before proceeding.

---

### Edge Cases

- A project-scoped token (sqp_ prefix) was initially used for the frontend scan but caused failure because it could not access both Sonar projects; a Global Analysis Token (sqa_ prefix) stored as `SONAR_TOKEN` resolved this.
- Backend scanner disables multi-language auto-discovery to prevent duplicating frontend analysis in the backend project.
- Test projects run serially (`-m:1`) to avoid cross-project EF migration initialization races with the shared ephemeral database.
- Zero-hit lines in coverage reports are valid data, not validation errors.
- Local scanning with `.\scripts\SonarQube-Scan.ps1` permits develop only unless `-AllowNonDevelopBranch` explicitly authorizes overwriting the shared baseline.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST run backend Sonar analysis on push to `develop` via GitHub-hosted Ubuntu with .NET 10, JDK 21, and SonarScanner for .NET 12.0.0 (workflow: `sonarqube-backend.yml`).
- **FR-002**: System MUST provision an ephemeral `pgvector/pgvector:pg16` service container for backend database integration tests with trust authentication; no application database secrets are used.
- **FR-003**: System MUST collect OpenCover coverage from all four xUnit test projects using the Coverlet collector, validate reports for structure and source mappings before upload, and pass report paths at scanner begin.
- **FR-004**: System MUST run frontend Sonar analysis on push to `develop` via GitHub-hosted Ubuntu with Node 24 (workflow: `sonarqube-frontend.yml`).
- **FR-005**: System MUST generate LCOV coverage via Vitest with `@vitest/coverage-v8`, validate the report, and import through `sonar.javascript.lcov.reportPaths`.
- **FR-006**: Both workflows MUST set `sonar.qualitygate.wait=true` with timeout 300 seconds and block on real Quality Gate result.
- **FR-007**: System MUST use a single global analysis token (`SONAR_TOKEN`, sqa_ prefix) for authenticating both projects; project-scoped tokens are insufficient.
- **FR-008**: System MUST guard workflow execution to `develop` branch only and NOT analyze PRs.
- **FR-009**: System MUST use separate concurrency groups per project to prevent scan serialization between backend and frontend workflows.
- **FR-010**: Backend scanner MUST disable multi-language auto-discovery so it does not duplicate frontend analysis.
- **FR-011**: Test results and coverage artifacts MUST be retained for 7 days even when later workflow steps fail.
- **FR-012**: System MUST validate coverage reports via `Validate-SonarCoverage.ps1` before scanning — backend checks OpenCover structure, sequence points, and source mappings; frontend checks LCOV source mapping and line-data entries.

### Key Entities

- **Backend Sonar Project** (`mikejoz_ragchatdemo`): .NET analysis with OpenCover coverage from 4 test projects.
- **Frontend Sonar Project** (`mikejoz_ragchatdemo_frontend`): Angular/TypeScript analysis with Vitest LCOV coverage.
- **Ephemeral Test Database**: pgvector container provisioned per-backend-workflow-run; destroyed with the job.
- **Global Analysis Token**: `SONAR_TOKEN` secret (sqa_ prefix) granting Execute Analysis on both projects.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Backend scan passes Quality Gate with 73 tests across 4 validated OpenCover coverage files.
- **SC-002**: Frontend scan passes Quality Gate with 4 Vitest tests and validated LCOV coverage.
- **SC-003**: Coverage validation rejects malformed reports before upload on both backend and frontend pipelines.
- **SC-004**: Both workflows trigger automatically on push to `develop` and are available for manual dispatch.

## Assumptions

- The shared SonarQube instance at `https://sonar.michaeljozwik.me` is available during CI scan windows.
- The Cloudflare tunnel remains operational; if down, workflows fail fast at scanner begin.
- Both Sonar project keys (`mikejoz_ragchatdemo`, `mikejoz_ragchatdemo_frontend`) exist on the server (created by `Initialize-SonarProjects.ps1`).
- Database integration tests use `RAGCHATDEMO_TEST_CONNECTION_STRING` exclusively; there is no fallback to the application database.

## Out of Scope

- PR decoration and branch-level analysis (requires Developer Edition).
- Scanning from self-hosted runners.
- Enforcing branch protection rules via GitHub settings (current private plan limitation).
- The legacy VB.NET Calculator project — documented separately in Micromix.Calculator.License.Api: `specs/001-sonarqube-integration/spec.md`.
