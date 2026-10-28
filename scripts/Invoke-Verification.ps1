#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('All', 'BuildTest', 'Smoke', 'Package')][string]$Stage = 'All',
    [string]$DotnetPath,
    [string]$OutputDirectory = '.ci-output'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsWindows) { throw 'Full verification requires Windows (WPF and named semaphores).' }
$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$output = [IO.Path]::GetFullPath((Join-Path $repo $OutputDirectory))
# Outputs never replace the local distribution or any pre-existing directory.
if (-not $output.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory must be inside this checkout.'
}
if (-not $DotnetPath) {
    $DotnetPath = Join-Path $repo '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path -LiteralPath $DotnetPath)) { $DotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
}
$DotnetPath = (Get-Command $DotnetPath -ErrorAction Stop).Source
$env:DOTNET_ROOT = Split-Path $DotnetPath -Parent
$env:DOTNET_CLI_HOME = Join-Path $repo '.tools/home'
$env:NUGET_PACKAGES = Join-Path $repo '.tools/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
# Tests must not export diagnostic state into a caller's evidence directory.
Remove-Item Env:FRONTIER_TEST_EVIDENCE -ErrorAction SilentlyContinue
Push-Location $repo
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Cannot identify source commit.' }
    $dirty = [bool](& git status --porcelain)
    $sdk = (& $DotnetPath --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -ne (Get-Content global.json -Raw | ConvertFrom-Json).sdk.version) {
        throw 'Provision the exact SDK in global.json before verification.'
    }
    function Invoke-Dotnet([string]$Log, [string[]]$Arguments) {
        & $DotnetPath @Arguments 2>&1 | Tee-Object -FilePath (Join-Path $output $Log)
        if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE); see $Log." }
    }
    function Write-Receipt([string]$Name) {
        @{ sourceCommit = $commit; sourceDirty = $dirty; sdk = $sdk; completedUtc = [DateTimeOffset]::UtcNow.ToString('o'); outcome = 'Passed' } |
            ConvertTo-Json | Set-Content (Join-Path $output "$Name.json") -Encoding utf8
    }
    function Require-Receipt([string]$Name) {
        $receipt = Get-Content (Join-Path $output "$Name.json") -Raw | ConvertFrom-Json
        if ($receipt.outcome -ne 'Passed' -or $receipt.sourceCommit -ne $commit -or $receipt.sdk -ne $sdk -or $receipt.sourceDirty -or $dirty) {
            throw "$Name must pass on this exact clean commit before publishing a traceable bundle."
        }
    }
    if ($Stage -in 'All', 'BuildTest') {
        if (Test-Path -LiteralPath $output) { throw 'Choose a new OutputDirectory; previous evidence is never overwritten.' }
        New-Item -ItemType Directory -Path $output | Out-Null
        Invoke-Dotnet 'restore.log' @('restore', 'FrontierAirportBuilder.sln')
        Invoke-Dotnet 'build.log' @('build', 'FrontierAirportBuilder.sln', '-c', 'Release', '--no-restore')
        Invoke-Dotnet 'tests.log' @('test', 'tests/Tests/FrontierAirportBuilder.Tests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=regression.trx', '--results-directory', (Join-Path $output 'tests'), '--blame-hang-timeout', '5m')
        [xml]$trx = Get-Content (Join-Path $output 'tests/regression.trx') -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if ([int]$counters.total -le 0 -or [int]$counters.failed -ne 0 -or $trx.TestRun.ResultSummary.outcome -ne 'Completed') {
            throw 'Regression TRX does not report a successful non-empty run.'
        }
        Write-Receipt 'regression'
    }
    if ($Stage -in 'All', 'Smoke') {
        if (-not (Test-Path -LiteralPath $output)) { throw 'Run BuildTest first.' }
        # One attempt per output directory, including failed/interrupted and legacy attempts.
        # Never overwrite logs while leaving a previous passing receipt eligible for packaging.
        $previousSmoke = Get-ChildItem -LiteralPath $output -Force | Where-Object {
            $_.Name -in 'smoke.json', 'smoke-attempt.json', 'screenshots' -or
            $_.Name -like 'LoadingWpfSmoke*' -or $_.Name -like 'SiteUiSmoke*'
        }
        if ($previousSmoke) { throw 'Smoke was already attempted; run BuildTest and Smoke in a new OutputDirectory.' }
        # CreateNew also prevents two Smoke invocations from claiming the same directory.
        $attempt = [IO.File]::Open((Join-Path $output 'smoke-attempt.json'), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try {
            $bytes = [Text.Encoding]::UTF8.GetBytes((@{ sourceCommit = $commit; startedUtc = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json))
            $attempt.Write($bytes, 0, $bytes.Length)
        } finally { $attempt.Dispose() }
        foreach ($name in @('LoadingWpfSmoke', 'SiteUiSmoke')) {
            Invoke-Dotnet "$name-build.log" @('build', "tests/$name/$name.csproj", '-c', 'Release')
            $tfm = if ($name -eq 'SiteUiSmoke') { 'net8.0-windows10.0.19041' } else { 'net8.0-windows' }
            $dll = Join-Path $repo "tests/$name/bin/Release/$tfm/$name.dll"
            $argument = if ($name -eq 'SiteUiSmoke') { Join-Path $output 'screenshots' } else { Join-Path $repo 'src/Desktop/MainWindow.xaml' }
            $stdout = Join-Path $output "$name.log"
            $stderr = Join-Path $output "$name-error.log"
            $process = Start-Process -FilePath $DotnetPath -ArgumentList ('"{0}" "{1}"' -f $dll, $argument) -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            try {
                if (-not $process.WaitForExit(120000)) {
                    $process.Kill($true)
                    throw "$name timed out after 120 seconds. Only its owned test process was stopped."
                }
                if ($process.ExitCode -ne 0) { throw "$name failed ($($process.ExitCode)); see $stderr." }
            } finally { $process.Dispose() }
            Get-Content $stdout
        }
        Write-Receipt 'smoke'
    }
    if ($Stage -in 'All', 'Package') {
        Require-Receipt 'regression'
        Require-Receipt 'smoke'
        $bundle = Join-Path $output 'development'
        if (Test-Path -LiteralPath $bundle) { throw 'Development bundle already exists; use a fresh verification run.' }
        $application = Join-Path $bundle 'application'
        Invoke-Dotnet 'publish.log' @('publish', 'src/Desktop/FrontierAirportBuilder.Desktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishProfile=DevelopmentWinX64', '-o', $application)
        if (-not (Test-Path -LiteralPath (Join-Path $application 'FrontierPilot.exe'))) { throw 'Published executable missing.' }
        Copy-Item (Join-Path $output 'tests') (Join-Path $bundle 'test-results') -Recurse
        Copy-Item (Join-Path $output 'regression.json'), (Join-Path $output 'smoke.json') $bundle
        @{ designation = 'Unsigned development build'; sourceCommit = $commit; sourceDirty = $false; builtUtc = [DateTimeOffset]::UtcNow.ToString('o'); sdk = $sdk; runtime = 'win-x64'; runUrl = if ($env:GITHUB_ACTIONS -eq 'true') { "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID" } else { $null }; simulatorVerification = 'Not performed' } |
            ConvertTo-Json | Set-Content (Join-Path $bundle 'build.json') -Encoding utf8
        'DEVELOPMENT BUILD. Unsigned; no live MSFS verification. Extract the complete bundle. application/FrontierPilot.exe is self-contained. Downloading does not authorize execution on the gaming PC. See build.json and SHA256SUMS.txt. No installation, SDK or personal saves are included.' |
            Set-Content (Join-Path $bundle 'README.txt') -Encoding utf8
        $checksums = Get-ChildItem -LiteralPath $bundle -Recurse -File | Sort-Object FullName | ForEach-Object {
            '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetRelativePath($bundle, $_.FullName).Replace('\', '/')
        }
        $checksums | Set-Content (Join-Path $bundle 'SHA256SUMS.txt') -Encoding utf8
        Write-Receipt 'package'
    }
} finally { Pop-Location }
