[CmdletBinding()]
param(
    [switch]$ScanBackend,
    [switch]$ScanFrontend,
    [Alias('AllowNonMainBranch')]
    [switch]$AllowNonDevelopBranch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ScanBackend -and -not $ScanFrontend) {
    throw 'Specify -ScanBackend, -ScanFrontend, or both.'
}
if ([string]::IsNullOrWhiteSpace($env:SONAR_TOKEN)) {
    throw 'Set SONAR_TOKEN in this session; do not store it in a file.'
}
$env:SONAR_HOST_URL = if ([string]::IsNullOrWhiteSpace($env:SONAR_HOST_URL)) {
    'https://sonar.michaeljozwik.me'
} else {
    $env:SONAR_HOST_URL.TrimEnd('/')
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$branch = git -C $repoRoot branch --show-current
if ($LASTEXITCODE -ne 0) { throw 'Unable to determine the current Git branch.' }
if ($branch -ne 'develop' -and -not $AllowNonDevelopBranch) {
    throw "Current branch is '$branch'. Use develop, or -AllowNonDevelopBranch to explicitly overwrite the shared analysis."
}

if ($ScanBackend) {
    if ([string]::IsNullOrWhiteSpace($env:RAGCHATDEMO_TEST_CONNECTION_STRING)) {
        throw 'Set RAGCHATDEMO_TEST_CONNECTION_STRING to an isolated PostgreSQL+pgvector database.'
    }
    if (-not (Get-Command dotnet-sonarscanner -ErrorAction SilentlyContinue)) {
        throw 'Install the scanner: dotnet tool install --global dotnet-sonarscanner --version 12.0.0'
    }
    $runId = [Guid]::NewGuid().ToString('N')
    $results = Join-Path $repoRoot "TestResults\$runId"
    $coverageGlob = "TestResults/$runId/*/coverage.opencover.xml"
    Push-Location $repoRoot
    try {
        dotnet sonarscanner begin /k:mikejoz_ragchatdemo `
            "/d:sonar.host.url=$env:SONAR_HOST_URL" `
            "/d:sonar.token=$env:SONAR_TOKEN" `
            '/d:sonar.scanner.scanAll=false' `
            "/d:sonar.cs.opencover.reportsPaths=$coverageGlob" `
            '/d:sonar.exclusions=**/Migrations/**,**/*.Designer.cs,**/bin/**,**/obj/**,frontend/**' `
            '/d:sonar.qualitygate.wait=true' `
            '/d:sonar.qualitygate.timeout=300'
        if ($LASTEXITCODE -ne 0) { throw 'SonarScanner begin failed.' }
        dotnet restore RagChatDemo.slnx
        if ($LASTEXITCODE -ne 0) { throw 'Solution restore failed.' }
        dotnet build RagChatDemo.slnx -c Release --no-incremental --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
        dotnet test RagChatDemo.slnx --no-build --no-restore -c Release -m:1 --collect 'XPlat Code Coverage' --results-directory $results --logger trx -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
        if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed; analysis was not uploaded.' }
        & (Join-Path $PSScriptRoot 'Validate-SonarCoverage.ps1') -BackendResultsDirectory $results
        dotnet sonarscanner end "/d:sonar.token=$env:SONAR_TOKEN"
        if ($LASTEXITCODE -ne 0) { throw 'Backend scanner upload or Quality Gate failed.' }
    } finally {
        Pop-Location
    }
}

if ($ScanFrontend) {
    $frontendPath = Join-Path $repoRoot 'frontend\rag-chat-demo'
    Push-Location $frontendPath
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
        npm run test:coverage
        if ($LASTEXITCODE -ne 0) { throw 'Frontend coverage tests failed.' }
        npm run build -- --configuration production
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        & (Join-Path $PSScriptRoot 'Validate-SonarCoverage.ps1') -Frontend
        docker run --rm `
            --mount "type=bind,source=$frontendPath,target=/usr/src" `
            --workdir /usr/src `
            -e SONAR_HOST_URL -e SONAR_TOKEN `
            sonarsource/sonar-scanner-cli `
            '-Dsonar.qualitygate.wait=true' `
            '-Dsonar.qualitygate.timeout=300'
        if ($LASTEXITCODE -ne 0) { throw 'Frontend scanner upload or Quality Gate failed.' }
    } finally {
        Pop-Location
    }
}
