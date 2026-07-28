<#
.SYNOPSIS
    Downloads a sherpa-onnx streaming zipformer model into models/ so the
    TalkPrompter uses the modern engine (better accuracy than Vosk,
    especially for fast or accented speech). Fully offline after download.

.PARAMETER Model
    Release asset name (without .tar.bz2). Options:
      sherpa-onnx-streaming-zipformer-en-2023-06-26      (English, best accuracy)
      sherpa-onnx-streaming-zipformer-en-20M-2023-02-17  (English, small + fastest)

.EXAMPLE
    ./scripts/Get-SherpaModel.ps1
    ./scripts/Get-SherpaModel.ps1 -Model sherpa-onnx-streaming-zipformer-en-20M-2023-02-17
#>
param(
    [string]$Model = "sherpa-onnx-streaming-zipformer-en-2023-06-26",
    [string]$BaseUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models"
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

$tar = Join-Path $modelsDir "$Model.tar.bz2"
$url = "$BaseUrl/$Model.tar.bz2"

Write-Host "Downloading $url" -ForegroundColor Cyan
Invoke-WebRequest -Uri $url -OutFile $tar

Write-Host "Extracting..." -ForegroundColor Cyan
tar -xjf $tar -C $modelsDir
Remove-Item $tar -Force

Write-Host "Installed: $dest" -ForegroundColor Green
Write-Host "Restart the app - it will prefer the sherpa-onnx model automatically." -ForegroundColor Green
