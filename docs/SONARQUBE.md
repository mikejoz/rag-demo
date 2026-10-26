# SonarQube Community Build

One GitHub repository (`mikejoz/rag-demo`) feeds two independent Sonar projects:

| Project | Key | Coverage |
| --- | --- | --- |
| .NET backend | `mikejoz_ragchatdemo` | OpenCover from all four xUnit test projects |
| Angular frontend | `mikejoz_ragchatdemo_frontend` | Vitest V8 LCOV |

## Repository configuration

- Secret `SONAR_TOKEN`: one user token whose owner has Execute Analysis
  permission on **both** projects. A project-specific token cannot cover both.
- Variable `SONAR_HOST_URL=https://sonar.michaeljozwik.me`; a secret with the
  same name takes precedence.
- Both project keys must exist on the server. Keep the Sonar server and tunnel
  available during hosted scans.

## Hosted scans and promotion

Both workflows use Ubuntu hosted runners, full Git history, develop push and
manual dispatch triggers, and a develop-ref job guard. They do not analyze PRs.
Each project serializes its own scans without cancelling an in-progress run.
The backend uses .NET 10, JDK 21, and SonarScanner for .NET 12.0.0; the frontend
uses Node 24 and the pinned official Sonar scan action.

Each scanner waits up to 300 seconds for its actual Quality Gate. A successful
upload alone is not a passing gate. Before promoting `develop` to `main`,
manually confirm **both workflows passed for the exact latest develop SHA**.
Any new commits or conflict-resolution changes require fresh validation.
The current private GitHub plan cannot enforce branch protection, so reviewers
must enforce this release policy. Keep scan triggers on develop.

Community Build has one analysis slot per project, not branch/PR isolation.
The UI may label that slot `main`; no rename or branch parameters are needed.
Do not overlap a local scan with CI for the same project: results overwrite
the same baseline.

The first hosted authentication, source analysis, coverage import, and gate
results remain pending merge into develop. Local checks do not establish these.

## Backend tests and database

The workflow provisions an ephemeral `pgvector/pgvector:pg16` service with a
loopback-only port and trust authentication, exclusively for nonproduction
tests. It is destroyed with the job; no application database secrets are used.

Database integration tests require `RAGCHATDEMO_TEST_CONNECTION_STRING`.
There is deliberately no fallback to the application's database. Fixtures run
the existing EF migrations; test projects execute serially (`-m:1`) to avoid
cross-project initialization races. Existing assertions and test data cleanup
remain in place. The Chat API test factory overrides only its database setting.

All four projects collect OpenCover through the existing Coverlet collector.
Reports under `TestResults/*/coverage.opencover.xml` are checked for valid
OpenCover structure, sequence points, and existing production source mappings
before upload. The single directory wildcard avoids importing duplicate TRX
attachment copies. TRX and coverage artifacts are retained for seven days even
when a later step fails. Zero-hit lines are valid data, not validation errors.

The .NET scanner does **not** read the root `sonar-project.properties`.
Active backend exclusions and report paths are passed at scanner begin by
the workflow and local script. Multi-language auto-discovery is disabled so
the backend scan does not duplicate frontend analysis.

## Frontend coverage

From `frontend/rag-chat-demo`:

```powershell
npm ci
npm run test:coverage
npm run build -- --configuration production
```

Vitest and its explicit `@vitest/coverage-v8` provider resolve to 4.1.11.
The report is `coverage/rag-chat-demo/lcov.info`. The shared validation script
checks every source mapping and line-data entry before scanning. LCOV is
imported through `sonar.javascript.lcov.reportPaths`; spec files retain their
existing test classification. No thresholds or source exclusions were weakened.
The app title test targets the current navbar title and mocks the activity
service so it does not attempt a live SignalR connection.

## Local scanning

Requires .NET 10, Node 24, Docker Desktop for frontend scanning and the test
database, and the scanner:

```powershell
dotnet tool install --global dotnet-sonarscanner --version 12.0.0
```

For a disposable local test database, first ensure port 55432 is free:

```powershell
docker run --detach --rm --name ragdemo-sonar-tests `
  --publish 127.0.0.1:55432:5432 `
  --env POSTGRES_USER=postgres --env POSTGRES_DB=ragchatdemo_test `
  --env POSTGRES_HOST_AUTH_METHOD=trust pgvector/pgvector:pg16
docker exec ragdemo-sonar-tests pg_isready -U postgres -d ragchatdemo_test
```

Wait for `accepting connections` before running tests. Never point test fixtures
at a production or shared application database: they migrate and write data.

```powershell
$env:RAGCHATDEMO_TEST_CONNECTION_STRING = 'Host=localhost;Port=55432;Database=ragchatdemo_test;Username=postgres'
$env:SONAR_TOKEN = '<token from your secure store>'
.\scripts\SonarQube-Scan.ps1 -ScanBackend -ScanFrontend
docker stop ragdemo-sonar-tests
```

Always stop the disposable container after testing, including on failure.
You can select either scan switch alone. The default host is the public HTTPS
URL; override `SONAR_HOST_URL` if needed. Local scanning permits develop only,
unless `-AllowNonDevelopBranch` (legacy alias `-AllowNonMainBranch`) explicitly
authorizes overwriting the shared baseline. Backend reports use a unique
per-run directory to avoid stale imports. Both build/test failures and failed
Quality Gates surface as errors.
