# Installs only the committed FPSC proof package; never removes unrelated add-ons.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$packageName = 'frontier-airport-fpsc'
function Assert-SimulatorClosed {
    $running = @(Get-CimInstance Win32_Process | Where-Object Name -Match '^(FlightSimulator.*|fspackagetool)\.exe$')
    if ($running.Count) { throw 'Close MSFS and Package Tool before installing FPSC.' }
    [DateTime]::UtcNow.ToString('o')
}
function Assert-Within([string]$path, [string]$root) {
    $resolved = [IO.Path]::GetFullPath($path)
    $boundary = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($root)) + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "Path outside intended root: $resolved" }
}
function Inventory([string]$root) {
    @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
}
function OtherEntries([string[]]$folders) {
    @(foreach($folder in $folders) {
        Get-ChildItem -LiteralPath $folder -Force | Where-Object Name -NE $packageName | Sort-Object Name | ForEach-Object {
            [pscustomobject]@{Folder=$folder;Name=$_.Name;Attributes=$_.Attributes.ToString();LastWriteTimeUtc=$_.LastWriteTimeUtc.ToString('o');LinkTarget=$_.Target}
        }
    })
}
$closedBefore = Assert-SimulatorClosed
$config = Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024\UserCfg.opt'
$configHash = (Get-FileHash -LiteralPath $config).Hash
$text = [IO.File]::ReadAllText($config)
$match = [regex]::Match($text, '(?m)^\s*InstalledPackagesPath\s+"([^"]+)"\s*$')
if (!$match.Success) { throw 'Cannot resolve InstalledPackagesPath.' }
$packages = (Resolve-Path -LiteralPath $match.Groups[1].Value).Path
$community = Join-Path $packages 'Community2024'
$legacy = Join-Path $packages 'Community'
foreach ($folder in @($community,$legacy)) {
    if (!(Test-Path -LiteralPath $folder -PathType Container)) { throw "Expected Community folder missing: $folder" }
    if ((Get-Item -LiteralPath $folder).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Community root is redirected; inspect its target before installing.' }
}
$target = Join-Path $community $packageName
$legacyTarget = Join-Path $legacy $packageName
Assert-Within $target $community
Assert-Within $legacyTarget $legacy
$archive = Join-Path $repo 'artifacts\fpsc\frontier-airport-fpsc.zip'
$expectedHash = (Get-Content (Join-Path $repo 'evidence\skeleton-coast\build\package-archive.json') -Raw | ConvertFrom-Json).Hash
if ((Get-FileHash -LiteralPath $archive).Hash -ne $expectedHash) { throw 'Committed package archive hash differs from build evidence.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$receipt = Join-Path $repo "evidence\skeleton-coast\install-$stamp"
$localRoot = Join-Path $env:LOCALAPPDATA 'FrontierAirportBuilder'
$staging = Join-Path $localRoot "staging\$stamp"
$backupRoot = Join-Path $localRoot "backups\$stamp"
Assert-Within $staging $localRoot
Assert-Within $backupRoot $localRoot
$before = OtherEntries @($legacy,$community)
New-Item -ItemType Directory -Path $receipt,$staging | Out-Null
$before | ConvertTo-Json -Depth 5 | Set-Content "$receipt\unrelated-entries-before.json"
# Inspect entry names before extraction. The trusted archive must contain exactly this package root.
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if (!$name.StartsWith("$packageName/",[StringComparison]::Ordinal) -or
            $name.Split('/') -contains '..' -or $name.Contains(':')) { throw "Unexpected archive entry: $name" }
    }
} finally { $zip.Dispose() }
Expand-Archive -LiteralPath $archive -DestinationPath $staging
$staged = Join-Path $staging $packageName
Assert-Within $staged $staging
if (@(Get-ChildItem -LiteralPath $staging -Force).Count -ne 1) { throw 'Staging contains unexpected extra content.' }
$expected = Get-Content (Join-Path $repo 'evidence\skeleton-coast\build\archive-verification.json') -Raw | ConvertFrom-Json
$stagedInventory = Inventory $staged
if ($stagedInventory.Count -ne $expected.Count) { throw 'Staged file count mismatch.' }
foreach ($file in $stagedInventory) {
    $reference = @($expected | Where-Object { $_.Entry.Replace('\','/') -eq "$packageName/$($file.Path)" })
    if ($reference.Count -ne 1 -or $reference[0].SHA256 -ne $file.SHA256 -or $reference[0].Bytes -ne $file.Bytes) { throw "Staged hash/size mismatch: $($file.Path)" }
}
$manifest = Get-Content "$staged\manifest.json" -Raw | ConvertFrom-Json
if ($manifest.title -ne 'Skeleton Coast Test Strip' -or $manifest.creator -ne 'Frontier Pilot' -or $manifest.package_order_hint -ne 'CUSTOM_AIRPORT') { throw 'Wrong package identity.' }
$stagedInventory | ConvertTo-Json | Set-Content "$receipt\staged-files.json"
$backups = @()
$newInstallMoveAttempted = $false
$closedBeforeInstall = Assert-SimulatorClosed
try {
    foreach ($existing in @($target,$legacyTarget)) {
        if (Test-Path -LiteralPath $existing) {
            $item = Get-Item -LiteralPath $existing
            if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Existing FPSC is not a normal directory: $existing" }
            $prior = Inventory $existing
            $parentName = Split-Path (Split-Path $existing -Parent) -Leaf
            $backup = Join-Path $backupRoot "$parentName\$packageName"
            Assert-Within $backup $backupRoot
            Assert-Within $existing $packages
            New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
            Move-Item -LiteralPath $existing -Destination $backup
            $backups += [pscustomobject]@{Original=$existing;Backup=$backup}
            $prior | ConvertTo-Json | Set-Content "$receipt\backup-$parentName-files.json"
            if (Compare-Object ($prior | ConvertTo-Json -Compress) ((Inventory $backup) | ConvertTo-Json -Compress)) { throw 'Backup verification failed.' }
        }
    }
    $null = Assert-SimulatorClosed
    Assert-Within $staged $localRoot
    Assert-Within $target $community
    if (Test-Path -LiteralPath $target) { throw 'FPSC target unexpectedly reappeared; refusing to overwrite it.' }
    $newInstallMoveAttempted = $true
    Move-Item -LiteralPath $staged -Destination $target
    $installed = Inventory $target
    if (Compare-Object ($stagedInventory | ConvertTo-Json -Compress) ($installed | ConvertTo-Json -Compress)) { throw 'Installed files differ from verified staging.' }
    $installed | ConvertTo-Json | Set-Content "$receipt\installed-files.json"
} catch {
    # Preserve a partial/new install for diagnosis; restore only exact backed-up FPSC paths.
    if ($newInstallMoveAttempted -and (Test-Path -LiteralPath $target)) {
        $failed = Join-Path $staging 'failed-install'
        Assert-Within $failed $localRoot
        Assert-Within $target $community
        Move-Item -LiteralPath $target -Destination $failed
    }
    foreach ($backup in $backups) {
        Assert-Within $backup.Backup $backupRoot
        Assert-Within $backup.Original $packages
        if (!(Test-Path -LiteralPath $backup.Original)) { Move-Item -LiteralPath $backup.Backup -Destination $backup.Original }
    }
    $_ | Out-String | Set-Content "$receipt\failure.log"
    throw
}
$after = OtherEntries @($legacy,$community)
$after | ConvertTo-Json -Depth 5 | Set-Content "$receipt\unrelated-entries-after.json"
$unrelatedUnchanged = !(Compare-Object ($before | ConvertTo-Json -Depth 5 -Compress) ($after | ConvertTo-Json -Depth 5 -Compress))
[pscustomobject]@{
    InstalledUtc=[DateTime]::UtcNow.ToString('o');SimulatorClosedChecksUtc=@($closedBefore,$closedBeforeInstall);
    UserConfig=$config;UserConfigUnchanged=((Get-FileHash -LiteralPath $config).Hash -eq $configHash);
    PackagesRoot=$packages;InstallPath=$target;ArchiveSHA256=$expectedHash;VerifiedFiles=$installed.Count;
    ExistingFpscBackups=$backups;UnrelatedEntriesUnchanged=$unrelatedUnchanged;UnrelatedEntryCount=$before.Count;
    ReceiptPath=$receipt;StagingPath=$staging
} | ConvertTo-Json -Depth 5 | Tee-Object "$receipt\receipt.json"
if (!$unrelatedUnchanged) { throw 'Unrelated entry metadata changed during installation; inspect receipt.' }

