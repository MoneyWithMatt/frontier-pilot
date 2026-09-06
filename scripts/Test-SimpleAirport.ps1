param([string]$SdkRoot = 'C:\Users\mattr\Desktop\MSFS 2024 SDK', [ValidateRange(1,3600)][int]$TimeoutSeconds = 180)
# Untouched baseline only. The diagnostic script can run a separately labelled compatibility experiment.
& "$PSScriptRoot\Diagnose-SampleBuild.ps1" -SdkRoot $SdkRoot -TimeoutSeconds $TimeoutSeconds -CorrectSdkEnvironment
