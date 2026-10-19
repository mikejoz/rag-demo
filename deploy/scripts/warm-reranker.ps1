#!/usr/bin/env pwsh
# Waits for the reranker deployment to become Ready, triggering the Text Embeddings Inference
# container to download and cache its model weights into the pod's /data emptyDir on first start.

param(
    [string]$Namespace = 'rag-demo',
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'

Write-Host "Waiting for reranker deployment to become Ready (this downloads model weights on first run)..."
kubectl rollout status deployment/reranker -n $Namespace --timeout="${TimeoutSeconds}s"

Write-Host "`nReranker is Ready: model weights are now cached in the pod's /data volume."
Write-Host 'Subsequent pod restarts will reuse the cache as long as the pod is not rescheduled (emptyDir is node-local).'
