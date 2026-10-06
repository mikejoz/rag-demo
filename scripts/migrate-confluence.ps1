#!/usr/bin/env pwsh
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$apiTokenLine = dotnet user-secrets --project src/RagChatDemo.IngestionWorker list | Select-String 'Confluence:ApiToken'
$apiToken = $apiTokenLine.ToString().Split('=', 2)[1].Trim()
$baseUrl = "https://michaeljozwik.atlassian.net"
$email = "michael.jozwik@gmail.com"
$spaceKey = "SD"

$credBytes = [System.Text.Encoding]::ASCII.GetBytes("$email`:$apiToken")
$basicAuth = "Basic " + [Convert]::ToBase64String($credBytes)
$headers = @{ "Authorization" = $basicAuth; "Content-Type" = "application/json" }

Write-Host "[1/3] Verifying API access to space '$spaceKey'..." -ForegroundColor Cyan
try {
    Invoke-RestMethod -Uri "$baseUrl/wiki/rest/api/content?spaceKey=$spaceKey&limit=0" `
        -Headers @{ "Authorization" = $basicAuth } -Method Get | Out-Null
    Write-Host "   OK" -ForegroundColor Green
} catch {
    Write-Host "   FAILED: $_" -ForegroundColor Red; exit 1
}

function New-ConfluencePage {
    param([string]$Title, [string]$BodyHtml)
    
    $escaped = [Uri]::EscapeDataString($Title)
    $checkUrl = "$baseUrl/wiki/rest/api/content?spaceKey=$spaceKey&title=$escaped&type=page&limit=1"
    try {
        $check = Invoke-RestMethod -Uri $checkUrl -Headers @{ "Authorization" = $basicAuth } -Method Get
        if ($check.results.Count -gt 0) { return "SKIPPED ($($check.results[0].id))" }
    } catch {}
    
    $jTitle = $Title -replace '\\', '\\\\' -replace '"', '\"'
    $jBody  = $BodyHtml -replace '\\', '\\\\' -replace '"', '\"' -replace "`r", '' -replace "`n", ' '
    
    $json = @"
{"type":"page","title":"$jTitle","space":{"key":"$spaceKey"},"body":{"storage":{"value":"$jBody","representation":"storage"}}}
"@
    
    try {
        $resp = Invoke-RestMethod -Uri "$baseUrl/wiki/rest/api/content" `
            -Headers $headers -Method Post -Body ([System.Text.Encoding]::UTF8.GetBytes($json))
        return "CREATED ($($resp.id))"
    } catch {
        try {
            $rs = $_.Exception.Response.GetResponseStream()
            $rdr = [System.IO.StreamReader]::new($rs)
            $errTxt = $rdr.ReadToEnd()
            $errObj = $errTxt | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errObj.message) { return "FAILED: $($errObj.message)" }
        } catch {}
        return "FAILED: $_"
    }
}

