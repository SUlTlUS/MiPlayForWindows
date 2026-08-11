[CmdletBinding()]
param(
    [switch]$Unsigned,
    [string]$CertificateThumbprint,
    [SecureString]$ExportPassword,
    [ValidateSet('Both', 'WithDotNet', 'WithoutDotNet')]
    [string]$RuntimeMode = 'Both'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$signing = Join-Path $artifacts 'signing'
$project = Join-Path $root 'src\DLNACast.App\DLNACast.App.csproj'
$manifest = Join-Path $root 'packaging\AppxManifest.xml'
[xml]$manifestXml = Get-Content -LiteralPath $manifest -Raw
$packageVersion = $manifestXml.Package.Identity.Version

$variants = @()
if ($RuntimeMode -in @('Both', 'WithDotNet')) {
    $variants += [pscustomobject]@{
        Name = 'with-dotnet'
        SelfContained = $true
    }
}
if ($RuntimeMode -in @('Both', 'WithoutDotNet')) {
    $variants += [pscustomobject]@{
        Name = 'without-dotnet'
        SelfContained = $false
    }
}

function Reset-ArtifactDirectory([string]$Path) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = $artifacts.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a path outside artifacts: $fullPath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
}

function Find-WindowsSdkTools {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem -LiteralPath $kitsRoot -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [Version]$_.Name } -Descending |
        Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makeappx.exe')) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe'))
        } |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw 'Windows SDK x64 MakeAppx.exe and SignTool.exe were not found.'
    }

    return @{
        MakeAppx = (Join-Path $candidate.FullName 'x64\makeappx.exe')
        SignTool = (Join-Path $candidate.FullName 'x64\signtool.exe')
        Version = $candidate.Name
    }
}

function New-Logo([string]$Path, [int]$Width, [int]$Height) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [Drawing.Bitmap]::new($Width, $Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.Color]::FromArgb(15, 23, 42))
        $diameter = [Math]::Min($Width, $Height) * 0.68
        $left = ($Width - $diameter) / 2
        $top = ($Height - $diameter) / 2
        $brush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(37, 99, 235))
        $font = [Drawing.Font]::new('Segoe UI', [Math]::Max(10, $diameter * 0.42), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
        $textBrush = [Drawing.SolidBrush]::new([Drawing.Color]::White)
        try {
            $graphics.FillEllipse($brush, $left, $top, $diameter, $diameter)
            $format = [Drawing.StringFormat]::new()
            $format.Alignment = [Drawing.StringAlignment]::Center
            $format.LineAlignment = [Drawing.StringAlignment]::Center
            $graphics.DrawString('D', $font, $textBrush, [Drawing.RectangleF]::new(0, 0, $Width, $Height), $format)
            $format.Dispose()
        }
        finally {
            $textBrush.Dispose()
            $font.Dispose()
            $brush.Dispose()
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$sdk = Find-WindowsSdkTools
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

if (-not $Unsigned) {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        if ($null -eq $ExportPassword) {
            $ExportPassword = Read-Host 'Password for the development PFX under artifacts/signing' -AsSecureString
        }

        New-Item -ItemType Directory -Path $signing -Force | Out-Null
        $certificate = New-SelfSignedCertificate `
            -Type Custom `
            -Subject 'CN=DLNACast Development' `
            -FriendlyName 'DLNA Cast for Windows development signing' `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyUsage DigitalSignature `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -NotAfter (Get-Date).AddYears(1) `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
        $CertificateThumbprint = $certificate.Thumbprint
        Export-Certificate -Cert $certificate -FilePath (Join-Path $signing 'DLNACast.Development.cer') -Force | Out-Null
        Export-PfxCertificate -Cert $certificate -FilePath (Join-Path $signing 'DLNACast.Development.pfx') -Password $ExportPassword -Force | Out-Null
    }

}

$packages = [Collections.Generic.List[string]]::new()
foreach ($variant in $variants) {
    $publish = Join-Path $artifacts "publish\win-x64-$($variant.Name)"
    $stage = Join-Path $artifacts "msix-stage-$($variant.Name)"
    $package = Join-Path $artifacts "DLNACast.Windows_${packageVersion}_x64_$($variant.Name).msix"
    $selfContained = $variant.SelfContained.ToString().ToLowerInvariant()

    Reset-ArtifactDirectory $publish
    Reset-ArtifactDirectory $stage

    & dotnet publish $project -c Release -r win-x64 --self-contained $selfContained `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish ($($variant.Name)) failed: $LASTEXITCODE"
    }

    Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force

    # `dotnet publish` omits the app's compiled WinUI resources for an unpackaged
    # project. A manually assembled MSIX still needs the root resources.pri and
    # XBF payloads so ms-appx:///MainWindow.xaml resolves under package identity.
    $buildRoot = Join-Path $root 'src\DLNACast.App\bin\Release'
    $applicationPri = Get-ChildItem -LiteralPath $buildRoot -Filter 'DLNACast.App.pri' -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($null -eq $applicationPri) {
        throw 'The compiled WinUI application PRI was not produced by dotnet publish.'
    }
    Copy-Item -LiteralPath $applicationPri.FullName -Destination (Join-Path $stage 'resources.pri') -Force
    Get-ChildItem -LiteralPath $applicationPri.DirectoryName -Filter '*.xbf' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $_.Name) -Force
    }

    Copy-Item -LiteralPath $manifest -Destination (Join-Path $stage 'AppxManifest.xml')
    Copy-Item -LiteralPath (Join-Path $root 'packaging\THIRD-PARTY-NOTICES.txt') -Destination $stage

    $assets = Join-Path $stage 'Assets'
    New-Item -ItemType Directory -Path $assets -Force | Out-Null
    New-Logo (Join-Path $assets 'StoreLogo.png') 50 50
    New-Logo (Join-Path $assets 'Square44x44Logo.png') 44 44
    New-Logo (Join-Path $assets 'Square150x150Logo.png') 150 150
    New-Logo (Join-Path $assets 'Wide310x150Logo.png') 310 150

    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
    & $sdk.MakeAppx pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed: $LASTEXITCODE" }

    if (-not $Unsigned) {
        & $sdk.SignTool sign /fd SHA256 /sha1 $CertificateThumbprint /s My $package
        if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed: $LASTEXITCODE" }
        $signature = Get-AuthenticodeSignature -LiteralPath $package
        $isUntrustedDevelopmentRoot = $signature.Status -in @('NotTrusted', 'UnknownError') -and
            $signature.StatusMessage -match 'root certificate.*not trusted'
        if ($null -eq $signature.SignerCertificate -or
            $signature.SignerCertificate.Thumbprint -ne $CertificateThumbprint -or
            $signature.SignerCertificate.Subject -ne 'CN=DLNACast Development' -or
            ($signature.Status -ne 'Valid' -and -not $isUntrustedDevelopmentRoot)) {
            throw "MSIX signature verification failed: $($signature.Status) $($signature.StatusMessage)"
        }
        if ($isUntrustedDevelopmentRoot) {
            Write-Warning 'The package signature is present, but the development certificate is not trusted yet. Install the exported CER into Local Machine/Trusted People from an elevated PowerShell before sideloading.'
        }
    }

    $packages.Add($package)
}

Write-Host "Windows SDK: $($sdk.Version)"
foreach ($package in $packages) {
    Write-Host "MSIX: $package"
}
if (-not $Unsigned) {
    Write-Host "Test certificate: $(Join-Path $signing 'DLNACast.Development.cer')"
}
