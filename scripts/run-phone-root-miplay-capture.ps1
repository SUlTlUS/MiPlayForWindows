param(
    [Parameter(Mandatory = $true)]
    [string]$Serial,

    [ValidateRange(10, 1800)]
    [int]$Seconds = 180,

    [Parameter(Mandatory = $true)]
    [string]$RemotePath,

    [Parameter(Mandatory = $true)]
    [string]$LocalPath
)

$ErrorActionPreference = 'Stop'

$localDirectory = Split-Path -Parent $LocalPath
New-Item -ItemType Directory -Path $localDirectory -Force | Out-Null

$rootCheck = & adb -s $Serial shell /system/bin/su -c id
if ($LASTEXITCODE -ne 0 -or $rootCheck -notmatch 'uid=0') {
    throw "Root check failed for $Serial. Output: $rootCheck"
}

$filter = 'tcp port 8899 and host 192.168.10.4 or tcp port 8899 and host 192.168.10.7'
$remoteScript = "/data/local/tmp/dlnacast_miplay_root_capture_$(Get-Date -Format 'yyyyMMddHHmmss').sh"
$localScript = [System.IO.Path]::GetTempFileName()
Set-Content -LiteralPath $localScript -Encoding ASCII -Value @"
#!/system/bin/sh
set -u
remote_path="`$1"
seconds="`$2"
/system/bin/tcpdump -i wlan0 -s 0 -w "`$remote_path" tcp port 8899 and host 192.168.10.4 or tcp port 8899 and host 192.168.10.7 &
capture_pid=`$!
sleep "`$seconds"
kill -INT "`$capture_pid" 2>/dev/null || true
wait "`$capture_pid" 2>/dev/null || true
ls -l "`$remote_path" 2>/dev/null || true
"@

"started=$(Get-Date -Format o)"
"serial=$Serial"
"seconds=$Seconds"
"remotePath=$RemotePath"
"localPath=$LocalPath"
"filter=$filter"

try {
    & adb -s $Serial push $localScript $remoteScript
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push remote capture script to $remoteScript"
    }

    & adb -s $Serial shell /system/bin/su -c "chmod 700 $remoteScript"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to chmod remote capture script $remoteScript"
    }

    & adb -s $Serial shell /system/bin/su -c "sh $remoteScript $RemotePath $Seconds"
    $captureExitCode = $LASTEXITCODE
}
finally {
    Remove-Item -LiteralPath $localScript -Force -ErrorAction SilentlyContinue
    & adb -s $Serial shell /system/bin/su -c "rm -f $remoteScript" | Out-Null
}

& adb -s $Serial pull $RemotePath $LocalPath
$pullExitCode = $LASTEXITCODE

& adb -s $Serial shell /system/bin/su -c "ls -l $RemotePath"

"captureExitCode=$captureExitCode"
"pullExitCode=$pullExitCode"
"completed=$(Get-Date -Format o)"

if ($pullExitCode -ne 0) {
    exit $pullExitCode
}

exit 0
