[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,
    [Parameter(Mandatory = $true)]
    [string]$IlspyPath
)

$ErrorActionPreference = 'Stop'
$assemblyFile = Get-Item -LiteralPath $AssemblyPath
$ilspyFile = Get-Item -LiteralPath $IlspyPath
if ($assemblyFile.PSIsContainer -or $ilspyFile.PSIsContainer) {
    throw 'AssemblyPath and IlspyPath must identify files.'
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$referenceRoot = Join-Path $repoRoot 'reference'
$outputRoot = Join-Path $referenceRoot 'decompiled'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$typeNames = @(
    'ItemSpawnerV2', 'ItemSpawnerID', 'ItemSpawnerCategoryDefinitionsV2',
    'ItemSpawnerV2TagCatDef', 'TagType', 'IM', 'FVRObject', 'AM',
    'AmmoSpawnerV2', 'FVRFireArmRoundTagData', 'FVRFireArmRoundDisplayData',
    'FireArmRoundPropertyTag', 'FVRFireArmRound', 'FVRFireArmMagazine',
    'FVRFireArmClip', 'Speedloader', 'FVRPhysicalObject',
    'FVRPointableButton', 'FVRPointable', 'FVRFireArmMagazineReloadTrigger',
    'FVRQuickBeltSlot'
)
$toolVersion = & $ilspyFile.FullName --version
if ($LASTEXITCODE -ne 0) { throw 'Could not read ILSpy version.' }
$assemblyHash = (Get-FileHash -LiteralPath $assemblyFile.FullName -Algorithm SHA256).Hash
foreach ($typeName in $typeNames) {
    Write-Host "Extracting FistVR.$typeName"
    $result = & $ilspyFile.FullName -t "FistVR.$typeName" $assemblyFile.FullName
    if ($LASTEXITCODE -ne 0) { throw "ILSpy failed for FistVR.$typeName." }
    if (-not $result -or -not ($result -match "(class|enum) $typeName\b")) {
        throw "Expected declaration missing from extraction: FistVR.$typeName."
    }
    $result | Set-Content -LiteralPath (Join-Path $outputRoot "FistVR.$typeName.cs") -Encoding utf8
}
if ((Get-FileHash -LiteralPath $assemblyFile.FullName -Algorithm SHA256).Hash -ne $assemblyHash) {
    throw 'The source assembly changed during extraction. Run again after the game update finishes.'
}
[ordered]@{
    extractedAtUtc = [DateTime]::UtcNow.ToString('o')
    assemblyPath = $assemblyFile.FullName
    assemblySha256 = $assemblyHash
    assemblyBytes = $assemblyFile.Length
    assemblyModifiedUtc = $assemblyFile.LastWriteTimeUtc.ToString('o')
    decompiler = @($toolVersion)
    types = @($typeNames | ForEach-Object { "FistVR.$_" })
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $referenceRoot 'provenance.json') -Encoding utf8
Write-Host "Reference extraction complete: $outputRoot"
