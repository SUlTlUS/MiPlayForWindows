param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,

    [ValidateRange(10, 300)]
    [int]$IntervalSeconds = 30
)

$ErrorActionPreference = 'SilentlyContinue'

while ($true) {
    & adb -s $Serial shell input keyevent 224 | Out-Null
    Start-Sleep -Seconds $IntervalSeconds
}
