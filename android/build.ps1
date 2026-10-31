param([string[]]$Tasks = @(':app:testDebugUnitTest', ':app:assembleDebug'))
$ErrorActionPreference = 'Stop'
$env:JAVA_HOME = (Split-Path (Split-Path (Get-Command java.exe).Source -Parent) -Parent)
if (-not $env:ANDROID_HOME) { $env:ANDROID_HOME = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
& (Join-Path $PSScriptRoot 'gradlew.bat') @Tasks --console=plain
exit $LASTEXITCODE
