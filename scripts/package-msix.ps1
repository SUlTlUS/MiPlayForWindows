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
$packageName = $manifestXml.Package.Identity.Name
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
            (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\makepri.exe')) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe'))
        } |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw 'Windows SDK x64 MakeAppx.exe, MakePri.exe, and SignTool.exe were not found.'
    }

    return @{
        MakeAppx = (Join-Path $candidate.FullName 'x64\makeappx.exe')
        MakePri = (Join-Path $candidate.FullName 'x64\makepri.exe')
        SignTool = (Join-Path $candidate.FullName 'x64\signtool.exe')
        Version = $candidate.Name
    }
}

function Test-UntrustedDevelopmentRoot(
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
    [System.Management.Automation.Signature]$Signature) {
    if ($Signature.Status -notin @('NotTrusted', 'UnknownError')) {
        return $false
    }

    $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
    try {
        $chain.ChainPolicy.RevocationMode =
            [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
        $chainIsTrusted = $chain.Build($Certificate)
        $chainStatuses = @($chain.ChainStatus)
        return -not $chainIsTrusted -and
            $chainStatuses.Count -gt 0 -and
            @($chainStatuses | Where-Object {
                ($_.Status -band
                    (-bnot [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot)) -ne
                    [Security.Cryptography.X509Certificates.X509ChainStatusFlags]::NoError
            }).Count -eq 0
    }
    finally {
        $chain.Dispose()
    }
}

function New-PackageLogo(
    [string]$SourcePath,
    [string]$Path,
    [int]$Width,
    [int]$Height,
    [double]$ContentScale = 0.72) {
    Add-Type -AssemblyName System.Drawing
    $source = [Drawing.Image]::FromFile($SourcePath)
    $bitmap = [Drawing.Bitmap]::new($Width, $Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([Drawing.Color]::Transparent)

        $logoSize = [Math]::Max(1, [Math]::Floor([Math]::Min($Width, $Height) * $ContentScale))
        $left = [Math]::Floor(($Width - $logoSize) / 2)
        $top = [Math]::Floor(($Height - $logoSize) / 2)
        $destination = [Drawing.Rectangle]::new($left, $top, $logoSize, $logoSize)
        $graphics.DrawImage($source, $destination)
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
        $source.Dispose()
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
    $sourceIcon = Join-Path $root 'src\DLNACast.App\Assets\MiPlay.ico'
    New-PackageLogo $sourceIcon (Join-Path $assets 'StoreLogo.png') 50 50
    New-PackageLogo $sourceIcon (Join-Path $assets 'Square44x44Logo.png') 44 44
    New-PackageLogo $sourceIcon (Join-Path $assets 'Square150x150Logo.png') 150 150
    New-PackageLogo $sourceIcon (Join-Path $assets 'Wide310x150Logo.png') 310 150

    # Windows uses target-size variants for taskbar, Start, search, Alt+Tab,
    # and other non-tile surfaces. Without both unplated theme variants it
    # synthesizes a colored backplate even when BackgroundColor is transparent.
    $targetSizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
    foreach ($targetSize in $targetSizes) {
        foreach ($suffix in @('', '_altform-unplated', '_altform-lightunplated')) {
            $targetName = "Square44x44Logo.targetsize-${targetSize}${suffix}.png"
            New-PackageLogo $sourceIcon (Join-Path $assets $targetName) `
                $targetSize $targetSize 0.9
        }
    }

    # The application PRI was compiled before the package-only visual assets
    # existed. Build a replacement from an isolated root containing that PRI
    # plus only our logos. Indexing the whole published Windows App SDK payload
    # would import its nested PRI files and produce duplicate resource entries.
    $priConfig = Join-Path $publish 'priconfig.xml'
    $stagePri = Join-Path $stage 'resources.pri'
    $priRoot = Join-Path $publish 'package-pri-root'
    $priAssets = Join-Path $priRoot 'Assets'
    New-Item -ItemType Directory -Path $priAssets -Force | Out-Null
    Copy-Item -LiteralPath $stagePri -Destination (Join-Path $priRoot 'resources.pri') -Force
    Get-ChildItem -LiteralPath $assets -Filter '*.png' | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $priAssets $_.Name) -Force
    }
    & $sdk.MakePri createconfig /cf $priConfig /dq zh-CN /o
    if ($LASTEXITCODE -ne 0) { throw "MakePri createconfig failed: $LASTEXITCODE" }
    if (Test-Path -LiteralPath $stagePri) { Remove-Item -LiteralPath $stagePri -Force }
    & $sdk.MakePri new /pr $priRoot /cf $priConfig /of $stagePri /in $packageName /o
    if ($LASTEXITCODE -ne 0) { throw "MakePri new failed: $LASTEXITCODE" }

    if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
    & $sdk.MakeAppx pack /d $stage /p $package /o
    if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed: $LASTEXITCODE" }

    if (-not $Unsigned) {
        & $sdk.SignTool sign /fd SHA256 /sha1 $CertificateThumbprint /s My $package
        if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed: $LASTEXITCODE" }
        $signature = Get-AuthenticodeSignature -LiteralPath $package
        $isUntrustedDevelopmentRoot = $null -ne $signature.SignerCertificate -and
            (Test-UntrustedDevelopmentRoot $signature.SignerCertificate $signature)
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
