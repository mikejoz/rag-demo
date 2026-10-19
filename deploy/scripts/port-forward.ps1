#!/usr/bin/env pwsh
# Exposes the in-cluster services on localhost and re-establishes each forward whenever the
# pod is restarted/replaced (plain `kubectl port-forward` dies with its pod). Ctrl+C to stop.
#   http://localhost:8080          UI
#   http://localhost:8081/swagger  Chat API Swagger
#   http://localhost:8082/hangfire Hangfire dashboard
#   http://localhost:8083          MCP server (eval script, MCP clients)
#   http://localhost:8084          Reranker (TEI /rerank, /health)
#   localhost:5432                 Postgres (McpServer integration tests)
# KEEP IN SYNC: any service added to deploy/helm/ must be added to the list below.

param([string]$Namespace = 'rag-demo')

$forwards = @(
    @{ Svc = 'frontend';         Local = 8080; Remote = 80 },
    @{ Svc = 'chat-api';         Local = 8081; Remote = 8080 },
    @{ Svc = 'ingestion-worker'; Local = 8082; Remote = 8080 },
    @{ Svc = 'mcp-server';       Local = 8083; Remote = 8080 },
    @{ Svc = 'reranker';         Local = 8084; Remote = 80 },
    @{ Svc = 'postgres';         Local = 5432; Remote = 5432 }
)

$jobs = foreach ($f in $forwards) {
    Start-Job -ArgumentList $Namespace, $f -ScriptBlock {
        param($ns, $f)
        while ($true) {
            kubectl port-forward -n $ns "svc/$($f.Svc)" "$($f.Local):$($f.Remote)" 2>&1 | Out-Null
            Start-Sleep -Seconds 2
        }
    }
}

Write-Host 'UI: http://localhost:8080 | Swagger: http://localhost:8081/swagger | Hangfire: http://localhost:8082/hangfire | MCP: http://localhost:8083 | Reranker: http://localhost:8084 | Postgres: localhost:5432'
try { Wait-Job $jobs | Out-Null } finally { $jobs | Stop-Job; $jobs | Remove-Job }
