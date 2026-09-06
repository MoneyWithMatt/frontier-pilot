param([string]$SdkRoot = 'C:\Users\mattr\Desktop\MSFS 2024 SDK', [ValidateRange(1,3600)][int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$evidence = Join-Path $repo "evidence\simpleairport-$stamp"
$workspace = Join-Path $repo "workspaces\simpleairport-$stamp"
$source = Join-Path $SdkRoot 'Samples\DevmodeProjects\Sceneries\SimpleAirport'
$tool = Join-Path $SdkRoot 'Tools\bin\fspackagetool.exe'
function Get-Inventory([string]$root) {
    @(Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($root, $_.FullName); Bytes = $_.Length; SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
New-Item -ItemType Directory -Path $evidence,$workspace | Out-Null
$before = Get-Inventory $source
$before | ConvertTo-Json -Depth 4 | Set-Content "$evidence\installed-inputs-before.json"
Copy-Item -LiteralPath $source -Destination "$workspace\SimpleAirport" -Recurse
$copy = Get-Inventory "$workspace\SimpleAirport"
$copy | ConvertTo-Json -Depth 4 | Set-Content "$evidence\copied-inputs-before.json"
if (Compare-Object ($before | ConvertTo-Json -Compress) ($copy | ConvertTo-Json -Compress)) { throw 'Copy differs from installed sample.' }
$project = "$workspace\SimpleAirport\AirportProject.xml"
$argsList = @($project, '-outputdir', "$workspace\output", '-tempdir', "$workspace\temp", '-rebuild', '-forcesteam', '-nopause')
[pscustomobject]@{ SdkRoot=$SdkRoot; SdkVersion=(Get-Content "$SdkRoot\version.txt" -Raw).Trim(); Tool=$tool; ToolSHA256=(Get-FileHash $tool).Hash; Source=$source; Workspace=$workspace; Arguments=$argsList; StartedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 4 | Set-Content "$evidence\invocation.json"
('& ''' + $tool.Replace("'","''") + ''' ' + (($argsList | ForEach-Object { "'" + $_.Replace("'","''") + "'" }) -join ' ')) | Set-Content "$evidence\command.ps1"
$start = [Diagnostics.ProcessStartInfo]::new($tool)
$start.WorkingDirectory = $workspace
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.RedirectStandardInput = $true
foreach ($arg in $argsList) { $start.ArgumentList.Add($arg) }
$process = [Diagnostics.Process]::new()
$process.StartInfo = $start
try {
    if (!$process.Start()) { throw 'Package tool did not start.' }
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = !$process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true); $process.WaitForExit() }
    $stdout.GetAwaiter().GetResult() | Set-Content "$evidence\stdout.log"
    $stderr.GetAwaiter().GetResult() | Set-Content "$evidence\stderr.log"
    $exitCode = $process.ExitCode
} catch {
    $_ | Out-String | Set-Content "$evidence\launch-error.log"
    throw
} finally {
    $after = Get-Inventory $source
    $after | ConvertTo-Json -Depth 4 | Set-Content "$evidence\installed-inputs-after.json"
    Get-Inventory $workspace | ConvertTo-Json -Depth 4 | Set-Content "$evidence\output-tree.json"
    Get-ChildItem $workspace -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($workspace,$_.FullName) } | Set-Content "$evidence\output-tree.txt"
    [pscustomobject]@{ ExitCode=$exitCode; TimedOut=$timedOut; InstalledSampleUnchanged=!(Compare-Object ($before | ConvertTo-Json -Compress) ($after | ConvertTo-Json -Compress)); BglFiles=@(Get-ChildItem "$workspace\output" -Filter '*.bgl' -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName); FinishedUtc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 4 | Set-Content "$evidence\result.json"
}
Get-Content "$evidence\result.json"
Write-Output "Evidence: $evidence"
