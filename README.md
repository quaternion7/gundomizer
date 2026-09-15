# Gundomizer

Gundomizer adds compact, rounded icon buttons with a shaded sliding rainbow gradient to the bottom-left of H3VR's **Item Spawner V2**, in classic and tag-search modes:

- **Dice (Randomizer)** spawns one random item from the current section, across all pages. At the category overview, it includes all subcategories; inside a subcategory, it stays within that subcategory.
- **Gun + hand (Compatible)** does the same, filtered by the object in your non-pointing hand. Hold a firearm and browse magazines, clips, speedloaders, or attachments. Installed rail adapters and attachment mounts are included. The button is gray when nothing is held.
- **Cartridge (Ammo)** rolls one enabled ammo variant for the held object's caliber, independently of the current section or tags. The joined gray **arrow** opens or closes its variant choices.

In tag-search mode, the dice and gun/hand buttons use the native results matching your selected tags, across all pages and in either grid or list view. Changing filters while an item loads cancels those rolls.

Hover a button for its tooltip. Items spawn on the spawner's native pads and become the selected entry in the details panel. The buttons spawn **one main object**, without bundled secondary items or the stock random gun's attachment pile. Accepting a selection with native **Spawn** includes any bundled secondary item.

**Version 0.1.10 is a local prototype.** Searches limit new prefab loads and can resume after pausing. Plain random selection uses existing artwork without loading the item. A small background index reads already-loaded components without requesting assets or retaining their prefabs. See [the completion roadmap](docs/roadmap.md).

When OtherLoader is installed, Gundomizer uses its object IDs, classic category tree, unlock state, and bundled spawn list. This keeps native and modded items in the same browser pool. The optional integration was tested with OtherLoader 1.3.7, Modul PM, Modul XM8, G36 Extras, and FN F2000; see [the measurements and remaining loading costs](docs/modded-profile-measurements.md).

Missing preview artwork uses a generic icon and keeps the item selectable. There is no sight-specific exclusion: if a prefab logs an exception during synchronous cloning or activation, Gundomizer removes the failed instance, leaves its entry selected, and shows an error. This also covers the native **Spawn** button for entries selected through Gundomizer on that panel, while preserving bundled secondary items. It does not repair faulty components or cover later callbacks and other spawning tools. The [HAM incident analysis](docs/incidents/2026-09-15-ham-scope.md) records the current-profile reproduction; vanilla failure has not been established.

## Compatible means

- Magazines: native magazine-well connector/type overrides, belt boxes, and secondary/attachable wells. An occupied well can still match a spare magazine for use after unloading.
- Clips: native clip-well connector type.
- Speedloaders: explicit authored compatibility plus matching chamber ammunition type. Unknown combinations are excluded.
- Attachments: matching connectors and the game's current mount-acceptance checks, including installed adapters, mount capacity, and built-in suppressor/bipod restrictions. Hidden or disabled mounts are excluded. The item is spawned loose; it is not automatically attached.
- Holding a magazine, clip, speedloader, or attachment can also filter compatible firearms in the Firearms section.

Compatibility is determined from the game's native rules and available item data. Mods with extra custom fitting rules or incomplete metadata need in-game validation; unsupported matches are skipped rather than guessed. A compatible roll with no matches displays a message and spawns nothing. Changing the section or held target during loading cancels the roll.

Hover tooltips describe the current section or selected-tag scope, and the compatible tooltip names the held object.

Held-item context refreshes **once per second**, only for visible browsing panels within **8 metres** of the player's VR head. Readiness and tooltips use that cache; hand changes can take up to one second to appear. Compatible clicks and the final selection/spawn step check live context again. A click can therefore use a newly picked-up item immediately, and a pending roll cannot commit against a stale target. Detailed compatibility searches run only for a requested roll. The rainbow continues animating smoothly each frame.

## Installation and testing

Requires `BepInEx-BepInExPack_H3VR-5.4.1700`. Import the local package into r2modman, or deploy it to an existing profile with the script below. Restart H3VR after installing; the plugin does not hot-load into an already running game.

```powershell
dotnet build .\Gundomizer.sln -c Release
dotnet run --project .\tests\Gundomizer.Tests.csproj -c Release
.\tools\check-game-api.ps1
.\tools\deploy-profile.ps1 -SkipBuild
```

