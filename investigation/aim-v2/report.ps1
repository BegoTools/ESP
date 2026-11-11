$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
[void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '../../build-aim-v2/ESPWorkbench.exe'))
$snapshot=[ESPWorkbench.NativeSnapshot]::Parse([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'before.json')))
$frame=$snapshot.ToFrame()
$options=New-Object ESPWorkbench.AimOptions
$options.Radius=1200
$options.Players=$false
$candidate=[ESPWorkbench.AimEngine]::Select($frame,(New-Object Drawing.Size 1758,988),$options,$null)
[pscustomobject]@{Camera=$frame.CameraRotation;Training=@($frame.Actors | Where-Object Kind -eq TrainingTarget).Count;Candidate=$candidate} | ConvertTo-Json -Depth 5
