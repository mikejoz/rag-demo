<#
.SYNOPSIS
    Runs local SonarQube Community Build scans for RagChatDemo.
.DESCRIPTION
    Scans the .NET backend with SonarScanner for .NET and the Angular frontend
    with the official SonarScanner CLI Docker image. Community Build does not
    support branch analysis; uploads update each project's main analysis.
#>
[CmdletBinding()]
param(
    [switch]$ScanBackend,
    [switch]$ScanFrontend,
    [switch]$AllowNonMainBranch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ScanBackend -and -not $ScanFrontend) {
    throw 'Specify -ScanBackend, -ScanFrontend, or both.'
}

if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
    throw 'Set SONAR_TOKEN in the current PowerShell session; do not put it in a file.'
}

$env:SONAR_HOST_URL = if ([string]::IsNullOrWhiteSpace($env:SONAR_HOST_URL)) {
    'https://sonar.michaeljozwik.me'
} else {
    $env:SONAR_HOST_URL.TrimEnd('/')
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$branch = git -C $repoRoot branch --show-current
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to determine the current Git branch.'
}

if ($branch -ne 'main' -and -not $AllowNonMainBranch) {
    throw "Community Build has no branch analysis. Current branch is '$branch'; pass -AllowNonMainBranch to explicitly overwrite the main-project analysis."
}

if ($ScanBackend) {
    if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
        throw 'Install SonarScanner for .NET with: dotnet tool install --global dotnet-sonarscanner'
    }

    Push-Location $repoRoot
    try {
        dotnet sonarscanner begin `
            /k:mikejoz_ragchatdemo `
            "/d:sonar.host.url=$env:SONAR_HOST_URL" `
            "/d:sonar.token=$env:SONAR_TOKEN"
        if ($LASTEXITCODE -ne 0) { throw 'SonarScanner begin failed.' }

        dotnet restore RagChatDemo.slnx
        if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }

        dotnet build RagChatDemo.slnx --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

        dotnet test RagChatDemo.slnx --no-build
        $testExit = $LASTEXITCODE

        dotnet sonarscanner end "/d:sonar.token=$env:SONAR_TOKEN"
        if ($LASTEXITCODE -ne 0) { throw 'SonarScanner end failed.' }
        if ($testExit -ne 0) { throw "Tests failed with exit code $testExit." }
    }
    finally {
        Pop-Location
    }
}

if ($ScanFrontend) {
    $frontendPath = Join-Path $repoRoot 'frontend/rag-chat-demo'
    if (-not (Test-Path (Join-Path $frontendPath 'sonar-project.properties'))) {
        throw "Frontend SonarQube configuration not found: $frontendPath"
    }

    docker run --rm `
        --mount "type=bind,source=$frontendPath,target=/usr/src" `
        --workdir /usr/src `
        -e SONAR_HOST_URL `
        -e SONAR_TOKEN `
        sonarsource/sonar-scanner-cli `
        -DprojectBaseDir=/usr/src
    if ($LASTEXITCODE -ne 0) {
        throw "Frontend SonarScanner CLI failed with exit code $LASTEXITCODE."
    }
}
