param([ValidateSet('project','tool')][string]$WorkingDirectory = 'project', [string[]]$Options = @('-nopause'), [ValidateRange(1,3600)][int]$TimeoutSeconds = 150, [switch]$CorrectSdkEnvironment, [switch]$PackageOrderCompatibilityFix, [string]$SdkRoot = 'C:\Users\mattr\Desktop\MSFS 2024 SDK')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sdk = [IO.Path]::GetFullPath($SdkRoot)
$source = Join-Path $sdk 'Samples\DevmodeProjects\Sceneries\SimpleAirport'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$e = Join-Path $repo "evidence\diagnosis-$stamp"
$w = Join-Path $repo "workspaces\diagnosis-$stamp"
New-Item -ItemType Directory $e,$w | Out-Null
Copy-Item -LiteralPath $source -Destination "$w\SimpleAirport" -Recurse
$project = "$w\SimpleAirport\AirportProject.xml"
$inputs = @(Get-ChildItem $source -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($source,$_.FullName)
    $hash = (Get-FileHash $_.FullName).Hash
    if ((Get-FileHash (Join-Path "$w\SimpleAirport" $relative)).Hash -ne $hash) { throw 'Input copy mismatch' }
    [pscustomobject]@{Path=$relative;SHA256=$hash}
})
$inputs | ConvertTo-Json | Set-Content "$e\inputs.json"
if ($PackageOrderCompatibilityFix) {
    $definition = "$w\SimpleAirport\PackageDefinitions\mycompany-airport.xml"
    $xml = [IO.File]::ReadAllText($definition)
    $xml = $xml.Replace('<AssetPackage Version="0.1.0">', "<AssetPackage Version=`"0.1.0`">`r`n`t<PackageOrderHint>CUSTOM_AIRPORT</PackageOrderHint>")
    [IO.File]::WriteAllText($definition, $xml, [Text.UTF8Encoding]::new($false))
    Copy-Item $definition "$e\compatibility-package-definition.xml"
}
$builderLog = Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024\BuilderLogError.txt'
$builderBefore = if (Test-Path $builderLog) { [IO.File]::ReadAllText($builderLog) } else { '' }
$cwd = if ($WorkingDirectory -eq 'tool') { "$sdk\Tools\bin" } else { "$w\SimpleAirport" }
$psi = [Diagnostics.ProcessStartInfo]::new("$sdk\Tools\bin\fspackagetool.exe")
$psi.WorkingDirectory=$cwd
$psi.UseShellExecute=$false
$psi.CreateNoWindow=$true
$psi.RedirectStandardOutput=$true
$psi.RedirectStandardError=$true
$psi.RedirectStandardInput=$true
if ($CorrectSdkEnvironment) { $psi.Environment['MSFS2024_SDK'] = "$sdk\" }
$psi.ArgumentList.Add($project)
foreach ($option in $Options) { $psi.ArgumentList.Add($option) }
[pscustomobject]@{WorkingDirectory=$cwd;Executable=$psi.FileName;Arguments=@($psi.ArgumentList);MSFS2024_SDK=$psi.Environment['MSFS2024_SDK'];CompatibilityFixApplied=[bool]$PackageOrderCompatibilityFix;StartedUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content "$e\invocation.json"
@("Set-Location -LiteralPath '$($cwd.Replace("'","''"))'", "`$env:MSFS2024_SDK = '$($psi.Environment['MSFS2024_SDK'].Replace("'","''"))'", ("& '" + $psi.FileName.Replace("'","''") + "' " + ((@($psi.ArgumentList) | ForEach-Object { "'" + $_.Replace("'","''") + "'" }) -join ' '))) | Set-Content "$e\command.ps1"
$p=[Diagnostics.Process]::Start($psi)
$p.StandardInput.Close()
$out=$p.StandardOutput.ReadToEndAsync()
$err=$p.StandardError.ReadToEndAsync()
$timer=[Diagnostics.Stopwatch]::StartNew()
$seen=@{}
while (!$p.HasExited -and $timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
    Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'FlightSimulator|fspackagetool|steam' } | ForEach-Object {
        $key="$($_.ProcessId):$($_.CreationDate)"
        if (!$seen.ContainsKey($key)) {
            $seen[$key]=$true
            [pscustomobject]@{ObservedUtc=[DateTime]::UtcNow.ToString('o');PID=$_.ProcessId;ParentPID=$_.ParentProcessId;Name=$_.Name;CommandLine=$_.CommandLine} | ConvertTo-Json -Compress | Add-Content "$e\processes.jsonl"
        }
    }
    Start-Sleep -Seconds 2
}
$timedOut = !$p.HasExited
if ($timedOut) { $p.Kill(); $p.WaitForExit() }
$out.GetAwaiter().GetResult() | Set-Content "$e\stdout.log"
$err.GetAwaiter().GetResult() | Set-Content "$e\stderr.log"
$builderAfter = if (Test-Path $builderLog) { [IO.File]::ReadAllText($builderLog) } else { '' }
$builderDelta = if ($builderAfter.StartsWith($builderBefore)) { $builderAfter.Substring($builderBefore.Length) } else { $builderAfter }
$builderDelta | Set-Content "$e\builder-errors-this-run.log"
$files=@(Get-ChildItem $w -Recurse -File | ForEach-Object { [pscustomobject]@{Path=[IO.Path]::GetRelativePath($w,$_.FullName);Bytes=$_.Length;SHA256=(Get-FileHash $_.FullName).Hash} })
$files | ConvertTo-Json | Set-Content "$e\output-tree.json"
$unchanged = @($inputs | Where-Object { (Get-FileHash (Join-Path $source $_.Path)).Hash -ne $_.SHA256 }).Count -eq 0
$layoutExists = Test-Path "$w\SimpleAirport\Packages\mycompany-airport\layout.json"
[pscustomobject]@{ExitCode=$p.ExitCode;TimedOut=$timedOut;Seconds=$timer.Elapsed.TotalSeconds;InstalledSampleUnchanged=$unchanged;CompatibilityFixApplied=[bool]$PackageOrderCompatibilityFix;LayoutExists=$layoutExists;ValidationErrors=($builderDelta -match 'Validation failed|\[Package Validation\].*Empty');BglFiles=@($files | Where-Object Path -Like '*.bgl');Evidence=$e} | ConvertTo-Json -Depth 4 | Tee-Object "$e\result.json"
