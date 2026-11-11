param([string]$OutputFolder = 'dist', [string]$ReleaseConfig = '')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.x C# compiler was not found.' }
$outputPath = Join-Path $projectRoot $OutputFolder
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$releaseFlags = @()
if ($ReleaseConfig) {
    $sourcePaths += (Resolve-Path -LiteralPath $ReleaseConfig).Path
    $releaseFlags += '/define:DISTRIBUTION'
}
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 /warn:4 /resource:"$projectRoot\src\Translations.tsv,App.Translations.tsv" /resource:"$projectRoot\ui.png,Theme.Reference.png" /out:"$outputPath\ESPWorkbench.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll @releaseFlags $sourcePaths
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'profiles') -Destination $outputPath -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.ar.md') -Destination $outputPath -Force
if (Test-Path -LiteralPath (Join-Path $projectRoot 'dist\native\esp-reader')) {
    $nativeOutput = Join-Path $outputPath 'native'
    New-Item -ItemType Directory -Force -Path $nativeOutput | Out-Null
    $nativeSource = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist\native\esp-reader'))
    $nativeTarget = [IO.Path]::GetFullPath((Join-Path $nativeOutput 'esp-reader'))
    if ($nativeSource -ne $nativeTarget) { Copy-Item -LiteralPath $nativeSource -Destination $nativeTarget -Force }
}
Copy-Item -LiteralPath (Join-Path $outputPath "ESPWorkbench.exe") -Destination (Join-Path $outputPath "RAM HAX.exe") -Force
Write-Output "Built: $outputPath\RAM HAX.exe"
