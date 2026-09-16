[CmdletBinding()]
param(
    [string]$GameManagedPath = '<H3VR>\h3vr_Data\Managed',
    [string]$BepInExCorePath = "$env:APPDATA\r2modmanPlus-local\H3VR\profiles\Development\BepInEx\core"
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
[Reflection.Assembly]::LoadFrom((Join-Path $BepInExCorePath 'Mono.Cecil.dll')) | Out-Null
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory($GameManagedPath)
$resolver.AddSearchDirectory($BepInExCorePath)
$parameters = New-Object Mono.Cecil.ReaderParameters
$parameters.AssemblyResolver = $resolver
$pluginPath = Join-Path $repoRoot 'plugin\bin\Release\net35\quaternion.gundomizer.dll'
$module = [Mono.Cecil.ModuleDefinition]::ReadModule($pluginPath, $parameters)
$game = [Mono.Cecil.ModuleDefinition]::ReadModule((Join-Path $GameManagedPath 'Assembly-CSharp.dll'), $parameters)
try {
    $spawner = $game.GetType('FistVR.ItemSpawnerV2')
    foreach ($field in @('PMode','SMode','m_displayLevel','m_curTagGroup','m_selectedTags','WorkingItemIDs','m_curSmallPos','m_selectedID')) {
        if (-not ($spawner.Fields | Where-Object Name -EQ $field)) { throw "Missing spawner field: $field" }
    }
    foreach ($method in @('Start','RedrawSimpleCanvas','RedrawListCanvas','AddToSelectionQueue','SetSelectedID','RedrawDetailsCanvas','IncrementSpawnedGuns','BTN_Details_Spawn','ExternalSpawnFromLaserToPoint')) {
        if (-not ($spawner.Methods | Where-Object Name -EQ $method)) { throw "Missing spawner method: $method" }
    }
    $checked = 0
    foreach ($member in $module.GetMemberReferences()) {
        if ($member.DeclaringType.Scope.Name -notin @('Assembly-CSharp','UnityEngine','UnityEngine.UI','0Harmony','BepInEx','mscorlib','System','System.Core')) { continue }
        if ($null -eq $member.Resolve()) { throw "Unresolved runtime member: $member" }
        $checked++
    }
    Write-Host "PASS: $checked referenced game/Unity/BepInEx members resolve against installed runtime assemblies."
    Write-Host 'PASS: all private spawner hook members exist.'
} finally {
    $module.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}
