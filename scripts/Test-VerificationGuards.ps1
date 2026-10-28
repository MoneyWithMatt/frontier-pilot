#requires -Version 7.2
# Run in a separate pwsh process. Exercises the real entry point with synthetic commands;
# no SDK, WPF, application, simulator or package publisher is executed.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path (Split-Path $PSScriptRoot -Parent) ('.ci-output-guard-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Invoke-Verification.ps1') (Join-Path $fixture 'scripts')
Copy-Item (Join-Path (Split-Path $PSScriptRoot -Parent) 'global.json') $fixture
$testState = @{ Mode = 'pass'; Builds = 0; Starts = 0; Publishes = 0; Killed = $false }
$testCommit = '0123456789012345678901234567890123456789'
$testSdk = (Get-Content (Join-Path $fixture 'global.json') -Raw | ConvertFrom-Json).sdk.version
function git {
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'rev-parse') { return $testCommit }
    if ($args[0] -ne 'status') { throw 'Unexpected git command in test.' }
}
$fakeDotnet = Join-Path $fixture 'fake-dotnet.ps1'
@'
$global:LASTEXITCODE = 0
switch ($args[0]) {
    '--version' { $testSdk }
    'build' {
        $testState.Builds++
        'Synthetic build output'
        if ($testState.Mode -eq 'build-failure') { $global:LASTEXITCODE = 1 }
    }
    'publish' { $testState.Publishes++; throw 'PUBLISH_REACHED' }
    default { throw 'Unexpected dotnet command in test.' }
}
'@ | Set-Content $fakeDotnet
function Start-Process {
    param($FilePath, $ArgumentList, $WorkingDirectory, $WindowStyle, [switch]$PassThru,
        $RedirectStandardOutput, $RedirectStandardError)
    $testState.Starts++
    'Synthetic smoke output' | Set-Content $RedirectStandardOutput
    'Synthetic diagnostic output' | Set-Content $RedirectStandardError
    $process = [pscustomobject]@{ ExitCode = $(if ($testState.Mode -eq 'exit-failure') { 1 } else { 0 }) }
    $process | Add-Member ScriptMethod WaitForExit { param($milliseconds) return $testState.Mode -ne 'timeout' }
    $process | Add-Member ScriptMethod Kill { param($tree) $testState.Killed = $true }
    $process | Add-Member ScriptMethod Dispose {}
    return $process
}
function Assert-True($Condition, $Message) { if (-not $Condition) { throw $Message } }
function Invoke-Stage($Stage, $Directory) {
    & (Join-Path $fixture 'scripts/Invoke-Verification.ps1') -Stage $Stage -DotnetPath $fakeDotnet -OutputDirectory ([IO.Path]::GetRelativePath($fixture, $Directory)) | Out-Null
}
function Assert-Fails($Action, $Pattern) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    Assert-True ($null -ne $caught -and $caught.Exception.Message -like $Pattern) "Expected failure '$Pattern'; got '$caught'."
}
function New-Case($Name) {
    $directory = Join-Path $fixture $Name
    New-Item -ItemType Directory -Path $directory | Out-Null
    @{ sourceCommit = $testCommit; sourceDirty = $false; sdk = $testSdk; outcome = 'Passed' } |
        ConvertTo-Json | Set-Content (Join-Path $directory 'regression.json')
    return $directory
}
function Evidence-Hashes($Directory) {
    return (Get-ChildItem $Directory -File -Recurse | Sort-Object FullName | ForEach-Object {
        $_.FullName + ':' + (Get-FileHash $_.FullName).Hash
    }) -join "`n"
}

$passed = New-Case 'passed'
Invoke-Stage Smoke $passed
Assert-True ((Get-Content (Join-Path $passed 'smoke.json') -Raw | ConvertFrom-Json).outcome -eq 'Passed') 'Successful smoke did not issue a receipt.'
Assert-Fails { Invoke-Stage Package $passed } '*PUBLISH_REACHED*'
$before = Evidence-Hashes $passed
$buildsBefore = $testState.Builds
$startsBefore = $testState.Starts
$testState.Mode = 'exit-failure'
Assert-Fails { Invoke-Stage Smoke $passed } '*Smoke was already attempted*'
Assert-True ($before -eq (Evidence-Hashes $passed)) 'Duplicate attempt changed existing evidence.'
Assert-True ($testState.Builds -eq $buildsBefore -and $testState.Starts -eq $startsBefore) 'Duplicate attempt executed build or smoke.'
Write-Output 'PASS: successful attempt permits packaging; duplicate is refused before execution and preserves evidence.'

foreach ($mode in 'build-failure', 'exit-failure', 'timeout') {
    $directory = New-Case $mode
    $testState.Mode = $mode
    $pattern = if ($mode -eq 'timeout') { '*timed out*' } else { '*failed*' }
    Assert-Fails { Invoke-Stage Smoke $directory } $pattern
    Assert-True (-not (Test-Path (Join-Path $directory 'smoke.json'))) "$mode left a passing receipt."
    $before = Evidence-Hashes $directory
    Assert-Fails { Invoke-Stage Smoke $directory } '*Smoke was already attempted*'
    Assert-True ($before -eq (Evidence-Hashes $directory)) "$mode retry overwrote evidence."
    $publishesBefore = $testState.Publishes
    Assert-Fails { Invoke-Stage Package $directory } '*smoke.json*'
    Assert-True ($testState.Publishes -eq $publishesBefore) "$mode reached publication."
    Write-Output "PASS: $mode preserves evidence, refuses retry and blocks publication."
}
Assert-True $testState.Killed 'Timed-out owned process was not stopped.'

foreach ($existing in 'smoke-attempt.json', 'smoke.json', 'LoadingWpfSmoke-build.log', 'SiteUiSmoke-error.log', 'screenshots') {
    $directory = New-Case ('existing-' + $existing)
    if ($existing -eq 'screenshots') { New-Item -ItemType Directory (Join-Path $directory $existing) | Out-Null }
    else { 'Preserved earlier evidence' | Set-Content (Join-Path $directory $existing) }
    $before = Evidence-Hashes $directory
    Assert-Fails { Invoke-Stage Smoke $directory } '*Smoke was already attempted*'
    Assert-True ($before -eq (Evidence-Hashes $directory)) "Existing $existing was overwritten."
    Write-Output "PASS: interrupted/legacy evidence $existing prevents another attempt."
}
Write-Output 'Verification guard tests passed (9 scenarios); all external commands were simulated.'