The default deployment profile is **Development**. The deployment script creates the local ZIP, backs up this mod's prior files and the profile registry, installs only Gundomizer, and verifies the installed DLL hash. If r2modman was already displaying the profile, reselect it to refresh the mod list.

Build output: `plugin/bin/Release/net35/quaternion.gundomizer.dll`.
Local package: `artifacts/quaternion-Gundomizer-0.1.10.zip`.

Please test category overview versus subcategory rolls, empty hands, magazine fit, an installed Picatinny adapter, occupied attachment mounts, hover tooltips, rapid clicks, and changing hands while an asset loads. See [the prototype plan](docs/extension-plan.md) for the full acceptance checklist.

For performance testing, compare the first compatible roll with subsequent rolls in the same section. Each compatible request logs its outcome, section/filtered counts, prefab requests/checks, requests that waited, observed load-wait time, and total time. A cached asset request may complete immediately; these are observed request timings, not a measurement of disk I/O alone. This version does not preload the item catalog or create a startup compatibility matrix.

An opt-in [runtime test harness](docs/runtime-testing.md) can launch the dev profile without VR, exercise the real spawner, simulate held-object state, and capture panel images and timings. Its DLL is excluded from the release package.

For large mod collections, **Performance > New Prefab Loads Per Click** defaults to 8 and **Search Seconds Per Click** to 10 seconds. At either limit the search pauses; click the same button to continue its original shuffled order. Paused searches expire after one minute or restart when their filters, held assembly, ammo choices, or spawn setting change. A pause is not an empty compatibility result. Cancelled native loads can continue in the background; Gundomizer permits only one outstanding load that it initiated. One chosen prefab can still require a large bundle, and Unity's asset-loading work itself is outside this mod's frame budget.

## Settings

In r2modman's Config Editor, open `BepInEx/config/quaternion.gundomizer.cfg`. The plugin creates this file on its first launch.

**General > Spawn Item Instantly** defaults to **true** and applies to all three roll buttons:

- **true:** immediately spawn one main item and select its entry, as before.
- **false:** only select the random entry in the native details panel and history. Use the native **Spawn** button to accept it, or click a randomizer button again to reroll. No object is created and no spawn pad or firearm counter advances during selection.

Both modes keep the same section scope and compatibility checks. Each roll uses the setting from when it was clicked. Edit the config while H3VR is closed, then launch the game. Accepting a selection with native Spawn includes any bundled secondary items and applies initialization-failure cleanup.

## Ammo choices

Hold a firearm, magazine, clip, speedloader, or cartridge, then open the arrow beside the cartridge button. Toggle individual named variants, or use **All**, **None**, and the page arrows. All variants start enabled; choices are remembered per caliber across panels for the current game session. Restarting resets them. Disabling every variant disables the ammo roll while leaving its choices accessible.

The popup uses native ammo names and shows native property tags such as incendiary, tracer, and armor penetrating when hovering a row. It reads metadata without loading the cartridge prefabs. Multi-caliber firearms and installed/integrated attachable firearms contribute their supported calibers. Changing held objects closes the popup; changing choices cancels a pending ammo roll. The loaded cartridge's actual caliber and class are checked again before selection/spawning.

Only unlocked variants with an exact native item-spawner entry are listed, so selection mode never silently substitutes another round. Custom ammo missing that registration is currently omitted. The native tag pager is shifted slightly right to leave room for the ammo group without reducing its text or hit targets.

## Research and development

- [Current scope and original requester background](docs/scope.md)
- [Roadmap and progress checklist](docs/roadmap.md)
- [Original code findings and source map](docs/item-spawner-v2-analysis.md)
- [Current prototype behavior and validation](docs/extension-plan.md)
- [Research provenance](docs/research-baseline.md)
- [Persistent metadata index experiment](docs/persistent-index-research.md)

The runtime targets .NET Framework 3.5, using the same initial dependency baseline as other H3VR mods. The independent policy tests run on .NET 5. The compile-time H3VR.GameLibs package is checked against the locally installed game through `check-game-api.ps1`.

To reproduce the original reference extraction:

```powershell
.\tools\decompile-reference.ps1 `
  -AssemblyPath '<H3VR>\h3vr_Data\Managed\Assembly-CSharp.dll' `
  -IlspyPath '<tools>\ilspycmd.exe'
```

Game binaries, decompiled sources, asset inspection output, screenshots, local backups, and build products stay Git-ignored. No remote repository or public release has been created.
