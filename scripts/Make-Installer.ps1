<#
.SYNOPSIS
    Builds the Velopack installer + update feed for TalkPrompter.

    Output (dist/updates/):
      TalkPrompter-win-Setup.exe     the installer users run once
      *.nupkg + RELEASES/assets      the update feed consumed by auto-update

    Every new version published into the same folder becomes an auto-update
    for installed apps whose UpdateFeedUrl points at it. Releases ship to the
    public feed repo (github.com/Sven-Bo/talkprompter) via Publish-Release.ps1.

.NOTES
    Requires the vpk tool: dotnet tool install -g vpk
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$props = [xml](Get-Content "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
if (-not $version) { throw "No version in Directory.Build.props" }

Write-Host "Publishing v$version..." -ForegroundColor Cyan
dotnet publish src/Teleprompter.App/Teleprompter.App.csproj -c $Configuration -r win-x64 --self-contained false -o publish | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# The installer ships WITHOUT models: the app offers the voice pack download
# on first start, stores it per-user, and every update stays small.
if (Test-Path "publish\models") { Remove-Item "publish\models" -Recurse -Force }

Write-Host "Packing installer + update feed..." -ForegroundColor Cyan
$notesArgs = @()
if (Test-Path "docs\RELEASE_NOTES.md") {
    # Embedded into the feed metadata; the app shows these in its update prompt.
    $notesArgs = @("--releaseNotes", "docs\RELEASE_NOTES.md")
}
vpk pack `
    --packId TalkPrompter `
    --packVersion $version `
    --packDir publish `
    --mainExe TalkPrompter.exe `
    --packTitle "TalkPrompter" `
    --packAuthors "Bosau Digital LLC" `
    --icon src/Teleprompter.App/Assets/app.ico `
    --outputDir dist/updates `
    @notesArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "Done. Installer: dist\updates\TalkPrompter-win-Setup.exe" -ForegroundColor Green
Get-ChildItem dist\updates | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
