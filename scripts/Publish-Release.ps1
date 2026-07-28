<#
.SYNOPSIS
    The one-command release: build the installer, upload to GitHub Releases,
    and make sure the release is really published (not a draft) with the
    release notes as its body.

    The SOURCE lives in the private repo; releases go to the PUBLIC feed repo
    (github.com/Sven-Bo/talkprompter) that installed apps poll for updates.

    Steps:
      1. scripts/Make-Installer.ps1  (publish + vpk pack with release notes)
      2. vpk upload github           (uploads assets, creates the release)
      3. gh release edit             (forces draft=false + sets the body)

.EXAMPLE
    ./scripts/Publish-Release.ps1
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$feedRepo = "Sven-Bo/talkprompter"
$repoUrl = "https://github.com/$feedRepo"
$props = [xml](Get-Content "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
$tag = "v$version"

& powershell -ExecutionPolicy Bypass -File "scripts\Make-Installer.ps1"
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

$token = gh auth token
vpk upload github --repoUrl $repoUrl --token $token --publish `
    --releaseName "TalkPrompter $tag" --tag $tag --outputDir dist/updates
if ($LASTEXITCODE -ne 0) { throw "vpk upload failed" }

# vpk sometimes leaves the release as a draft; force-publish and set the notes.
if (Test-Path "docs\RELEASE_NOTES.md") {
    gh release edit $tag --repo $feedRepo --draft=false --notes-file docs\RELEASE_NOTES.md | Out-Null
}
else {
    gh release edit $tag --repo $feedRepo --draft=false | Out-Null
}

$state = gh release view $tag --repo $feedRepo --json isDraft | ConvertFrom-Json
if ($state.isDraft) { throw "release $tag is still a draft!" }

Write-Host "Released $tag (published, with notes) to $feedRepo." -ForegroundColor Green
