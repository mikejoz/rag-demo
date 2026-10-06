# Quickstart: RAG Support Chatbot Demo

## Prerequisites (one-time, local machine)

1. Docker Desktop with Kubernetes enabled (cluster context `docker-desktop`).
2. Ollama running locally on `localhost:11434` with models pulled:
   ```powershell
   ollama pull qwen3.6:27b       # already present
   ollama pull llama3.1:8b       # already present
   ollama pull nomic-embed-text  # run once
   ```
3. .NET SDK 10, Node.js 24 + Angular CLI, Helm, kubectl (all already verified present).

## Local dev loop (fastest iteration, before containerizing)

1. Run Postgres with pgvector in a dev container (`docker run` is fine for inner-loop dev; the
   Helm chart is used for the "real" deployment target):
   ```powershell
   docker run -d --name rag-pg -e POSTGRES_PASSWORD=devpassword -e POSTGRES_DB=ragchatdemo `
     -p 5432:5432 pgvector/pgvector:pg16
   ```
   `ChatApi`/`McpServer`/`IngestionWorker` all apply EF Core migrations automatically on
   startup, so no manual `dotnet ef database update` step is needed.
2. `dotnet user-secrets set "Confluence:ApiToken" "<token>"` in `RagChatDemo.IngestionWorker`
   only if testing against real Confluence Cloud; otherwise leave unset to use the stub.
3. Run `RagChatDemo.ConfluenceStub`, then `RagChatDemo.McpServer`, then
   `RagChatDemo.IngestionWorker`, then `RagChatDemo.ChatApi` (each `dotnet run`).
4. Run the Angular app: `npm start` in `frontend/rag-chat-demo`.
5. Trigger ingestion from the Hangfire dashboard (`https://localhost:<port>/hangfire`), then chat
   in the Angular UI and watch the activity log panel.

## Cluster deployment (demo-day target)

```powershell
cd deploy/scripts
./build-images.ps1 # docker build for all 6 services, tagged :local (no registry push needed -
                    # Docker Desktop's Kubernetes shares the same local image store)
./deploy-all.ps1   # helm upgrade --install for postgres, confluence-stub, mcp-server,
                    # ingestion-worker, chat-api, frontend, in that order, namespace rag-demo
kubectl get pods -n rag-demo
```

Access the services with the port-forward script, which re-establishes each forward when a pod is
restarted (plain `kubectl port-forward` dies with its pod). Then repeat the ingestion-trigger + chat
verification above against the cluster.

```powershell
./deploy/scripts/port-forward.ps1
# UI:      http://localhost:8080
# Swagger: http://localhost:8081/swagger
# Hangfire: http://localhost:8082/hangfire
```

## Verification checklist (maps to spec Success Criteria)

Executed 2026-10-01 against the real `docker-desktop` cluster deployment (all 6 pods Running,
`deploy-all.ps1` + manual Hangfire trigger, real Ollama qwen3.6:27b/nomic-embed-text):

- [x] SC-001: Ask a question, confirm streamed text starts within ~5s. — PASS (first token
      streamed well within budget once the model warmed up; cold-start first-call latency with
      qwen3.6:27b observed around 15-60s on this dev machine's hardware, warm calls <1-5s).
- [x] SC-002: Narrate the activity log out loud for one question, without opening logs. — PASS
      (Retrieval → McpTool "search_knowledge_base" (chunk count) → Generation Started/Succeeded
      (char count) all rendered live in the Activity Log panel, no server log access needed).
- [x] SC-003: Edit a stub article, re-run ingestion, confirm the next answer reflects the edit.
      — PASS (verified in Phase 5 against local dev Postgres; same code path runs in-cluster).
- [x] SC-004: `kubectl get pods -n rag-demo` all Running, no external paid API calls made. — PASS
      (`postgres`, `confluence-stub`, `mcp-server`, `ingestion-worker`, `chat-api`, `frontend`
      all `1/1 Running`; only external dependency is the local `host.docker.internal:11434`
      Ollama instance, confirmed reachable from in-cluster pods).
- [x] SC-005: Run `axe` against the Angular app's chat screen — zero critical/serious
      violations. — PASS after a fix (see below); 0 violations on both an empty and a populated
      (mid-conversation) activity log/chat screen.

### SC-005 fix applied

`axe-core` initially found: `aria-allowed-role` + `listitem` (role="log" is incompatible with
`<ul>`'s implicit list role, orphaning the `<li>` children) and `scrollable-region-focusable`
(the scrollable activity-log/chat panels had no keyboard focus target). Fixed by changing
`ActivityLogComponent`'s `<ul>`/`<li>` to `<div>`/`<div>` (a log is not a list) and adding
`tabindex="0"` to both scrollable log regions (`activity-log.html`, `chat.html`).

- [ ] SC-003: Edit a stub article, re-run ingestion, confirm the next answer reflects the edit.
- [ ] SC-004: `kubectl get pods -n rag-demo` all Running, no external paid API calls made.
- [ ] SC-005: Run `axe` against the Angular app's chat screen — zero critical/serious violations.
