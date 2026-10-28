param(
    [string]$OutputPath,
    [ValidateRange(1,3600)][int]$Seconds = 300,
    [string]$SdkRoot = 'C:\MSFS 2024 SDK',
    [string]$AirframePath
)
$ErrorActionPreference = 'Stop'
$conditionRepo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$env:DOTNET_CLI_HOME = Join-Path $conditionRepo '.tools\home'
$env:NUGET_PACKAGES = Join-Path $conditionRepo '.tools\nuget'
if (-not $OutputPath) {
    $conditionOutputDirectory = Join-Path $conditionRepo '.tools\condition-recordings'
    New-Item -ItemType Directory -Force -Path $conditionOutputDirectory | Out-Null
    $OutputPath = Join-Path $conditionOutputDirectory ('condition-' + [Guid]::NewGuid().ToString('N') + '.jsonl')
}
if (Test-Path -LiteralPath $OutputPath) { throw 'Recording output already exists; choose a new file.' }
$conditionArguments = @('run', '--project', (Join-Path $conditionRepo 'src\App\FrontierAirportBuilder.App.csproj'),
    '-c', 'Release', '--', 'record-condition', $OutputPath, "$Seconds", $SdkRoot)
if ($AirframePath) { $conditionArguments += $AirframePath }
Write-Host "Read-only recording output: $OutputPath"
& (Join-Path $conditionRepo '.tools\dotnet\dotnet.exe') @conditionArguments
if ($LASTEXITCODE -ne 0) { throw "Condition recording failed (exit $LASTEXITCODE). Inspect the partial JSONL if present." }
