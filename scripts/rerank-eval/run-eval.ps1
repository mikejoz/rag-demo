#!/usr/bin/env pwsh
# Compares search_knowledge_base citation hit-rate against the curated question set (SC-001).
# Run once with the mcp-server deployment's Reranker__Enabled=true and once with false,
# giving a different -Label each time (e.g. "reranked" / "baseline") so results don't overwrite.
#
# The MCP server itself is not forwarded by deploy/scripts/port-forward.ps1; forward it first:
#   kubectl port-forward -n rag-demo svc/mcp-server 8083:8080
# then it is reachable (MapMcp() with no path prefix -> endpoint at "/") at http://localhost:8083/

param(
    [string]$McpUrl = 'http://localhost:8083/',
    [int]$TopK = 5,
    [string]$Label = 'run'
)

$ErrorActionPreference = 'Stop'
$questionsPath = Join-Path $PSScriptRoot 'questions.json'
$questions = Get-Content $questionsPath -Raw | ConvertFrom-Json

function ConvertFrom-McpResponse {
    param($WebResponse)
    $contentType = $WebResponse.Headers['Content-Type']
    $body = $WebResponse.Content
    if ($contentType -match 'text/event-stream') {
        $dataLines = ($body -split "`n") | Where-Object { $_ -like 'data:*' } | ForEach-Object { $_.Substring(5).Trim() }
        $body = $dataLines -join ''
    }
    if ([string]::IsNullOrWhiteSpace($body)) { return $null }
    return $body | ConvertFrom-Json
}

function Invoke-McpRpc {
    param([string]$Url, [hashtable]$Body, [string]$SessionId)
    $headers = @{ Accept = 'application/json, text/event-stream' }
    if ($SessionId) { $headers['Mcp-Session-Id'] = $SessionId }
    $json = $Body | ConvertTo-Json -Depth 10 -Compress
    $response = Invoke-WebRequest -Uri $Url -Method Post -Body $json -ContentType 'application/json' -Headers $headers -SkipHttpErrorCheck
    $parsed = ConvertFrom-McpResponse -WebResponse $response
    return [pscustomobject]@{ Parsed = $parsed; SessionId = $response.Headers['Mcp-Session-Id'] }
}

Write-Host "Initializing MCP session at $McpUrl ..."
$initBody = @{
    jsonrpc = '2.0'; id = 1; method = 'initialize'
    params  = @{
        protocolVersion = '2025-06-18'
        capabilities    = @{}
        clientInfo      = @{ name = 'rerank-eval'; version = '1.0.0' }
    }
}
$initResult = Invoke-McpRpc -Url $McpUrl -Body $initBody
$sessionId = $initResult.SessionId
if (-not $initResult.Parsed) { throw "No initialize response; is $McpUrl reachable?" }

Invoke-McpRpc -Url $McpUrl -Body @{ jsonrpc = '2.0'; method = 'notifications/initialized' } -SessionId $sessionId | Out-Null

$rows = @()
$id = 2
foreach ($q in $questions) {
    $callBody = @{
        jsonrpc = '2.0'; id = $id; method = 'tools/call'
        params  = @{ name = 'search_knowledge_base'; arguments = @{ query = $q.question; topK = $TopK } }
    }
    $id++

    $callResult = (Invoke-McpRpc -Url $McpUrl -Body $callBody -SessionId $sessionId).Parsed
    $toolResult = $callResult.result.structuredContent
    if (-not $toolResult) { $toolResult = $callResult.result.content[0].text | ConvertFrom-Json }

    $titles = @($toolResult.results | ForEach-Object { $_.documentTitle })
    $rank = ($titles.IndexOf($q.expectedTitle)) + 1
    $hit = $rank -gt 0

    $rows += [pscustomobject]@{
        Question      = $q.question
        ExpectedTitle = $q.expectedTitle
        Hit           = $hit
        Rank          = if ($hit) { $rank } else { $null }
        TopTitle      = $titles | Select-Object -First 1
        Rerank        = $toolResult.rerank
    }
}

$rows | Format-Table Question, ExpectedTitle, Hit, Rank, TopTitle -AutoSize -Wrap

$hits = $rows | Where-Object Hit
$hitRate = [math]::Round(100.0 * $hits.Count / $rows.Count, 1)
$meanRank = if ($hits.Count -gt 0) { [math]::Round(($hits.Rank | Measure-Object -Average).Average, 2) } else { $null }

Write-Host ""
Write-Host "Label: $Label | Questions: $($rows.Count) | Hit rate: $hitRate% | Mean rank (hits only): $meanRank"

$outputPath = Join-Path $PSScriptRoot "last-run-$Label.json"
[pscustomobject]@{
    label    = $Label
    mcpUrl   = $McpUrl
    topK     = $TopK
    hitRate  = $hitRate
    meanRank = $meanRank
    results  = $rows
} | ConvertTo-Json -Depth 10 | Set-Content -Path $outputPath

Write-Host "Results written to $outputPath"
