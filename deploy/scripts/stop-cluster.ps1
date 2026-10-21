#!/usr/bin/env pwsh
# Gracefully stops all RagChatDemo pods by scaling deployments to zero, clients first and
# Postgres last. Nothing is deleted: the PVC, secrets and Helm releases are kept, so
# start-cluster.ps1 restores the system with its data.
# KEEP IN SYNC: any service added to deploy/helm/ must be added to the tiers below.

param(
    [string]$Namespace = 'rag-demo',
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

# Reverse of start order; Postgres goes last so in-flight work can finish writing.
$tiers = @(
    @('frontend'),
    @('chat-api'),
    @('ingestion-worker', 'mcp-server'),
    @('reranker', 'confluence-stub'),
    @('postgres')
)

foreach ($tier in $tiers) {
    Write-Host "Stopping: $($tier -join ', ')"
    foreach ($name in $tier) {
        kubectl scale deployment $name -n $Namespace --replicas=0
        if ($LASTEXITCODE -ne 0) { throw "Failed to scale $name" }
    }
    foreach ($name in $tier) {
        kubectl wait pod -n $Namespace -l "app=$name" --for=delete --timeout="${TimeoutSeconds}s" 2>$null | Out-Null
    }
}

Write-Host 'All services stopped. Data is preserved; run deploy/scripts/start-cluster.ps1 to bring everything back.'
kubectl get pods -n $Namespace
