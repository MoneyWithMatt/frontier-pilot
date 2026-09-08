param([switch]$CreateDesktopShortcut)
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$dotnet = Join-Path $repo '.tools\dotnet\dotnet.exe'
$project = Join-Path $repo 'src\Desktop\FrontierAirportBuilder.Desktop.csproj'
$distributionRoot = Join-Path $repo 'dist'
$destination = Join-Path $distributionRoot 'FrontierPilot'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0,8)
$staging = Join-Path $distributionRoot ".publish-$suffix"
$previous = Join-Path $distributionRoot ".previous-$suffix"

if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) { throw "Repository .NET SDK not found at $dotnet" }
$publishedApplication = Join-Path $destination 'FrontierPilot.exe'
$runningPublishedApp = Get-Process -Name FrontierPilot -ErrorAction SilentlyContinue | Where-Object {
    try { [IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($publishedApplication) } catch { $false }
}
if ($runningPublishedApp) { throw 'Close Frontier Pilot before replacing the development distribution.' }
New-Item -ItemType Directory -Force -Path $distributionRoot | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $repo '.tools\home'
$env:NUGET_PACKAGES = Join-Path $repo '.tools\nuget'

try {
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishProfile=DevelopmentWinX64 -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
    $stagedApplication = Join-Path $staging 'FrontierPilot.exe'
    if (-not (Test-Path -LiteralPath $stagedApplication -PathType Leaf)) { throw 'Publish did not create FrontierPilot.exe.' }
    Get-ChildItem -LiteralPath $staging -Filter '*.pdb' -File | Remove-Item -Force

    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $previous }
    try { Move-Item -LiteralPath $staging -Destination $destination }
    catch {
        if ((Test-Path -LiteralPath $previous) -and -not (Test-Path -LiteralPath $destination)) { Move-Item -LiteralPath $previous -Destination $destination }
        throw
    }
    if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}

$application = Join-Path $destination 'FrontierPilot.exe'
if ($CreateDesktopShortcut) { & (Join-Path $PSScriptRoot 'Create-DevelopmentShortcut.ps1') }
Write-Host "Frontier Pilot is ready: $application"
