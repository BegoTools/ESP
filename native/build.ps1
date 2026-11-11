$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$projectRoot = Split-Path -Parent $nativeRoot
$toolchainRoot = (& rustc --print sysroot).Trim()
$linkerPath = Join-Path $toolchainRoot 'lib\rustlib\x86_64-pc-windows-msvc\bin\rust-lld.exe'
$outputDirectory = Join-Path $projectRoot 'dist\native'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
& rustc --edition=2021 --target x86_64-unknown-linux-musl -C opt-level=3 -C strip=symbols -C panic=abort -C linker-flavor=ld.lld -C "linker=$linkerPath" -o (Join-Path $outputDirectory 'esp-reader') (Join-Path $nativeRoot 'reader.rs')
if ($LASTEXITCODE -ne 0) { throw 'Native reader compilation failed.' }
Write-Output "Built: $outputDirectory\esp-reader"
