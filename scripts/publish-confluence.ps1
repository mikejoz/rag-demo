<#
.SYNOPSIS
    Publishes a feature specification to Confluence Cloud under the project hierarchy.

.DESCRIPTION
    Reads a Markdown spec file, converts it to Confluence storage format, and publishes
    it under: Software Development -> [Workspace] -> [Project] -> [Feature Name].
    Parent pages are auto-created if they don't exist. Existing feature pages are updated.

.PARAMETER SpecPath
    Path to the spec.md file for the feature.

.PARAMETER WorkspaceName
    Workspace name (e.g., "RagChatDemo"). Becomes a parent page under "Software Development".

.PARAMETER ProjectName
    Optional project/sub-project name. Defaults to WorkspaceName if not provided.
    Creates an intermediate page level (skipped when equal to WorkspaceName).

.PARAMETER FeatureTitle
    Override the feature title. Otherwise derived from spec file frontmatter or filename.

.PARAMETER DryRun
    Show what would be published without making any API calls.

.EXAMPLE
    .\scripts\publish-confluence.ps1 -SpecPath "specs/myfeature/spec.md" -WorkspaceName "RagChatDemo"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SpecPath,

    [Parameter(Mandatory = $true)]
    [string]$WorkspaceName,

    [string]$ProjectName = "",

    [string]$FeatureTitle,

    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# ============================================================
# Constants - Change these to target a different Confluence instance
# ============================================================
$ConfluenceBaseUrl  = "https://michaeljozwik.atlassian.net"
$ConfluenceSpaceKey = "SD"

# ============================================================
# Credential Handling
# ============================================================
function Get-ConfluenceCredentials {
    # Try environment variables first
    if ($env:CONFLUENCE_EMAIL -and $env:CONFLUENCE_API_TOKEN) {
        Write-Host "[Credentials] Using CONFLUENCE_EMAIL / CONFLUENCE_API_TOKEN from environment." -ForegroundColor Green
        return @{ Email = $env:CONFLUENCE_EMAIL; ApiToken = $env:CONFLUENCE_API_TOKEN }
    }

    # Fallback to dotnet user-secrets — search all csprojs for Confluence credentials
    Write-Host "[Credentials] Environment variables not set, trying dotnet user-secrets..." -ForegroundColor Yellow

    $csprojects = Get-ChildItem -Path "src" -Filter "*.csproj" -Recurse -ErrorAction SilentlyContinue
    if (-not $csprojects) {
        Write-Host "[Credentials] FAILED: No .csproj found under src/ for user-secrets fallback." -ForegroundColor Red
        exit 1
    }

    foreach ($csproj in $csprojects) {
        try {
            $secretsOutput = dotnet user-secrets --project $csproj.FullName list 2>&1
            if (-not $secretsOutput) { continue }

            $lines = @($secretsOutput -split "`r?`n")

            # Look for Confluence:Email (case-insensitive)
            $emailLine = $lines | Where-Object { $_ -imatch '^confluence:email\s*=' } | Select-Object -First 1
            if (-not $emailLine) { $emailLine = $lines | Where-Object { $_ -imatch '^Confluence:Email\s*=' } | Select-Object -First 1 }

            # Look for Confluence:ApiToken (case-insensitive)
            $tokenLine = $lines | Where-Object { $_ -imatch '^confluence:apitoken\s*=' } | Select-Object -First 1
            if (-not $tokenLine) { $tokenLine = $lines | Where-Object { $_ -imatch '^Confluence:ApiToken\s*=' } | Select-Object -First 1 }

            $email = if ($emailLine) { [string]$emailLine -replace '^[^=]+\s*=\s*', '' }
            $token = if ($tokenLine) { [string]$tokenLine -replace '^[^=]+\s*=\s*', '' }

            if ($email -and $token) {
                Write-Host "[Credentials] Retrieved from dotnet user-secrets ($($csproj.Name))." -ForegroundColor Green
                return @{ Email = $email.Trim(); ApiToken = $token.Trim() }
            }
        } catch {
            # Skip csprojs that don't have user-secrets configured
            continue
        }
    }

    Write-Host "[Credentials] FAILED: Could not find Confluence credentials in any user-secrets." -ForegroundColor Red
    exit 1
}

