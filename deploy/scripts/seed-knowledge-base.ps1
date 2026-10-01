#!/usr/bin/env pwsh
# Convenience script: port-forwards the ingestion-worker's Hangfire dashboard and opens it so an
# operator can click "Trigger now" on the knowledge-base-ingestion recurring job (T035).

param(
    [string]$Namespace = 'rag-demo',
    [int]$LocalPort = 5071
)

$ErrorActionPreference = 'Stop'

Write-Host "Port-forwarding ingestion-worker (localhost:$LocalPort) -> cluster service..."
$portForward = Start-Process kubectl -ArgumentList @(
    'port-forward', "svc/ingestion-worker", "${LocalPort}:8080", '-n', $Namespace
) -PassThru -NoNewWindow

Start-Sleep -Seconds 2
$dashboardUrl = "http://localhost:$LocalPort/hangfire/recurring"
Write-Host "Opening Hangfire dashboard: $dashboardUrl"
Write-Host 'Select the "knowledge-base-ingestion" row and click "Trigger now".'
Start-Process $dashboardUrl

Write-Host "`nPress Enter to stop port-forwarding once you're done."
Read-Host | Out-Null
Stop-Process -Id $portForward.Id -Force -ErrorAction SilentlyContinue
