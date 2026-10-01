#!/usr/bin/env pwsh
# Deploys RagChatDemo to the local Docker Desktop Kubernetes cluster.
# Prerequisites: images already built locally (see deploy/scripts/build-images.ps1 or build
# manually with `docker build`), kubectl context set to docker-desktop, helm installed.

param(
    [string]$Namespace = 'rag-demo',
    [string]$ConfluenceApiToken = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$helmDir = Join-Path $repoRoot 'deploy\helm'

Write-Host "Ensuring namespace '$Namespace' exists..."
kubectl create namespace $Namespace --dry-run=client -o yaml | kubectl apply -f -

# --- Secret (postgres password, confluence token) -----------------------------------------
$existingPassword = kubectl get secret rag-chat-demo-secrets -n $Namespace -o jsonpath='{.data.postgres-password}' 2>$null
if ($existingPassword) {
    Write-Host 'Reusing existing Postgres password from rag-chat-demo-secrets.'
    $postgresPassword = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($existingPassword))
} else {
    Write-Host 'Generating a new random Postgres password.'
    $postgresPassword = [System.Convert]::ToBase64String((1..24 | ForEach-Object { Get-Random -Maximum 256 })) -replace '[/+=]', ''
}

kubectl create secret generic rag-chat-demo-secrets `
    --namespace $Namespace `
    --from-literal=postgres-password=$postgresPassword `
    --from-literal=confluence-api-token=$ConfluenceApiToken `
    --dry-run=client -o yaml | kubectl apply -f -

# --- ConfigMap (non-secret shared config) --------------------------------------------------
kubectl create configmap rag-chat-demo-config `
    --namespace $Namespace `
    --from-literal=Ollama__BaseUrl='http://host.docker.internal:11434' `
    --from-literal=Ollama__EmbedModel='nomic-embed-text' `
    --from-literal=Ollama__ChatModel='qwen3.6:27b' `
    --from-literal=Mcp__ServerUrl='http://mcp-server:8080' `
    --from-literal=ChatApi__BaseUrl='http://chat-api:8080' `
    --from-literal=Confluence__UseStub='true' `
    --from-literal=Confluence__BaseUrl='http://confluence-stub:8080' `
    --dry-run=client -o yaml | kubectl apply -f -

# --- Helm releases, in dependency order ----------------------------------------------------
$charts = @('postgres', 'confluence-stub', 'mcp-server', 'ingestion-worker', 'chat-api', 'frontend')
foreach ($chart in $charts) {
    Write-Host "`nDeploying $chart..."
    helm upgrade --install $chart (Join-Path $helmDir $chart) --namespace $Namespace
}

Write-Host "`nDeployment complete. Pod status:"
kubectl get pods -n $Namespace
