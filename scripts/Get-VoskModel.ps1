<#
.SYNOPSIS
    Downloads an offline Vosk speech model and installs it into models/ so the
    TalkPrompter switches from simulation mode to real voice tracking.

.PARAMETER Model
    The Vosk model name. Defaults to the small US-English model (~40 MB).
    Larger/other-language options: https://alphacephei.com/vosk/models
      vosk-model-small-en-us-0.15   (English, ~40 MB, fast)
      vosk-model-en-us-0.22         (English, ~1.8 GB, more accurate)
      vosk-model-small-de-0.15      (German, ~45 MB)
      vosk-model-de-0.21            (German, ~1.9 GB, best umlaut handling)

.EXAMPLE
    ./scripts/Get-VoskModel.ps1
    ./scripts/Get-VoskModel.ps1 -Model vosk-model-small-de-0.15
#>
param(
    [string]$Model = "vosk-model-small-en-us-0.15",
    [string]$BaseUrl = "https://alphacephei.com/vosk/models"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$modelsDir = Join-Path $repoRoot "models"
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

$dest = Join-Path $modelsDir $Model
if (Test-Path $dest) {
    Write-Host "Model already installed: $dest" -ForegroundColor Green
    exit 0
}

$zip = Join-Path $modelsDir "$Model.zip"
$url = "$BaseUrl/$Model.zip"

Write-Host "Downloading $url" -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $zip

Write-Host "Extracting..." -ForegroundColor Cyan
Expand-Archive -Path $zip -DestinationPath $modelsDir -Force
Remove-Item $zip -Force

Write-Host "Installed: $dest" -ForegroundColor Green
Write-Host "Restart the app - it will detect the model and use real voice tracking." -ForegroundColor Green
