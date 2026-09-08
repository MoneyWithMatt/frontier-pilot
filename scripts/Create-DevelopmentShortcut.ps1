param([ValidateSet('Desktop','StartMenu')][string]$Location = 'Desktop')
$ErrorActionPreference = 'Stop'

$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$application = Join-Path $repo 'dist\FrontierPilot\FrontierPilot.exe'
if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Published application not found at $application. Run scripts\Publish-Development.ps1 first."
}

$folder = if ($Location -eq 'StartMenu') { [Environment]::GetFolderPath('Programs') } else { [Environment]::GetFolderPath('Desktop') }
$shortcutPath = Join-Path $folder 'Frontier Pilot.lnk'
$shell = New-Object -ComObject WScript.Shell
if (Test-Path -LiteralPath $shortcutPath) {
    $current = $shell.CreateShortcut($shortcutPath)
    if ([IO.Path]::GetFullPath($current.TargetPath) -ne [IO.Path]::GetFullPath($application)) {
        throw "Refusing to replace an unrelated shortcut at $shortcutPath"
    }
}

$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $application
$shortcut.WorkingDirectory = Split-Path $application -Parent
$shortcut.Description = 'Launch Frontier Pilot'
$shortcut.IconLocation = "$application,0"
$shortcut.Save()
Write-Host "Shortcut ready: $shortcutPath"
