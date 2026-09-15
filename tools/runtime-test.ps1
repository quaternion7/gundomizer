[CmdletBinding()]
param(
    [ValidateSet('Start', 'Run', 'Stop')][string]$Action = 'Start',
    [string]$ProfilePath = "$env:APPDATA\r2modmanPlus-local\H3VR\profiles\Development",
    [string]$SteamPath = 'C:\Program Files (x86)\Steam\steam.exe',
    [string]$RunDirectory,
    [switch]$MeasureMods,
    [switch]$IntegrationOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$activeFile = Join-Path $repo 'temp\active-runtime.txt'

function Build-Suite([string]$Destination) {
    # Unity's Mono caches assemblies by identity, even when loaded from bytes.
    $name = 'Gundomizer.RuntimeSuite.' + [guid]::NewGuid().ToString('N')
    & dotnet build (Join-Path $repo 'tests\runtime\RuntimeSuite.csproj') -c Release "-p:AssemblyName=$name"
    if ($LASTEXITCODE -ne 0) { throw 'Runtime suite build failed.' }
    Copy-Item -LiteralPath (Join-Path $repo "tests\runtime\bin\Release\net35\$name.dll") -Destination (Join-Path $Destination 'suite.dll')
}

if ($Action -eq 'Start') {
    if (Get-Process h3vr -ErrorAction SilentlyContinue) { throw 'H3VR is already running.' }
    $profile = (Resolve-Path -LiteralPath $ProfilePath).Path
    $probe = Join-Path $profile 'BepInEx\plugins\Gundomizer.RuntimeProbe.dll'
    if (Test-Path -LiteralPath $probe) { throw 'A runtime probe is already installed; finish its session first.' }
    & dotnet build (Join-Path $repo 'tests\runtime\RuntimeProbe.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Runtime probe build failed.' }
    & (Join-Path $PSScriptRoot 'deploy-profile.ps1') -ProfilePath $profile
    if (-not $RunDirectory) { $RunDirectory = Join-Path $repo ('temp\runtime-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    if (Test-Path -LiteralPath $RunDirectory) { throw 'Use a new output directory for each launch.' }
    New-Item -ItemType Directory -Path $RunDirectory | Out-Null
    $RunDirectory = (Resolve-Path -LiteralPath $RunDirectory).Path
    $config = Join-Path $profile 'BepInEx\config\quaternion.gundomizer.cfg'
    $log = Join-Path $profile 'BepInEx\LogOutput.log'
    if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $RunDirectory 'previous-LogOutput.log') }
    if (Test-Path -LiteralPath $config) { Copy-Item -LiteralPath $config -Destination (Join-Path $RunDirectory 'quaternion.gundomizer.cfg') }
    $probeConfig = Join-Path $profile 'BepInEx\config\quaternion.gundomizer.runtimeprobe.cfg'
    @{ Profile = $profile; ProbeConfigExisted = (Test-Path -LiteralPath $probeConfig) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunDirectory 'session.json')
    Build-Suite $RunDirectory
    if ($MeasureMods -or $IntegrationOnly) { [IO.File]::WriteAllText((Join-Path $RunDirectory 'measure-mods.txt'), 'enabled') }
    if ($IntegrationOnly) { [IO.File]::WriteAllText((Join-Path $RunDirectory 'integration-only.txt'), 'enabled') }
    Copy-Item -LiteralPath (Join-Path $repo 'tests\runtime\bin\Release\net35\Gundomizer.RuntimeProbe.dll') -Destination $probe
    [IO.File]::WriteAllText($activeFile, $RunDirectory)
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'command.txt'), 'run')
    $preloader = Join-Path $profile 'BepInEx\core\BepInEx.Preloader.dll'
    Start-Process -FilePath $SteamPath -WindowStyle Hidden -ArgumentList @('-applaunch', '450540',
        '--doorstop-enable', 'true', '--doorstop-target', ('"' + $preloader + '"'), '-vrmode', 'None',
        '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720',
        '-gundomizer-probe', ('"' + $RunDirectory + '"'))
    Write-Output "Started runtime tests. Results: $RunDirectory"
    return
}

if (-not $RunDirectory) { $RunDirectory = [IO.File]::ReadAllText($activeFile).Trim() }
$RunDirectory = (Resolve-Path -LiteralPath $RunDirectory).Path
$session = Get-Content -LiteralPath (Join-Path $RunDirectory 'session.json') -Raw | ConvertFrom-Json
$game = @(Get-CimInstance Win32_Process -Filter "name = 'h3vr.exe'" | Where-Object {
    $_.CommandLine.Contains('-gundomizer-probe') -and $_.CommandLine.Contains($RunDirectory)
})
if ($Action -eq 'Run') {
    if ($game.Count -ne 1) { throw 'The matching test session is not running.' }
    Build-Suite $RunDirectory
    $marker = Join-Path $RunDirectory 'measure-mods.txt'
    if ($MeasureMods -or $IntegrationOnly) { [IO.File]::WriteAllText($marker, 'enabled') }
    elseif (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker }
    $integrationMarker = Join-Path $RunDirectory 'integration-only.txt'
    if ($IntegrationOnly) { [IO.File]::WriteAllText($integrationMarker, 'enabled') }
    elseif (Test-Path -LiteralPath $integrationMarker) { Remove-Item -LiteralPath $integrationMarker }
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'command.txt'), 'run')
    Write-Output "Queued updated suite. Results: $RunDirectory"
    return
}

if ($game.Count -eq 1) {
    [IO.File]::WriteAllText((Join-Path $RunDirectory 'command.txt'), 'quit')
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 250
        $running = Get-Process -Id $game[0].ProcessId -ErrorAction SilentlyContinue
    } while ($running -and (Get-Date) -lt $deadline)
    if ($running) { throw 'The suite is still busy or the game has not exited. Retry Stop after it finishes.' }
    Start-Sleep -Seconds 1 # Allow shutdown/log flushing to settle before profile cleanup.
}
if (Get-Process h3vr -ErrorAction SilentlyContinue) { throw 'Another H3VR process is running; profile cleanup postponed.' }
$profile = $session.Profile
$log = Join-Path $profile 'BepInEx\LogOutput.log'
if (Test-Path -LiteralPath $log) { Copy-Item -LiteralPath $log -Destination (Join-Path $RunDirectory 'LogOutput.log') }
$configBackup = Join-Path $RunDirectory 'quaternion.gundomizer.cfg'
if (Test-Path -LiteralPath $configBackup) {
    Copy-Item -LiteralPath $configBackup -Destination (Join-Path $profile 'BepInEx\config\quaternion.gundomizer.cfg')
}
$probe = Join-Path $profile 'BepInEx\plugins\Gundomizer.RuntimeProbe.dll'
if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe }
$probeConfig = Join-Path $profile 'BepInEx\config\quaternion.gundomizer.runtimeprobe.cfg'
if (-not $session.ProbeConfigExisted -and (Test-Path -LiteralPath $probeConfig)) { Remove-Item -LiteralPath $probeConfig }
Write-Output "Stopped test session, preserved logs, restored settings, and removed the probe. Results: $RunDirectory"
