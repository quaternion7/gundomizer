[CmdletBinding()]
param(
    [string]$ProfilePath = "$env:APPDATA\r2modmanPlus-local\H3VR\profiles\Development",
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$profile = (Resolve-Path -LiteralPath $ProfilePath).Path
if (-not (Test-Path -LiteralPath (Join-Path $profile 'BepInEx\core\BepInEx.dll'))) { throw 'Profile lacks BepInEx.' }
if (Get-Process -Name h3vr -ErrorAction SilentlyContinue) { throw 'Close H3VR before installing this build.' }
& (Join-Path $PSScriptRoot 'package.ps1') -SkipBuild:$SkipBuild
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'manifest.json') -Raw | ConvertFrom-Json
$zip = Join-Path $repoRoot "artifacts\quaternion-Gundomizer-$($manifest.version_number).zip"
$target = Join-Path $profile 'BepInEx\plugins\quaternion-Gundomizer'
$backupRoot = Join-Path $repoRoot ('temp\profile-backup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$modsPath = Join-Path $profile 'mods.yml'
$modsHash = if (Test-Path -LiteralPath $modsPath) { (Get-FileHash -LiteralPath $modsPath).Hash } else { $null }
if ($modsHash) { Copy-Item -LiteralPath $modsPath -Destination $backupRoot }
if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination $backupRoot -Recurse }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Expand-Archive -LiteralPath $zip -DestinationPath $target -Force
$parts = $manifest.version_number.Split('.')
$entry = [ordered]@{
    manifestVersion = 2; name = 'quaternion-Gundomizer'; authorName = 'quaternion'
    websiteUrl = $manifest.website_url; displayName = 'Gundomizer'; description = $manifest.description
    gameVersion = ''; networkMode = ''; packageType = ''; installMode = ''
    installedAtTime = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    loaders = @(); dependencies = @($manifest.dependencies); incompatibilities = @(); optionalDependencies = @()
    versionNumber = [ordered]@{ major = [int]$parts[0]; minor = [int]$parts[1]; patch = [int]$parts[2] }
    enabled = $true; icon = ''; onlineSource = $false
}
$entry | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $target 'mm_v2_manifest.json') -Encoding utf8

# Register the local package while preserving every other profile entry verbatim.
if ($modsHash) {
    $original = Get-Content -LiteralPath $modsPath -Raw
    $blocks = [regex]::Split($original, '(?m)(?=^- manifestVersion:)')
    $retained = @($blocks | Where-Object { $_ -notmatch '(?m)^  name: quaternion-Gundomizer\s*$' }) -join ''
    $yaml = @"
- manifestVersion: 2
  name: quaternion-Gundomizer
  authorName: quaternion
  websiteUrl: ''
  displayName: Gundomizer
  description: 'Item Spawner V2 randomizer - local prototype.'
  gameVersion: ''
  networkMode: ''
  packageType: ''
  installMode: ''
  installedAtTime: $($entry.installedAtTime)
  loaders: []
  dependencies:
    - BepInEx-BepInExPack_H3VR-5.4.1700
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: $($parts[0])
    minor: $($parts[1])
    patch: $($parts[2])
  enabled: true
  icon: ''
  onlineSource: false
"@
    if ((Get-FileHash -LiteralPath $modsPath).Hash -ne $modsHash) {
        throw 'r2modman changed the profile registry during deployment. Runtime DLL is installed; registry was not overwritten.'
    }
    [IO.File]::WriteAllText($modsPath, $retained.TrimEnd() + "`r`n" + $yaml + "`r`n", (New-Object Text.UTF8Encoding $false))
}
$sourceHash = (Get-FileHash -LiteralPath (Join-Path $repoRoot 'plugin\bin\Release\net35\quaternion.gundomizer.dll')).Hash
$installedHash = (Get-FileHash -LiteralPath (Join-Path $target 'quaternion.gundomizer.dll')).Hash
if ($sourceHash -ne $installedHash) { throw 'Installed DLL hash differs from the build.' }
Write-Host "Installed Gundomizer $($manifest.version_number): $target"
Write-Host "Verified SHA-256: $installedHash"
Write-Host "Profile backup: $backupRoot"
