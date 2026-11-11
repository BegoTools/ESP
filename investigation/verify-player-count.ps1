$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Web.Extensions
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectPath 'dist\ESPWorkbench.exe'))
$renderOptions = New-Object ESPWorkbench.RenderOptions
$snapshotPaths = @('investigation\host-native-snapshot.json','investigation\gameloop-21518\host-native-snapshot.json','investigation\gameloop-21518\render-native-snapshot.json','investigation\aim-v4\activation-state.json')
if (Test-Path -LiteralPath (Join-Path $projectPath 'build-player-count\live-snapshot.json')) { $snapshotPaths += 'build-player-count\live-snapshot.json' }
$reports = foreach ($relativePath in $snapshotPaths) {
    $snapshotPath = Join-Path $projectPath $relativePath
    $nativeSnapshot = [ESPWorkbench.NativeSnapshot]::Parse([IO.File]::ReadAllText($snapshotPath))
    $replayFrame = $nativeSnapshot.ToFrame()
    $expectedCount = @($replayFrame.Actors | Where-Object {
        ($_.Kind -eq 'TrainingTarget' -or (($_.Kind -eq 'Character' -or [String]::IsNullOrEmpty($_.Kind)) -and $_.Team -ne $replayFrame.LocalTeam)) -and
        $_.Health -gt 0 -and ($_.Position.Distance($replayFrame.Origin) / $replayFrame.UnitsPerMeter) -le [Math]::Min($renderOptions.MaxDistance,$renderOptions.Category($_.Kind).MaxDistance)
    }).Count
    $actualCount = [ESPWorkbench.NearbyPlayers]::Count($replayFrame,$renderOptions)
    if ($actualCount -ne $expectedCount -or $actualCount -eq 0) { throw "Replay failed: $relativePath expected $expectedCount, received $actualCount" }
    [PSCustomObject]@{ Snapshot=$relativePath; Count=$actualCount; TrainingTargets=@($replayFrame.Actors | Where-Object Kind -eq 'TrainingTarget').Count }
}
$reports | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectPath 'build-player-count\recorded-data-check.json') -Encoding UTF8
$reports | Format-Table -AutoSize
