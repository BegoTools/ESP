$ErrorActionPreference='Stop'
$inputRoot=$PSScriptRoot
$inputBuild=Join-Path $inputRoot 'build'
$inputClasses=Join-Path $inputBuild 'classes'
$inputDex=Join-Path $inputBuild 'dex'
New-Item -ItemType Directory -Force -Path $inputClasses,$inputDex | Out-Null
$r8Jar=Join-Path $inputRoot 'tools/r8.jar'
if(-not(Test-Path -LiteralPath $r8Jar)) { throw 'Place the official Google R8 compiler at tools/r8.jar first.' }
& javac --release 8 -Xlint:-options -d $inputClasses (Join-Path $inputRoot 'EspTouchMain.java')
if($LASTEXITCODE -ne 0) {throw 'Java compilation failed.'}
& java -cp $r8Jar com.android.tools.r8.D8 --min-api 26 --output $inputDex (Join-Path $inputClasses 'EspTouchMain.class')
if($LASTEXITCODE -ne 0) {throw 'DEX compilation failed.'}
$inputJar=Join-Path $inputBuild 'esp-touch.jar'
& jar --create --file $inputJar -C $inputDex classes.dex
if($LASTEXITCODE -ne 0) {throw 'JAR packaging failed.'}
Write-Output $inputJar
