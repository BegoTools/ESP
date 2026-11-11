param([switch]$Provision, [switch]$Copy)
$ErrorActionPreference = 'Stop'
if (-not $Provision -and -not $Copy) { throw 'Use -Provision for the initial deployment or -Copy to sign in to the admin panel.' }
$folder = Join-Path $env:LOCALAPPDATA 'SpectraVisionBuildKeys'
$path = Join-Path $folder 'admin.dpapi'
Add-Type -AssemblyName System.Security
$scope = [System.Security.Cryptography.DataProtectionScope]::CurrentUser
if (-not (Test-Path -LiteralPath $path)) {
    if (-not $Provision) { throw 'The admin key has not been provisioned on this Windows account.' }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = [byte[]]::new(32)
        $random.GetBytes($bytes)
        $encrypted = [System.Security.Cryptography.ProtectedData]::Protect($bytes, $null, $scope)
        $file = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $file.Write($encrypted, 0, $encrypted.Length) } finally { $file.Dispose() }
        [Array]::Clear($bytes, 0, $bytes.Length)
    } finally { $random.Dispose() }
}
$bytes = [System.Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path), $null, $scope)
try {
    $key = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
    if ($Provision) {
        Push-Location $PSScriptRoot
        try {
            $key | & npx.cmd --yes wrangler@4.158.0 secret put ADMIN_TOKEN
            if ($LASTEXITCODE -ne 0) { throw 'Cloudflare did not save the admin key.' }
        } finally { Pop-Location }
    }
    if ($Copy) {
        Set-Clipboard -Value $key
        Write-Output 'Admin key copied to the Windows clipboard.'
    }
} finally { [Array]::Clear($bytes, 0, $bytes.Length) }
