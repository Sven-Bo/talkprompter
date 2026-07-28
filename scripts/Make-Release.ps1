<#
.SYNOPSIS
    Builds a distributable release of TalkPrompter:
      1. Publishes a Release build to publish/ (framework-dependent, win-x64).
      2. Ensures the speech models are bundled.
      3. Zips everything to dist/TalkPrompter-v<version>.zip.

.NOTES
    The zip is fully portable: unzip anywhere and run TalkPrompter.exe.
    Requires the .NET 8 Desktop Runtime on the target machine.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# Version from Directory.Build.props
$props = [xml](Get-Content "Directory.Build.props")
$version = $props.Project.PropertyGroup.Version
if (-not $version) { $version = "0.0.0" }

Write-Host "Publishing v$version..." -ForegroundColor Cyan
dotnet publish src/Teleprompter.App/Teleprompter.App.csproj -c $Configuration -r win-x64 --self-contained false -o publish | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Bundle models if present in the repo but missing from publish
$modelPairs = @(
    "vosk-model-small-en-us-0.15",
    "sherpa-onnx-streaming-zipformer-en-2023-06-26"
)
New-Item -ItemType Directory -Force -Path "publish\models" | Out-Null
foreach ($m in $modelPairs) {
    $src = "models\$m"; $dst = "publish\models\$m"
    if ((Test-Path $src) -and -not (Test-Path $dst)) {
        if ($m -like "sherpa*") {
            New-Item -ItemType Directory -Force -Path $dst | Out-Null
            Copy-Item "$src\tokens.txt" $dst -Force
            Copy-Item "$src\*.int8.onnx" $dst -Force
        }
        else {
            Copy-Item $src $dst -Recurse -Force
        }
    }
}

New-Item -ItemType Directory -Force -Path "dist" | Out-Null
$zip = "dist\TalkPrompter-v$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "Zipping to $zip..." -ForegroundColor Cyan
Compress-Archive -Path "publish\*" -DestinationPath $zip -CompressionLevel Optimal

$size = "{0:N1} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "Release ready: $zip ($size)" -ForegroundColor Green
