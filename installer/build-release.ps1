param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version = '2.0.0',
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$MinimumVersion = '0.0.0',
    [string]$Notes = 'Online license verification and cloud control.',
    [string]$MakeNsis,
    [switch]$Test,
    [switch]$Local,
    [switch]$Bridge
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (($Local -and -not $Test) -or ($Bridge -and $Test)) { throw 'Invalid release mode.' }
$channel = if ($Test) { 'test' } else { 'stable' }
$origin = if ($Local) { 'http://127.0.0.1:8787/' }
    elseif ($Test) { 'https://sv-client-api-v2-staging.ahmed-mabrouk-dev.workers.dev/' }
    else { 'https://sv-client-api-v2.ahmed-mabrouk-dev.workers.dev/' }
$product = if ($Local) { 'SpectraVision-Local' } elseif ($Test) { 'SpectraVision-Test' } else { 'SpectraVision' }
foreach ($value in @($Version, $MinimumVersion)) {
    $parts = [version]$value
    if ($parts.Major -gt 65534 -or $parts.Minor -gt 65534 -or $parts.Build -gt 65534) { throw 'Version components must be below 65535.' }
}
if ([version]$MinimumVersion -gt [version]$Version) { throw 'The minimum version cannot exceed the release version.' }
if ($Notes.Length -gt 1000) { throw 'Release notes must fit within 1000 characters.' }
if (-not $MakeNsis) {
    $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
    $MakeNsis = @(
        $command.Source
        (Join-Path $env:LOCALAPPDATA 'SpectraVisionBuildTools\nsis-3.12\makensis.exe')
        (Join-Path $([Environment]::GetFolderPath('ProgramFilesX86')) 'NSIS\makensis.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $MakeNsis) { throw 'Install NSIS or supply -MakeNsis.' }

$trusted = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'update-keys.json') | ConvertFrom-Json
$buildChannel = if ($Local) { 'local' } else { $channel }
$rootOfBuild = "release\cloud-control\$buildChannel\$Version"
$stage = Join-Path $root $rootOfBuild
$null = New-Item -ItemType Directory -Force -Path $stage
$config = Join-Path $stage 'ReleaseConfig.cs'
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
$rsa.PersistKeyInCsp = $false
$signingSecret = $env:SV_RELEASE_SIGNING_KEY

function Import-LocalKey([string]$name) {
    Add-Type -AssemblyName System.Security
    $keyPath = Join-Path $env:LOCALAPPDATA "SpectraVisionBuildKeys\$name"
    if (-not (Test-Path -LiteralPath $keyPath)) { throw "Missing DPAPI-protected $name." }
    $bytes = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($keyPath), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try { $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($bytes)) }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

try {
    $env:SV_RELEASE_SIGNING_KEY = $null
    if ($Bridge) {
        Import-LocalKey 'setup.rsa'
    } elseif ($signingSecret) {
        try { $rsa.FromXmlString($signingSecret) }
        catch { throw 'The supplied signing key is invalid.' }
    } elseif (-not $Test) {
        Import-LocalKey 'cloud-v2.rsa'
    } elseif (-not $Local) {
        Import-LocalKey 'setup-test.rsa'
    }
    $key = $rsa.ExportParameters($false)
    $modulus = [Convert]::ToBase64String($key.Modulus)
    $exponent = [Convert]::ToBase64String($key.Exponent)
    if ($rsa.PublicOnly -or $key.Modulus.Length -lt 256) { throw 'An RSA private key of at least 2048 bits is required.' }
    if (-not $Test) {
        $expected = if ($Bridge) { $trusted.legacy } else { $trusted.v2 }
        if ($modulus -cne $expected.modulus -or $exponent -cne $expected.exponent) {
            throw 'The signing key does not match the trusted public key.'
        }
    }
    $primary = if ($Test) { @{modulus=$modulus;exponent=$exponent} } else { $trusted.v2 }
    $legacy = if ($Bridge) { $trusted.legacy } else { @{modulus='';exponent=''} }
    $source = @"
using System.Reflection;
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
namespace ESPWorkbench {
    internal static class ReleaseConfig {
        internal const string Origin = "$origin";
        internal const string Product = "$product";
        internal const string Channel = "$channel";
        internal const string Modulus = "$($primary.modulus)";
        internal const string Exponent = "$($primary.exponent)";
        internal const string LegacyModulus = "$($legacy.modulus)";
        internal const string LegacyExponent = "$($legacy.exponent)";
    }
}
"@
    [IO.File]::WriteAllText($config, $source)
    & (Join-Path $root 'build.ps1') -OutputFolder "$rootOfBuild\payload" -ReleaseConfig $config
    $exe = Join-Path $stage 'payload\ESPWorkbench.exe'
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
    $compiledConfig = $assembly.GetType('ESPWorkbench.ReleaseConfig', $true)
    $flags = [Reflection.BindingFlags]'NonPublic, Static'
    if ($compiledConfig.GetField('Origin', $flags).GetRawConstantValue() -cne $origin -or
        $compiledConfig.GetField('Channel', $flags).GetRawConstantValue() -cne $channel -or
        -not $assembly.GetType('ESPWorkbench.LicenseService')) { throw 'Release build does not contain the expected online licensing configuration.' }

    $name = "SpectraVision-Setup-$Version.exe"
    $setup = Join-Path $stage $name
    & $MakeNsis /V2 "/DPRODUCT=$product" "/DVERSION=$Version" "/DSETUP_FILE=$setup" "/DPAYLOAD=$stage\payload" (Join-Path $PSScriptRoot 'SpectraVision.nsi')
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
    $file = Get-Item -LiteralPath $setup
    if ($file.Length -ge 2GB -or ($Bridge -and $file.Length -gt 25MB)) { throw 'Setup exceeds the release host size limit.' }
    $tag = "sv-$channel-v$Version"
    $location = "https://github.com/BegoTools/ESP/releases/download/$tag"
    $checksum = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
    $payload = [Text.Encoding]::UTF8.GetBytes(([ordered]@{
        version = $Version
        sha256 = $checksum
        size = $file.Length
        notes = $Notes
        channel = $channel
        minSupportedVersion = $MinimumVersion
        url = "$location/$name"
    } | ConvertTo-Json -Compress))
    $signature = $rsa.SignData($payload, 'SHA256')
    if (-not $rsa.VerifyData($payload, 'SHA256', $signature)) { throw 'Manifest signature failed verification.' }
    $manifest = [ordered]@{
        payload = [Convert]::ToBase64String($payload)
        signature = [Convert]::ToBase64String($signature)
    } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $stage 'latest.json'), $manifest)
    [IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), "$checksum  $name" + [Environment]::NewLine)
    [IO.File]::WriteAllText((Join-Path $stage 'release-metadata.json'), ([ordered]@{
        version = $Version; channel = $channel; manifestUrl = "$location/latest.json"; checksum = $checksum
    } | ConvertTo-Json))
    Write-Output "Setup: $setup"
} finally {
    $env:SV_RELEASE_SIGNING_KEY = $signingSecret
    $rsa.Dispose()
    if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config }
}