# ============================================================
# Validate Spec Path (always required, even for DryRun)
# ============================================================
if (-not (Test-Path $SpecPath)) {
    Write-Host "[Error] Spec file not found: $SpecPath" -ForegroundColor Red
    exit 1
}

$specContent = Get-Content $SpecPath -Raw

# ============================================================
# Authentication and Connectivity (skip in DryRun)
# ============================================================
if (-not $DryRun) {
    $creds = Get-ConfluenceCredentials

    $credEmail  = $creds.Email
    $credToken  = $creds.ApiToken
    $credBytes  = [System.Text.Encoding]::ASCII.GetBytes("$credEmail`:$credToken")
    $basicAuth  = "Basic " + [Convert]::ToBase64String($credBytes)
    $authHeaders = @{ "Authorization" = $basicAuth; "Content-Type" = "application/json" }

    Write-Host "[Connectivity] Verifying Confluence API access..." -ForegroundColor Cyan
    try {
        Invoke-RestMethod -Uri "$ConfluenceBaseUrl/wiki/rest/api/content?spaceKey=$ConfluenceSpaceKey&limit=0" `
            -Headers @{ "Authorization" = $basicAuth } -Method Get | Out-Null
        Write-Host "   Connected OK." -ForegroundColor Green
    } catch {
        Write-Host "   FAILED: Could not connect to Confluence API." -ForegroundColor Red
        Write-Host "   $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[Credentials] DryRun mode — skipping credential lookup and connectivity check." -ForegroundColor Yellow
}

# ============================================================
# Inline Markdown conversion helper (called from Convert-MarkdownToConfluenceStorage)
# ============================================================
function ConvertInlineMarkdown {
    param([string]$Text)

    $result = $Text

    # Inline code (backticks) — do first so bold/italic don't eat backtick content
    $result = $result -replace '`([^`]+)`', '<code>$1</code>'

    # Bold (**text**)
    $result = $result -replace '\*\*([^*]+)\*\*', '<strong>$1</strong>'

    # Italic (*text*)
    $result = $result -replace '\*([^*]+)\*', '<em>$1</em>'

    # Links [text](url)
    $result = $result -replace '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>'

    return $result
}

# ============================================================
# Markdown to Confluence Storage Format
# ============================================================
function Convert-MarkdownToConfluenceStorage {
    param([string]$Markdown)

    $lines = $Markdown -split "`n"
    $output = [System.Collections.Generic.List[string]]::new()
    $inCodeBlock = $false
    $codeContent = ""
    $inUlList = $false
    $inOlList = $false
    $pClosed = $true  # track paragraph state

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Code block toggle
        if ($trimmed -match '^\`\`\`') {
            if (-not $inCodeBlock) {
                # Close any open list/paragraph before code block
                if ($inUlList) { $output.Add("</ul>"); $inUlList = $false }
                if ($inOlList) { $output.Add("</ol>"); $inOlList = $false }
                if (-not $pClosed) { $output.Add("</p>"); $pClosed = $true }
                $inCodeBlock = $true
                $codeContent = ""
            } else {
                # Escape HTML entities inside code for storage format
                $escapedCode = $codeContent -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
                $output.Add("<pre><code>$escapedCode</code></pre>")
                $inCodeBlock = $false
            }
            continue
        }

        if ($inCodeBlock) {
            $codeContent += "$line`n"
            continue
        }

        # Horizontal rule
        if ($trimmed -match '^---+$' -or $trimmed -match '^\*\*\*+$') {
            if ($inUlList) { $output.Add("</ul>"); $inUlList = $false }
            if ($inOlList) { $output.Add("</ol>"); $inOlList = $false }
            if (-not $pClosed) { $output.Add("</p>"); $pClosed = $true }
            $output.Add("<hr/>")
            continue
        }

        # Headings
        if ($trimmed -match '^(#{1,6})\s+(.+)$') {
            if ($inUlList) { $output.Add("</ul>"); $inUlList = $false }
            if ($inOlList) { $output.Add("</ol>"); $inOlList = $false }
            if (-not $pClosed) { $output.Add("</p>"); $pClosed = $true }

            $level = $Matches[1].Length
            $text  = ConvertInlineMarkdown -Text $Matches[2]
            $output.Add("<h$level>$text</h$level>")
            $pClosed = $true
            continue
        }

        # Unordered list items
        if ($trimmed -match '^[\s]*[-*+]\s+(.+)') {
            if ($inOlList) { $output.Add("</ol>"); $inOlList = $false }
            if (-not $inUlList) { $output.Add("<ul>"); $inUlList = $true }
            $text = ConvertInlineMarkdown -Text $Matches[1]
            $output.Add("<li>$text</li>")
            $pClosed = $true
            continue
        }

        # Ordered list items
        if ($trimmed -match '^[\s]*\d+\.\s+(.+)') {
            if ($inUlList) { $output.Add("</ul>"); $inUlList = $false }
            if (-not $inOlList) { $output.Add("<ol>"); $inOlList = $true }
            $text = ConvertInlineMarkdown -Text $Matches[1]
            $output.Add("<li>$text</li>")
            $pClosed = $true
            continue
        }

        # Blank line
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            if ($inUlList) { $output.Add("</ul>"); $inUlList = $false }
            if ($inOlList) { $output.Add("</ol>"); $inOlList = $false }
            if (-not $pClosed) { $output.Add("</p>"); $pClosed = $true }
            continue
        }

        # Regular paragraph text — close any open list first
        if ($inUlList) {
            $output.Add("</ul>")
            $inUlList = $false
        }
        if ($inOlList) {
            $output.Add("</ol>")
            $inOlList = $false
        }
        if ($pClosed) { $output.Add("<p>"); $pClosed = $false }
        $text = ConvertInlineMarkdown -Text $trimmed
        $output.Add("$text")
    }

    # Close any remaining open blocks
    if ($inCodeBlock) {
        $escapedCode = $codeContent -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
        $output.Add("<pre><code>$escapedCode</code></pre>")
    }
    if ($inUlList) { $output.Add("</ul>") }
    if ($inOlList) { $output.Add("</ol>") }
    if (-not $pClosed) { $output.Add("</p>") }

    return ($output -join "`n")
}

# ============================================================
# Frontmatter Parsing
# ============================================================
function Parse-Frontmatter {
    param([string]$Content)

    $result = @{ Title = $null; Date = $null; Body = $Content }

    if ($Content -notmatch '^---\s*\r?\n(.*?)^---\s*\r?\n([\s\S]*)$') {
        return $result
    }

    $yamlBlock = $Matches[1]
    $result.Body = $Matches[2]

    # Extract title
    if ($yamlBlock -match '^title:\s*(.+)$', [System.Text.RegularExpressions.RegexOptions]::Multiline) {
        $result.Title = $Matches[1].Trim().Trim("'`"")
    }

    # Extract date
    if ($yamlBlock -match '^date:\s*(.+)$', [System.Text.RegularExpressions.RegexOptions]::Multiline) {
        $result.Date = $Matches[1].Trim().Trim("'`"")
    }

    return $result
}

# ============================================================
# Spec Processing
# ============================================================
$fm = Parse-Frontmatter -Content $specContent

if (-not $FeatureTitle) {
    if ($fm.Title) {
        $FeatureTitle = $fm.Title
    } else {
        # Derive from filename: "spec.md" -> "Spec", remove "-spec" suffixes
        $fileName = [System.IO.Path]::GetFileNameWithoutExtension($SpecPath)
        $FeatureTitle = $fileName -replace '-[Ss]pec$', '' -replace '[-_]', ' '
    }
}

$specDate = if ($fm.Date) { $fm.Date } else { (Get-Item $SpecPath).LastWriteTime.ToString("yyyy-MM-dd") }
$specBody = $fm.Body

# Normalize ProjectName: default to WorkspaceName when not provided
if (-not $ProjectName) {
    $ProjectName = $WorkspaceName
}

# ============================================================
# Build Confluence Body HTML
# ============================================================
$htmlBody = Convert-MarkdownToConfluenceStorage -Markdown $specBody

# Escape HTML entities in title and metadata values for storage format
$h1Title     = $FeatureTitle   -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
$hWorkspace  = $WorkspaceName  -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'
$hProject    = $ProjectName    -replace '&', '&amp;' -replace '<', '&lt;' -replace '>', '&gt;'

$pageHtml = @"
<h1>$h1Title</h1>
<table>
<tr><th>Workspace</th><td>$hWorkspace</td></tr>
<tr><th>Project</th><td>$hProject</td></tr>
<tr><th>Spec Date</th><td>$specDate</td></tr>
<tr><th>Status</th><td>Published via script</td></tr>
</table>
<br/>
$htmlBody
"@

# ============================================================
# Confluence API Helpers
# ============================================================
function Escape-JsonString {
    param([string]$Value)
    $result = $value -replace '\\', '\\\\' -replace '"', '\"' -replace "`r", '' -replace "`n", ' '
    return $result
}

function Find-PageByTitle {
    param(
        [string]$Title,
        [string]$ParentId = ""
    )

    $encodedTitle = [Uri]::EscapeDataString($Title)

    if ($ParentId) {
        $url = "$ConfluenceBaseUrl/wiki/rest/api/content/$ParentId/child/page?title=$encodedTitle&limit=1"
    } else {
        $url = "$ConfluenceBaseUrl/wiki/rest/api/content?spaceKey=$ConfluenceSpaceKey&title=$encodedTitle&type=page&limit=1"
    }

    try {
        $resp = Invoke-RestMethod -Uri $url -Headers @{ "Authorization" = $basicAuth } -Method Get
        if ($resp.results.Count -gt 0) {
            return $resp.results[0]
        }
    } catch {
        Write-Host "[Warning] Error searching for page '$Title': $_" -ForegroundColor Yellow
    }
    return $null
}

function New-ConfluencePage {
    param(
        [string]$Title,
        [string]$BodyHtml,
        [string]$ParentId = ""
    )

    # Build JSON manually to avoid ConvertTo-Json issues with nested arrays/hashtables in PS 5.1
    $jTitle = Escape-JsonString -Value $Title
    $jBody  = Escape-JsonString -Value $BodyHtml

    if ($ParentId) {
        $jsonStr = @"
{"type":"page","title":"$jTitle","space":{"key":"$ConfluenceSpaceKey"},"ancestors":[{"id":"$ParentId"}],"body":{"storage":{"value":"$jBody","representation":"storage"}}}
"@
    } else {
        $jsonStr = @"
{"type":"page","title":"$jTitle","space":{"key":"$ConfluenceSpaceKey"},"body":{"storage":{"value":"$jBody","representation":"storage"}}}
"@
    }

    try {
        $resp = Invoke-RestMethod -Uri "$ConfluenceBaseUrl/wiki/rest/api/content" `
            -Headers $authHeaders -Method Post `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonStr))
        Write-Host "   CREATED page '$Title' (id: $($resp.id))" -ForegroundColor Green
        return $resp
    } catch {
        # Try to read error details from response
        try {
            if ($_.Exception.Response) {
                $rs = $_.Exception.Response.GetResponseStream()
                $rdr = [System.IO.StreamReader]::new($rs)
                $errTxt = $rdr.ReadToEnd()
                Write-Host "   API Error: $errTxt" -ForegroundColor Red
            }
        } catch {}
        Write-Host "   FAILED to create page '$Title': $_" -ForegroundColor Red
        exit 1
    }
}

function Update-ConfluencePage {
    param(
        [string]$PageId,
        [string]$Title,
        [string]$BodyHtml
    )

    # Get current version
    $resp = Invoke-RestMethod -Uri "$ConfluenceBaseUrl/wiki/rest/api/content/$PageId?expand=version" `
        -Headers @{ "Authorization" = $basicAuth } -Method Get
    $currentVersion = $resp.version.number
    $newVersion = $currentVersion + 1

    # Build JSON manually for reliability in PS 5.1
    $jTitle = Escape-JsonString -Value $Title
    $jBody  = Escape-JsonString -Value $BodyHtml

    $jsonStr = @"
{"id":"$PageId","type":"page","title":"$jTitle","space":{"key":"$ConfluenceSpaceKey"},"body":{"storage":{"value":"$jBody","representation":"storage"}},"version":{"number":$newVersion}}
"@

    try {
        $updateResp = Invoke-RestMethod -Uri "$ConfluenceBaseUrl/wiki/rest/api/content/$PageId" `
            -Headers $authHeaders -Method Put `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($jsonStr))
        Write-Host "   UPDATED page '$Title' (id: $PageId, version: $newVersion)" -ForegroundColor Green
        return $updateResp
    } catch {
        try {
            if ($_.Exception.Response) {
                $rs = $_.Exception.Response.GetResponseStream()
                $rdr = [System.IO.StreamReader]::new($rs)
                $errTxt = $rdr.ReadToEnd()
                Write-Host "   API Error: $errTxt" -ForegroundColor Red
            }
        } catch {}
        Write-Host "   FAILED to update page '$Title': $_" -ForegroundColor Red
        exit 1
    }
}

function FindOrCreate-Page {
    param(
        [string]$Title,
        [string]$ParentId = "",
        [string]$BodyHtml = "<p></p>"
    )

    if ($DryRun) {
        Write-Host "   [DRY-RUN] Checking page '$Title' ..." -ForegroundColor Yellow
        # In dry-run we can't actually check the API, so just report what would happen
        Write-Host "   [DRY-RUN] Would search for/create page '$Title'" -ForegroundColor Yellow
        return @{ id = "dry-run-placeholder"; title = $Title }
    }

    $existing = Find-PageByTitle -Title $Title -ParentId $ParentId
    if ($existing) {
        Write-Host "   Found existing page '$Title' (id: $($existing.id))" -ForegroundColor Green
        return $existing
    }

    return New-ConfluencePage -Title $Title -BodyHtml $BodyHtml -ParentId $ParentId
}

# ============================================================
# Build the Page Hierarchy
# ============================================================
if ($DryRun) {
    Write-Host "`n===== DRY RUN MODE =====" -ForegroundColor Yellow
}

Write-Host "[Hierarchy] Building page hierarchy..." -ForegroundColor Cyan

# Step 1: Software Development root page
Write-Host "  [1/4] Finding/creating 'Software Development' root page..." -ForegroundColor Cyan
$sdevPage = FindOrCreate-Page -Title "Software Development" -ParentId "" `
    -BodyHtml "<p>Root page for software development documentation.</p>"

# Step 2: Workspace page
Write-Host "  [2/4] Finding/creating workspace page '$WorkspaceName'..." -ForegroundColor Cyan
$workspacePage = FindOrCreate-Page -Title $WorkspaceName -ParentId $sdevPage.id `
    -BodyHtml "<p>Documentation for the $WorkspaceName workspace.</p>"

# Step 3: Project page (skip if same as workspace)
if ($ProjectName -and $ProjectName -ne $WorkspaceName) {
    Write-Host "  [3/4] Finding/creating project page '$ProjectName'..." -ForegroundColor Cyan
    $projectPage = FindOrCreate-Page -Title $ProjectName -ParentId $workspacePage.id `
        -BodyHtml "<p>Documentation for the $ProjectName project.</p>"
    $featureParentId = $projectPage.id
} else {
    $featureParentId = $workspacePage.id
    Write-Host "  [3/4] Project equals workspace - skipping intermediate level." -ForegroundColor Yellow
}

# Step 4: Feature page (create or update)
Write-Host "  [4/4] Publishing feature page '$FeatureTitle'..." -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "`n[DRY-RUN COMPLETE]" -ForegroundColor Yellow
    Write-Host "Hierarchy would be:" -ForegroundColor Cyan
    Write-Host "  Software Development" -ForegroundColor White
    Write-Host "  |-- $WorkspaceName" -ForegroundColor White
    if ($ProjectName -ne $WorkspaceName) {
        Write-Host "      |-- $ProjectName" -ForegroundColor White
        Write-Host "          |-- $FeatureTitle" -ForegroundColor Green
    } else {
        Write-Host "      |-- $FeatureTitle" -ForegroundColor Green
    }
    Write-Host "`nSpec source: $SpecPath" -ForegroundColor White
    Write-Host "Generated body length: $($pageHtml.Length) characters" -ForegroundColor White
} else {
    # Check if feature page exists under the parent
    $existingFeature = Find-PageByTitle -Title $FeatureTitle -ParentId $featureParentId

    if ($existingFeature) {
        Update-ConfluencePage -PageId $existingFeature.id -Title $FeatureTitle -BodyHtml $pageHtml
    } else {
        New-ConfluencePage -Title $FeatureTitle -BodyHtml $pageHtml -ParentId $featureParentId
    }

    Write-Host "`n[PUBLISH COMPLETE] Feature '$FeatureTitle' published to Confluence." -ForegroundColor Green
    Write-Host "Spec source: $SpecPath" -ForegroundColor White
}