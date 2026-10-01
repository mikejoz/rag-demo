#!/usr/bin/env pwsh
# One-time local setup: ensure the embedding model is pulled into the existing Ollama instance.
# Chat models (qwen3.6:27b, llama3.1:8b) are assumed already present.

$ErrorActionPreference = 'Stop'

Write-Host 'Checking Ollama models on http://localhost:11434 ...'
$tags = Invoke-RestMethod -Uri 'http://localhost:11434/api/tags' -Method Get
$models = $tags.models | ForEach-Object { $_.name }

if ($models -contains 'nomic-embed-text:latest' -or $models -contains 'nomic-embed-text') {
    Write-Host 'nomic-embed-text already installed.'
} else {
    Write-Host 'Pulling nomic-embed-text ...'
    ollama pull nomic-embed-text
}

foreach ($required in @('qwen3.6:27b', 'llama3.1:8b')) {
    if ($models -notcontains $required) {
        Write-Warning "Expected chat model '$required' not found in Ollama. Pull it manually: ollama pull $required"
    }
}

Write-Host 'Ollama model check complete.'
