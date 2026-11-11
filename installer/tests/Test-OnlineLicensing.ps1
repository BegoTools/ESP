$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tempRoot = [IO.Path]::GetFullPath($env:TEMP)
$id = [Guid]::NewGuid().ToString('N')
$scratch = Join-Path $tempRoot "sv-license-checks-$id"
$product = "SpectraVision-LicensingChecks-$id"
$profile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) $product
$null = New-Item -ItemType Directory -Path $scratch -ErrorAction Stop
$portFile = Join-Path $scratch 'port.txt'
$originalTestKey = $env:SV_TEST_SIGNER_XML
$signer = [Security.Cryptography.RSACryptoServiceProvider]::new(2048)
$signer.PersistKeyInCsp = $false
try {
    $serverScript = Join-Path $PSScriptRoot 'license-server.mjs'
    $server = Start-Process -FilePath (Get-Command node.exe).Source -ArgumentList "`"$serverScript`" `"$portFile`"" -PassThru -WindowStyle Hidden
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $portFile) -and -not $server.HasExited -and $wait.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path -LiteralPath $portFile)) { throw 'Loopback license fixture did not start.' }
    $port = [int](Get-Content -Raw -LiteralPath $portFile)
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $releaseConfig = Join-Path $scratch 'ReleaseConfig.cs'
    $testPublicKey = $signer.ExportParameters($false)
    $modulus = [Convert]::ToBase64String($testPublicKey.Modulus)
    $exponent = [Convert]::ToBase64String($testPublicKey.Exponent)
    $source = @"
using System.Reflection;
[assembly: AssemblyVersion("2.0.0.0")]
namespace ESPWorkbench {
    internal static class ReleaseConfig {
        internal const string Origin = "http://127.0.0.1:$port/";
        internal const string Product = "$product";
        internal const string Channel = "test";
        internal const string Modulus = "$modulus";
        internal const string Exponent = "$exponent";
        internal const string LegacyModulus = "";
        internal const string LegacyExponent = "";
    }
}
"@
    [IO.File]::WriteAllText($releaseConfig, $source)
    $sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
    $target = Join-Path $scratch 'LicenseChecks.exe'
    & $compiler /nologo /main:ESPWorkbench.LicenseChecks /platform:x64 /define:DISTRIBUTION /utf8output /codepage:65001 /out:"$target" /resource:"$root\src\Translations.tsv,App.Translations.tsv" /resource:"$root\ui.png,Theme.Reference.png" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll @sourceFiles $releaseConfig (Join-Path $PSScriptRoot 'LicenseChecks.cs')
    if ($LASTEXITCODE -ne 0) { throw 'License checks could not compile.' }
    $env:SV_TEST_SIGNER_XML = $signer.ToXmlString($true)
    & $target
    if ($LASTEXITCODE -ne 0) { throw 'Windows licensing checks failed.' }
} finally {
    $env:SV_TEST_SIGNER_XML = $originalTestKey
    $signer.Dispose()
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id; $server.WaitForExit() }
    foreach ($path in @($profile,$scratch)) {
        $resolved = [IO.Path]::GetFullPath($path)
        if ([IO.Path]::GetFileName($resolved) -notin $product, "sv-license-checks-$id") { throw 'Unexpected test cleanup path.' }
        if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
