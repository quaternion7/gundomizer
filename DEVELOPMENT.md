# Development

Build with a .NET SDK supporting the project's .NET Framework targets. The standalone tests target .NET 5. Game reference packages restore through NuGet; don't commit installed game assemblies or decompiled source.

```powershell
dotnet build Gundomizer.sln -c Release
dotnet run --project tests/Gundomizer.Tests.csproj -c Release
dotnet run --project tests/indexing/Indexing.Tests.csproj -c Release
.\tools\package.ps1 -SkipBuild
```

The package is written to the ignored `artifacts/` directory. Local paths are explicit parameters:

```powershell
.\tools\check-game-api.ps1 -GameManagedPath '<H3VR>/h3vr_Data/Managed' -BepInExCorePath '<profile>/BepInEx/core'
.\tools\deploy-profile.ps1 -ProfilePath '<profile>' -SkipBuild
```

Optional runtime tests launch H3VR without a headset, install a temporary test probe, and retain results under ignored `temp/`. Use a development profile and close the game first. `Stop` closes the test session, removes its probe and restores saved settings.

```powershell
.\tools\runtime-test.ps1 -Action Start -ProfilePath '<profile>' -SteamPath '<Steam>/steam.exe' -IndexChecks
.\tools\runtime-test.ps1 -Action Stop
```

Other suites: `-ColdIndexChecks`, `-SearchChecks`, `-AmmoFillChecks`, `-AmmoModChecks`, and `-IntegrationOnly`. Runtime tests and tools are excluded from the distributable mod package. Keep personal notes and machine settings in ignored `.local/`.

`-PatchedLoaderChecks` adds OtherLoaderPatched category and spawn checks to the normal suite; install OtherLoaderPatched and MLOK Rails in the test profile. Also run the normal suite with original OtherLoader and with neither loader enabled.
