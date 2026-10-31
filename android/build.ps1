param([string[]]$Tasks = @(':app:testDebugUnitTest', ':app:assembleDebug'))
$ErrorActionPreference = 'Stop'
$env:JAVA_HOME = (Split-Path (Split-Path (Get-Command java.exe).Source -Parent) -Parent)
& (Join-Path $PSScriptRoot 'gradlew.bat') @Tasks --console=plain
exit $LASTEXITCODE
