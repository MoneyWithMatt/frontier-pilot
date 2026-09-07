$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$env:DOTNET_ROOT = Join-Path $repo '.tools\dotnet'
$exe = Join-Path $repo 'src\Desktop\bin\Debug\net8.0-windows\FrontierAirportBuilder.Desktop.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the solution first.' }
Start-Process -FilePath $exe
