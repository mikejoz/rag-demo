# SonarQube Community Build

RagChatDemo uses separate SonarQube projects for its .NET backend and Angular
frontend:

| Project | Key |
| --- | --- |
| .NET backend | `mikejoz_ragchatdemo` |
| Angular frontend | `mikejoz_ragchatdemo_frontend` |

Import both repositories/projects through the SonarQube GitHub integration, or
create projects manually with these exact keys. Generate an analysis token from
**My Account → Security → Tokens** and store it securely.

## GitHub Actions

The backend and frontend workflows run on `main` pushes and manual dispatch
only. Community Build does not support pull request or multi-branch analysis.
Configure each repository with:

- Actions variable `SONAR_HOST_URL=https://sonar.michaeljozwik.me`
- Actions secret `SONAR_TOKEN` containing an analysis token

Register a Windows x64 self-hosted runner separately for each personal
repository, with labels `self-hosted`, `Windows`, `X64`, and `sonarqube`. It
needs .NET 10, Node.js 20, npm, and Docker Desktop.

## Local Scanning

Install SonarScanner for .NET with
`dotnet tool install --global dotnet-sonarscanner`. Set the token in the
current PowerShell session and run the desired scans:

```powershell
$env:SONAR_TOKEN = '<token from your secure store>'
.\scripts\SonarQube-Scan.ps1 -ScanBackend -ScanFrontend
```

The public hostname is the default; optionally override `SONAR_HOST_URL`.
Community Build has no branch analysis. The script refuses non-main uploads
unless `-AllowNonMainBranch` is specified; using that switch overwrites the
main-project analysis.

The backend uses `RagChatDemo.slnx`; the frontend uses the official
SonarScanner CLI Docker image and its adjacent `sonar-project.properties`.
Scanner output folders `.scannerwork/` and `.sonarqube/` are gitignored.
