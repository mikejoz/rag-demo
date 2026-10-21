#!/usr/bin/env pwsh
# Starts all RagChatDemo deployments (scales them back to 1 replica) in dependency order.
# Postgres and its PVC are untouched by stop-cluster.ps1, so data survives a stop/start cycle.
# KEEP IN SYNC: any service added to deploy/helm/ must be added to the tiers below.

param(
    [string]$Namespace = 'rag-demo',
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = 'Stop'

# Each tier starts only after the previous tier is ready.
$tiers = @(
    @('postgres'),
    @('confluence-stub', 'reranker'),
    @('mcp-server', 'ingestion-worker'),
    @('chat-api'),
    @('frontend')
)

foreach ($tier in $tiers) {
    Write-Host "Starting: $($tier -join ', ')"
    foreach ($name in $tier) {
        kubectl scale deployment $name -n $Namespace --replicas=1
        if ($LASTEXITCODE -ne 0) { throw "Failed to scale $name" }
    }
    foreach ($name in $tier) {
        kubectl rollout status deployment/$name -n $Namespace --timeout="${TimeoutSeconds}s"
        if ($LASTEXITCODE -ne 0) { throw "$name did not become ready within ${TimeoutSeconds}s" }
    }
}

Write-Host 'All services are up. Run deploy/scripts/port-forward.ps1 to reach them from localhost.'
kubectl get pods -n $Namespace
