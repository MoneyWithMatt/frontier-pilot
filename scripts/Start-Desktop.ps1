param([switch]$DryRun, [ValidateSet("Debug","Release")][string]$Configuration = "Release")
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $repo '.tools\dotnet'
$exe = Join-Path $repo "src\Desktop\bin\$Configuration\net8.0-windows\FrontierPilot.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the solution first.' }
if ($DryRun) { Start-Process -FilePath $exe -ArgumentList "--dry-run" } else { Start-Process -FilePath $exe }
