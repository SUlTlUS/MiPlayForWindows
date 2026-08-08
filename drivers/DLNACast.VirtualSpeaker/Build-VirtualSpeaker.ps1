[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64'
)

$ErrorActionPreference = 'Stop'
$vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vsWhere)) {
    throw 'Visual Studio Installer (vswhere.exe) was not found.'
}

$vsPath = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if ([string]::IsNullOrWhiteSpace($vsPath)) {
    throw 'A Visual Studio installation with MSBuild was not found.'
}

$msBuild = Join-Path $vsPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msBuild)) {
    throw "64-bit MSBuild was not found at $msBuild"
}

& $msBuild (Join-Path $PSScriptRoot 'VirtualAudioDriver.sln') `
    /m /t:Rebuild "/p:Configuration=$Configuration" "/p:Platform=$Platform" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Virtual speaker build failed with exit code $LASTEXITCODE."
}

Write-Host "Package: $PSScriptRoot\$Platform\$Configuration\package"
