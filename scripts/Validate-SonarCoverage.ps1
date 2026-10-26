[CmdletBinding()]
param(
    [string]$BackendResultsDirectory,
    [switch]$Frontend
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

if (-not $BackendResultsDirectory -and -not $Frontend) {
    throw 'Specify -BackendResultsDirectory or -Frontend.'
}

if ($BackendResultsDirectory) {
    $reports = @(Get-ChildItem (Join-Path $BackendResultsDirectory '*\coverage.opencover.xml') -File)
    if ($reports.Count -ne 4) {
        throw "Expected four test-project coverage reports; found $($reports.Count)."
    }
    $sourceRoot = (Join-Path $repoRoot 'src') + [IO.Path]::DirectorySeparatorChar
    foreach ($report in $reports) {
        [xml]$xml = Get-Content $report.FullName -Raw
        if ($xml.DocumentElement.Name -ne 'CoverageSession' -or $xml.SelectNodes('//SequencePoint').Count -eq 0) {
            throw "Invalid OpenCover structure: $($report.FullName)"
        }
        $sources = @($xml.SelectNodes('//File') | Where-Object {
            $_.fullPath.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $_.fullPath -PathType Leaf)
        })
        if ($sources.Count -eq 0) {
            throw "No resolved production source mappings: $($report.FullName)"
        }
        Write-Host "Validated $($report.FullName): $($sources.Count) production source mappings."
    }
}

if ($Frontend) {
    $frontendRoot = Join-Path $repoRoot 'frontend\rag-chat-demo'
    $report = Join-Path $frontendRoot 'coverage\rag-chat-demo\lcov.info'
    $sourceCount = 0
    $lineCount = 0
    foreach ($line in Get-Content -LiteralPath $report) {
        if ($line.StartsWith('SF:')) {
            $source = $line.Substring(3)
            if (-not (Test-Path -LiteralPath (Join-Path $frontendRoot $source) -PathType Leaf)) {
                throw "Unresolved LCOV source mapping: $source"
            }
            $sourceCount++
        } elseif ($line.StartsWith('DA:')) {
            if ($line -notmatch '^DA:[1-9]\d*,\d+(?:,.*)?$') {
                throw "Invalid LCOV line data: $line"
            }
            $lineCount++
        }
    }
    if ($sourceCount -eq 0 -or $lineCount -eq 0) {
        throw 'LCOV must contain resolved source mappings and line coverage data.'
    }
    Write-Host "Validated LCOV: $sourceCount source mappings, $lineCount line entries."
}
