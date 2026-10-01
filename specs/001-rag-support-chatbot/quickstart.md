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
./deploy-all.ps1   # helm upgrade --install for postgres, confluence-stub, mcp-server,
                    # ingestion-worker, chat-api, frontend, in that order, namespace rag-demo
kubectl get pods -n rag-demo
```

Access the frontend via `kubectl port-forward` or the chart's configured local-only ingress
host, then repeat the ingestion-trigger + chat verification above against the cluster.

## Verification checklist (maps to spec Success Criteria)

- [ ] SC-001: Ask a question, confirm streamed text starts within ~5s.
- [ ] SC-002: Narrate the activity log out loud for one question, without opening logs.
- [ ] SC-003: Edit a stub article, re-run ingestion, confirm the next answer reflects the edit.
- [ ] SC-004: `kubectl get pods -n rag-demo` all Running, no external paid API calls made.
- [ ] SC-005: Run `axe` against the Angular app's chat screen — zero critical/serious violations.
