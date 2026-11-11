param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version = '1.0.0',
    [string]$Notes = 'License activation and in-app updates.',
    [string]$MakeNsis,
    [switch]$Test,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Test -and $Publish) { throw 'Test builds cannot be published.' }
$origin = if ($Test) { 'http://127.0.0.1:8787/' } else { 'https://spectravision-api.ahmed-mabrouk-dev.workers.dev/' }
$product = if ($Test) { 'SpectraVision-Test' } else { 'SpectraVision' }
$channel = if ($Test) { 'setup-test' } else { 'setup' }
$stage = Join-Path $root "release\$channel\$Version"
$assets = if ($Test) { Join-Path $root 'licensing\test-assets\updates' } else { Join-Path $root 'licensing\public\updates' }
$versionParts = [version]$Version
if ($versionParts.Major -gt 65534 -or $versionParts.Minor -gt 65534 -or $versionParts.Build -gt 65534) { throw 'Version components must be below 65535.' }
if (-not $MakeNsis) {
    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    if ($command) { $MakeNsis = $command.Source }
    else {
        $MakeNsis = @(
            (Join-Path $env:LOCALAPPDATA 'SpectraVisionBuildTools\nsis-3.12\makensis.exe')
            (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe')
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $MakeNsis) { throw 'Install NSIS or pass -MakeNsis with the full path to makensis.exe.' }
New-Item -ItemType Directory -Force -Path $stage,$assets | Out-Null
$keys = Join-Path $env:LOCALAPPDATA 'SpectraVisionBuildKeys'
New-Item -ItemType Directory -Force -Path $keys | Out-Null
Add-Type -AssemblyName System.Security
$scope = [System.Security.Cryptography.DataProtectionScope]::CurrentUser
$keyPath = Join-Path $keys "$channel.rsa"
$config = Join-Path $stage 'ReleaseConfig.cs'
$rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(3072)
$rsa.PersistKeyInCsp = $false
try {
    if (Test-Path -LiteralPath $keyPath) {
        $privateKey = [System.Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyPath), $null, $scope)
        $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($privateKey))
        [Array]::Clear($privateKey, 0, $privateKey.Length)
    } else {
        if (-not $Test -and (Get-ChildItem -LiteralPath $assets -Filter 'latest.json')) { throw 'The release signing key is missing. Restore it before building.' }
        $privateKey = [Text.Encoding]::UTF8.GetBytes($rsa.ToXmlString($true))
        $encrypted = [System.Security.Cryptography.ProtectedData]::Protect($privateKey, $null, $scope)
        $file = [IO.File]::Open($keyPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $file.Write($encrypted, 0, $encrypted.Length) } finally { $file.Dispose() }
        [Array]::Clear($privateKey, 0, $privateKey.Length)
    }
    $publicKey = $rsa.ExportParameters($false)
    $modulus = [Convert]::ToBase64String($publicKey.Modulus)
    $exponent = [Convert]::ToBase64String($publicKey.Exponent)
    $source = @"
using System.Reflection;
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
namespace ESPWorkbench {
    internal static class ReleaseConfig {
        internal const string Origin = "$origin";
        internal const string Product = "$product";
        internal const string Modulus = "$modulus";
        internal const string Exponent = "$exponent";
    }
}
"@
    [IO.File]::WriteAllText($config, $source)
    & (Join-Path $root 'build.ps1') -OutputFolder "release\$channel\$Version\payload" -ReleaseConfig $config
    $name = "SpectraVision-Setup-$Version.exe"
    $setup = Join-Path $stage $name
    & $MakeNsis /V2 "/DPRODUCT=$product" "/DVERSION=$Version" "/DSETUP_FILE=$setup" "/DPAYLOAD=$stage\payload" (Join-Path $PSScriptRoot 'SpectraVision.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    $file = Get-Item -LiteralPath $setup
    if ($file.Length -gt 25MB) { throw 'Setup exceeds the Cloudflare Static Assets file limit.' }
    $payload = [Text.Encoding]::UTF8.GetBytes((@{
        version = $Version
        sha256 = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $file.Length
        notes = $Notes
    } | ConvertTo-Json -Compress))
    $signature = $rsa.SignData($payload, 'SHA256')
    $manifest = @{
        payload = [Convert]::ToBase64String($payload)
        signature = [Convert]::ToBase64String($signature)
    } | ConvertTo-Json -Compress
    Copy-Item -LiteralPath $setup -Destination (Join-Path $assets $name) -Force
    [IO.File]::WriteAllText((Join-Path $assets 'latest.json'), $manifest)
    Write-Output "Setup: $setup"
    if ($Publish) {
        Push-Location (Join-Path $root 'licensing')
        try {
            & npx.cmd --yes wrangler@4.158.0 deploy
            if ($LASTEXITCODE -ne 0) { throw 'Cloudflare deployment failed.' }
        } finally { Pop-Location }
    }
} finally {
    $rsa.Dispose()
    if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config }
}
