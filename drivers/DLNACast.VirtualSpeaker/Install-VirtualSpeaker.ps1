[CmdletBinding()]
param(
    [string]$PackageDirectory,
    [string]$ScheduledTaskName,
    [switch]$LaunchApplication
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $PSScriptRoot 'x64\Release\package'
}
$resultPath = Join-Path $env:TEMP 'DLNACast-VirtualSpeaker-install.log'
Start-Transcript -Path $resultPath -Force | Out-Null
trap {
    Write-Error $_
    Stop-Transcript | Out-Null
    exit 1
}

$principal = [Security.Principal.WindowsPrincipal]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $argumentItems = [Collections.Generic.List[string]]@(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-PackageDirectory', ('"{0}"' -f $PackageDirectory)
    )
    if (-not [string]::IsNullOrWhiteSpace($ScheduledTaskName)) {
        $argumentItems.Add('-ScheduledTaskName')
        $argumentItems.Add(('"{0}"' -f $ScheduledTaskName))
    }
    if ($LaunchApplication) {
        $argumentItems.Add('-LaunchApplication')
    }
    $arguments = $argumentItems -join ' '
    Stop-Transcript | Out-Null
    $process = Start-Process powershell.exe -ArgumentList $arguments -Verb RunAs `
        -WindowStyle Hidden -Wait -PassThru
    if (Test-Path -LiteralPath $resultPath) {
        Get-Content -LiteralPath $resultPath -Tail 80
    }
    exit $process.ExitCode
}

$package = [IO.Path]::GetFullPath($PackageDirectory)
$inf = Join-Path $package 'VirtualAudioDriver.inf'
if (-not (Test-Path -LiteralPath $inf)) {
    throw "The built driver INF was not found at $inf. Run Build-VirtualSpeaker.ps1 first."
}

$catalog = Join-Path $package 'dlnacastvirtualspeaker.cat'
if (-not (Test-Path -LiteralPath $catalog)) {
    throw "The built driver catalog was not found at $catalog. Run Build-VirtualSpeaker.ps1 first."
}

# TESTSIGNING permits test-signed kernel binaries, but Windows still requires the
# package signer to be trusted when PnP stages the catalog. Import only the
# self-signed WDK test certificate that signed this package.
$signature = Get-AuthenticodeSignature -LiteralPath $catalog
$certificate = $signature.SignerCertificate
$certificateName = if ($null -ne $certificate) {
    $certificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)
}
if ($null -eq $certificate -or
    $certificateName -notlike 'WDKTestCert *' -or
    $certificate.Subject -ne $certificate.Issuer) {
    throw 'The driver catalog is not signed by the expected self-signed WDK test certificate.'
}

$certificatePath = Join-Path $env:TEMP 'DLNACast-VirtualSpeaker-test-certificate.cer'
try {
    [IO.File]::WriteAllBytes(
        $certificatePath,
        $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))

    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        $installedCertificate = Get-ChildItem "Cert:\LocalMachine\$storeName" |
            Where-Object Thumbprint -EQ $certificate.Thumbprint |
            Select-Object -First 1
        if ($null -eq $installedCertificate) {
            Import-Certificate -FilePath $certificatePath `
                -CertStoreLocation "Cert:\LocalMachine\$storeName" | Out-Null
            Write-Host "Trusted WDK test certificate in LocalMachine\$storeName."
        }
    }
}
finally {
    Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
}

$bootOptions = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control' `
    -Name SystemStartOptions -ErrorAction SilentlyContinue).SystemStartOptions
$restartRequired = $bootOptions -notmatch '(^|\s)TESTSIGNING(\s|$)'
if ($restartRequired) {
    try {
        if (Confirm-SecureBootUEFI) {
            throw 'Secure Boot is enabled. Disable it before enabling Windows test-signing.'
        }
    }
    catch [System.PlatformNotSupportedException] {
        # Legacy BIOS has no Secure Boot state to check.
    }

    $bitLocker = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction SilentlyContinue
    if ($null -ne $bitLocker -and $bitLocker.ProtectionStatus -eq 'On') {
        throw 'BitLocker protection is active. Suspend protection before changing test-signing boot policy.'
    }

    & bcdedit /set testsigning on
    if ($LASTEXITCODE -ne 0) {
        throw 'Windows refused to enable test-signing. Check Secure Boot and BitLocker policy.'
    }

}

$devCon = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Tools\10.0.28000.0\x64\devcon.exe'
if (-not (Test-Path -LiteralPath $devCon)) {
    throw "WDK devcon.exe was not found at $devCon"
}

& $devCon install $inf 'ROOT\DLNACastVirtualSpeaker'
if ($LASTEXITCODE -notin 0, 1) {
    throw "Driver installation failed with exit code $LASTEXITCODE."
}

if ($restartRequired) {
    Write-Host 'The driver package is staged and test-signing is enabled. Restart Windows to load the virtual speaker.'
    Stop-Transcript | Out-Null
    exit 3010
}

Start-Sleep -Seconds 2
$device = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object FriendlyName -Like '*DLNA Cast Virtual Speaker*' |
    Select-Object -First 1
if ($null -eq $device -or $device.Status -ne 'OK') {
    throw 'The driver was installed, but the DLNA Cast virtual speaker is not ready.'
}

Write-Host "Installed: $($device.FriendlyName) ($($device.InstanceId))"

if (-not [string]::IsNullOrWhiteSpace($ScheduledTaskName)) {
    Unregister-ScheduledTask -TaskName $ScheduledTaskName -Confirm:$false -ErrorAction SilentlyContinue
}

if ($LaunchApplication) {
    $application = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot `
        '..\..\src\DLNACast.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\DLNACast.App.exe'))
    if (Test-Path -LiteralPath $application) {
        Start-Process -FilePath $application
    }
}

Stop-Transcript | Out-Null
