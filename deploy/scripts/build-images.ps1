#!/usr/bin/env pwsh
# Builds all RagChatDemo container images locally (tagged :local), ready for deploy-all.ps1.
# Docker Desktop's Kubernetes shares the same image store, so no registry push is needed.

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $repoRoot
try {
    docker build -f src/RagChatDemo.ChatApi/Dockerfile -t ragchatdemo/chat-api:local .
    docker build -f src/RagChatDemo.ConfluenceStub/Dockerfile -t ragchatdemo/confluence-stub:local .
    docker build -f src/RagChatDemo.IngestionWorker/Dockerfile -t ragchatdemo/ingestion-worker:local .
    docker build -f src/RagChatDemo.McpServer/Dockerfile -t ragchatdemo/mcp-server:local .

    Push-Location (Join-Path $repoRoot 'frontend/rag-chat-demo')
    try {
        docker build -t ragchatdemo/frontend:local .
    } finally {
        Pop-Location
    }
} finally {
    Pop-Location
}

Write-Host "`nAll images built:"
docker images 'ragchatdemo/*'
