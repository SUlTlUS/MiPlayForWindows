param(
    [Parameter(Mandatory = $true)]
    [string]$Address,

    [ValidateRange(10, 1800)]
    [int]$Seconds = 1800,

    [Parameter(Mandatory = $true)]
    [string]$StdoutPath,

    [Parameter(Mandatory = $true)]
    [string]$StderrPath
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$probePath = Join-Path $repositoryRoot 'tools\DLNACast.Probe\bin\Release\net10.0-windows10.0.22000.0\DLNACast.Probe.exe'
if (-not (Test-Path -LiteralPath $probePath)) {
    throw "Probe executable not found: $probePath"
}

$stdoutDirectory = Split-Path -Parent $StdoutPath
$stderrDirectory = Split-Path -Parent $StderrPath
New-Item -ItemType Directory -Path $stdoutDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stderrDirectory -Force | Out-Null

"runnerPid=$PID started=$(Get-Date -Format o)" | Add-Content -LiteralPath $StdoutPath -Encoding utf8

& $probePath `
    "--miplay-passive-sender-capture=$Address" `
    '--miplay-confirm-passive-sender-capture' `
    "--miplay-capture-seconds=$Seconds" `
    1>> $StdoutPath `
    2>> $StderrPath

exit $LASTEXITCODE