$articles = @(
    @{ T="VPN Password Reset"; B='<p>If you have forgotten your VPN password or it has expired, you can reset it yourself without contacting the help desk.</p><p>Open the self-service portal at <code>portal.example.com</code> and sign in with your employee ID. Click <strong>Reset Password</strong> under the VPN section, then follow the emailed link to choose a new password. The new password must be at least 12 characters and include a number and a symbol.</p><p>After resetting, disconnect and reconnect your VPN client (Cisco AnyConnect) for the change to take effect. If you are still locked out after five minutes, contact IT support.</p>' },
    @{ T="Connecting to the Office Wi-Fi"; B='<p>The corporate Wi-Fi network is named <strong>CorpNet-5G</strong>. Use your network username and password (the same credentials you use to log into your laptop) to connect.</p><p>Guests should use the <strong>CorpNet-Guest</strong> network, which requires a daily access code available from the front desk.</p><p>If your laptop will not connect, forget the network in your Wi-Fi settings and reconnect from scratch. Persistent issues should be reported to IT support with your laptops asset tag.</p>' },
    @{ T="Setting Up Multi-Factor Authentication (MFA)"; B='<p>All employees are required to enable multi-factor authentication (MFA) on their corporate account within their first week.</p><p>Install the Microsoft Authenticator app on your phone, then go to <code>account.example.com/security</code> and choose <strong>Add sign-in method</strong>. Scan the QR code shown on screen with the app to link your device.</p><p>Keep your recovery codes somewhere safe - you will need them if you lose your phone. Contact IT support to regenerate MFA if you are ever locked out.</p>' },
    @{ T="Requesting New IT Equipment"; B='<p>To request a new laptop, monitor, docking station, or other hardware, submit a request through the IT Equipment Request form on the intranet.</p><p>Standard requests (monitors, keyboards, mice, headsets) are usually fulfilled within 3 business days. Laptop requests require manager approval and typically take 1-2 weeks depending on stock.</p><p>Loaner equipment is available from the IT desk on the 4th floor for urgent needs while you wait for your request to be fulfilled.</p>' },
    @{ T="Printer Setup and Troubleshooting"; B='<p>Office printers are named by floor, e.g. <code>PRINT-FL3-COLOR</code> for the 3rd floor color printer. Add a printer via Settings &gt; Printers &amp; Scanners &gt; Add Printer and search for the printer name.</p><p>If a print job is stuck in the queue, open the queue, cancel the job, and try again. A paper jam usually clears the queue automatically once resolved.</p><p>For persistent connectivity issues, confirm you are connected to CorpNet-5G (printers are not reachable from the guest network).</p>' },
    @{ T="Requesting Software Licenses"; B='<p>Most productivity software (Microsoft 365, Slack, Figma) is pre-installed and licensed automatically when your account is created.</p><p>For specialized software (e.g. Adobe Creative Cloud, JetBrains IDEs, Visio), submit a Software License Request with your managers approval. Licenses are provisioned within 2 business days of approval.</p><p>Do not install unlicensed or pirated software on company devices; this is a policy violation and a security risk.</p>' },
    @{ T="Email Not Syncing on Mobile"; B='<p>If your corporate email has stopped syncing on your phone, the most common fix is to remove and re-add the account.</p><p>On iOS: Settings &gt; Mail &gt; Accounts &gt; select your work account &gt; Delete Account, then re-add it using the Outlook app and your MFA-enabled login.</p><p>On Android, do the same from the Outlook apps account settings. If the problem persists, confirm your phones date and time are set automatically - an incorrect clock will break the secure connection.</p>' },
    @{ T="Requesting Access to a Shared Drive"; B='<p>Shared drives and SharePoint sites are owned by individual teams. To request access, find the drive owner (listed on the intranets Shared Drives directory) and submit an access request through the Access Request form, naming the specific folder you need.</p><p>Access requests require approval from the drive owner or your manager and are typically granted within 1 business day.</p><p>Access is automatically revoked when you change teams or leave the company.</p>' },
    @{ T="Troubleshooting a Slow Laptop"; B='<p>If your laptop has become slow, start with these steps before contacting IT support:</p><ol><li>Restart the laptop - this clears memory leaks from long-running applications.</li><li>Check available disk space; less than 10 percent free space can severely slow Windows down.</li><li>Open Task Manager and look for a single process consuming excessive CPU or memory.</li><li>Make sure Windows Update is not running a large update in the background.</li></ol><p>If performance does not improve after these steps, submit a support ticket with your asset tag and a description of when the slowness started.</p>' },
    @{ T="New Employee Onboarding Checklist"; B='<p>Welcome to the company! Here is what to expect on your first day:</p><p>Your manager will meet you at reception and walk you to IT to collect your laptop and badge. IT will help you set up your account, MFA, and VPN on the spot.</p><p>You will receive an onboarding email with links to required training modules, which must be completed within your first two weeks. Your badge grants building access and is also your ID for the cafeteria.</p>' },
    @{ T="Reporting a Phishing Email"; B='<p>If you receive a suspicious email asking for credentials, payment, or urgent action, do not click any links or attachments.</p><p>Use the <strong>Report Phishing</strong> button in Outlook (top ribbon) to forward the email directly to the Security team and remove it from your inbox in one click.</p><p>If you have already clicked a link or entered your password, change your password immediately via <code>account.example.com/security</code> and contact Security.</p>' },
    @{ T="Setting Up Microsoft Teams for Video Calls"; B='<p>Microsoft Teams is the standard tool for video calls, chat, and screen sharing.</p><p>Download it from the Company Portal app if it is not already installed, then sign in with your corporate email and MFA. Test your camera and microphone under Settings &gt; Devices before your first meeting.</p><p>Conference rooms have a dedicated Teams Room device - just tap Join on the rooms touchscreen when a meeting is about to start.</p>' },
    @{ T="Company Password Policy"; B='<p>All corporate passwords must be at least 12 characters long and include a mix of uppercase, lowercase, numbers, and symbols.</p><p>Passwords expire every 90 days. You will receive a reminder email one week before expiration. Reused passwords from your last 5 are not allowed.</p><p>Never share your password with anyone, including IT staff - IT will never ask for your password over email or phone.</p>' },
    @{ T="Backing Up Your Files"; B='<p>Company laptops automatically back up the Documents, Desktop, and Pictures folders to OneDrive via Known Folder Move. Check sync status via the OneDrive cloud icon in your system tray.</p><p>Files stored outside these folders (e.g. directly on drive <code>D:</code>) are NOT backed up automatically - move important files into your OneDrive folders.</p><p>To restore a previous version of a file, right-click it in File Explorer and choose Version history.</p>' },
    @{ T="Requesting a Distribution List or Shared Mailbox"; B='<p>Team distribution lists (e.g. <code>team-support@example.com</code>) and shared mailboxes can be requested via the IT Service Catalog.</p><p>Provide the desired name, purpose, and an initial list of members/owners. Requests are typically fulfilled within 2 business days. The requesting manager becomes the default owner and can add or remove members afterward via the Outlook admin portal link included in the fulfillment email.</p>' },
    @{ T="How to Submit an IT Support Ticket"; B='<p>For any issue not covered by self-service articles, submit a ticket at <code>support.example.com</code> or email <code>helpdesk@example.com</code>.</p><p>Include your asset tag, a clear description of the issue, and screenshots if applicable - this helps IT resolve your ticket faster. Standard tickets are triaged within 4 business hours; urgent issues (e.g. complete loss of access) should be marked Urgent and will page the on-call technician.</p>' }
)

Write-Host "[2/3] Migrating $($articles.Count) pages..." -ForegroundColor Cyan
$ok = 0; $skip = 0; $fail = 0

foreach ($a in $articles) {
    Write-Host "   $($a.T)..." -NoNewline
    $result = New-ConfluencePage -Title $a.T -BodyHtml $a.B
    
    if ($result -match "^CREATED") {
        Write-Host " $result" -ForegroundColor Green; $ok++
    } elseif ($result -match "^SKIPPED") {
        Write-Host " $result" -ForegroundColor Yellow; $skip++
    } else {
        Write-Host " $result" -ForegroundColor Red; $fail++
    }
    Start-Sleep -Milliseconds 250
}

Write-Host ""
Write-Host "[3/3] Migration complete! Created: $ok | Skipped: $skip | Failed: $fail" -ForegroundColor Cyan

if ($ok + $skip -eq $articles.Count) {
    Write-Host "   All pages accounted for!" -ForegroundColor Green
} else {
    exit 1
}
