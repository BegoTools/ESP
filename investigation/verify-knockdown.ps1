$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $projectPath 'build-knockdown'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Web.Extensions
[void][Reflection.Assembly]::LoadFrom((Join-Path $outputPath 'ESPWorkbench.exe'))
$options = New-Object ESPWorkbench.RenderOptions
$json = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$livePath = Join-Path $outputPath 'live-snapshot.json'
if (Test-Path -LiteralPath $livePath) {
    $snapshot = [ESPWorkbench.NativeSnapshot]::Parse([IO.File]::ReadAllText($livePath))
    $characters = @($snapshot.Actors | Where-Object { $_.Kind -eq 'Character' -or $_.Kind -eq 'TrainingTarget' })
    if (@($characters | Where-Object { $null -eq $_.IsDead -or $null -eq $_.NearDeathBreath }).Count -gt 0) { throw 'Native reflected life fields are missing' }
    [PSCustomObject]@{ NativeCharacters=$characters.Count; FieldsPresent=$true; Note='Current training scene has no actual knocked opponent.' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'live-life-check.json') -Encoding UTF8
}
$reports = foreach ($state in @('alive','knocked','revived','finished')) {
    $frame = [ESPWorkbench.Demo]::Make(0)
    $frame.Source = 'SYNTHETIC / Life-state transition: ' + $state
    $opponent = $frame.Actors[1]
    $opponent.Address = 'synthetic-opponent'
    $opponent.IsDead = $false
    $opponent.NearDeathBreath = 100
    if ($state -eq 'knocked') { $opponent.Health=0; $opponent.NearDeathBreath=70 }
    if ($state -eq 'revived') { $opponent.Health=25 }
    if ($state -eq 'finished') { $opponent.Health=0; $opponent.IsDead=$true; $opponent.NearDeathBreath=70 }
    $bitmap = New-Object System.Drawing.Bitmap(1000,620)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        [ESPWorkbench.Painter]::Draw($graphics,$bitmap.Size,$frame,$options,$true,$true)
        $bitmap.Save((Join-Path $outputPath ($state + '.png')),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose();$bitmap.Dispose() }
    [PSCustomObject]@{ State=$state; Health=$opponent.Health; IsDead=$opponent.IsDead; Breath=$opponent.NearDeathBreath; IsKnocked=$opponent.IsKnocked; Count=[ESPWorkbench.NearbyPlayers]::Count($frame,$options) }
}
$reports | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'life-transition-check.json') -Encoding UTF8
$reports | Format-Table -AutoSize
