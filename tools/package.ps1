[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    & dotnet build (Join-Path $repoRoot 'Gundomizer.sln') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
}
$dll = Join-Path $repoRoot 'plugin\bin\Release\net35\quaternion.gundomizer.dll'
$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'manifest.json') -Raw | ConvertFrom-Json
$version = [Reflection.AssemblyName]::GetAssemblyName($dll).Version
if ("$($version.Major).$($version.Minor).$($version.Build)" -ne $manifest.version_number) {
    throw 'Assembly and package versions differ.'
}
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'icon.png'))) { & (Join-Path $PSScriptRoot 'make-icon.ps1') }
$output = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$zip = Join-Path $output "quaternion-Gundomizer-$($manifest.version_number).zip"
# Stage in a unique workspace directory; no recursive cleanup or unrelated files are included.
$stage = Join-Path $repoRoot ('temp\package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($file in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $stage
}
Copy-Item -LiteralPath $dll -Destination $stage
Copy-Item -LiteralPath (Join-Path (Split-Path $dll) 'AssetsTools.NET.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path (Split-Path $dll) 'Gundomizer.Reader.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path (Split-Path $dll) 'Gundomizer.Reader.exe.config') -Destination $stage
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Write-Host "Package: $zip"
