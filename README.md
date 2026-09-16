# Gundomizer

Gundomizer adds compact, rounded icon buttons with a shaded sliding rainbow gradient to the bottom-left of H3VR's **Item Spawner V2**, including the **tablet version from the toolbox**, in classic and tag-search modes:

- **Dice (Randomizer)** spawns one random item from the current section, across all pages. At the category overview, it includes all subcategories; inside a subcategory, it stays within that subcategory.
- **Gun + hand (Compatible)** does the same, filtered by the object in your non-pointing hand. Hold a firearm and browse magazines, clips, speedloaders, or attachments. Installed rail adapters and attachment mounts are included. The button is gray when nothing is held.
- **Cartridge (Ammo)** rolls one enabled ammo variant for the held object's caliber, independently of the current section or tags. The joined gray **arrow** opens or closes its variant choices.

In tag-search mode, the dice and gun/hand buttons use the native results matching your selected tags, across all pages and in either grid or list view. Changing filters while an item loads cancels those rolls.

Hover a button for its tooltip. Items spawn on the spawner's native pads and become the selected entry in the details panel. The buttons spawn **one main object**, without bundled secondary items or the stock random gun's attachment pile. Accepting a selection with native **Spawn** includes any bundled secondary item.

On the toolbox tablet, use its native **stylus placement** to accept a selected item. Ammo filling also runs after successful stylus placement.

**Version 0.1.13 is a local development build.**

## Roadmap / progress

- [x] **Working prototype** — tested random and compatible buttons in the native panel.
- [x] **Faster compatible search** — reuse native results, limit new loads, and resume paused searches.
- [x] **Lightweight async indexing** — persistent startup index; rebuild changed packages and keep live fallback.
- [x] **Ammo randomizer** — held-caliber rolls, variant toggles, selection mode, and optional automatic filling.
- [x] **General spawn safety** — generic preview for missing icons and cleanup after failed initialization.
- [x] **Tag-search mode** — use selected tags across all pages, with OtherLoader support.

## Mod compatibility

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
Local package: `artifacts/quaternion-Gundomizer-0.1.13.zip`.

Please test category overview versus subcategory rolls, empty hands, magazine fit, an installed Picatinny adapter, occupied attachment mounts, hover tooltips, rapid clicks, and changing hands while an asset loads. See [the prototype plan](docs/extension-plan.md) for the full acceptance checklist.

For performance testing, compare the first compatible roll with subsequent rolls in the same section. Each compatible request logs its outcome, section/filtered counts, prefab requests/checks, requests that waited, observed load-wait time, and total time. A cached asset request may complete immediately; these are observed request timings, not a measurement of disk I/O alone. Startup indexing reads per-prefab connector facts; it does not preload the item catalog or create a pairwise compatibility matrix.

An opt-in [runtime test harness](docs/runtime-testing.md) can launch the dev profile without VR, exercise the real spawner, simulate held-object state, and capture panel images and timings. Its DLL is excluded from the release package.

## Optimization

Compatibility searches can be expensive with large mod collections: loading a candidate prefab through Unity may bring in a large asset bundle, including textures and other media. Gundomizer instead reads the serialized **connector metadata** needed to reject incompatible attachments, magazines, and clips. It does not instantiate those assets or load their textures into Unity just to index them. The held object's current mounts and the game's native fitting rules still decide the final match.

A low-priority helper builds a **persistent metadata index asynchronously from startup**, with paced work and reads limited to approximately 8 MiB/s. Keeping parsing outside Unity also avoids adding that allocation pressure to the game's garbage collector. Completed results become available while you play; unindexed or unsupported items keep the bounded live checks. Package/version and bundle fingerprints let subsequent launches reuse the cache and rebuild only affected bundles. This reduces speculative prefab loads and makes compatible rolls faster as indexing completes; see [measured results](docs/persistent-index-measurements.md).

The cache survives restarts in **`BepInEx/cache/Gundomizer` inside your active profile**. The reset option below discards and rebuilds it. See [index behavior and limits](docs/persistent-index.md) for details.

Searches pause at the configured load/time limits; click the same button to continue the original shuffled search. Paused searches expire after one minute or restart when their context changes. A selected item can still require its full bundle, and a native load already underway can continue after cancellation. Gundomizer permits only one outstanding load that it initiated.

## Mod Options

In r2modman's Config Editor, open `BepInEx/config/quaternion.gundomizer.cfg`. The plugin creates this file on its first launch.

| Section | Option | Default | What it does |
| --- | --- | --- | --- |
| General | **Spawn Item Instantly** | On | All three roll buttons spawn immediately. Off: select the result first, then use native Spawn to accept it or roll again. |
| General | **Auto Fill Held Item** | On | After an ammo roll spawns, replace existing rounds and fill matching magazines/chambers on the held item. The loose round still spawns. In selection mode, filling waits until Spawn. |
| Performance | **New Prefab Loads Per Click** | 8 | Pause after this many new prefab requests (1–64). Click again to continue. |
| Performance | **Search Seconds Per Click** | 10 | Pause after this many seconds (1–60). A shared load already started can finish in the background. |
| Performance | **Persistent Connector Index** | On | Build/reuse the background metadata cache. Restart after changing this option. |
| Performance | **Reset Metadata Indexing** | Off | Clear saved metadata once, rebuild in the background, and switch itself back off. If indexing is disabled, only clear the cache. |

Use an in-game config manager, or edit the file with H3VR closed and launch. Choosing a result without spawning never changes the held item's ammunition.

## Ammo choices

Hold a firearm, magazine, clip, speedloader, or cartridge, then open the arrow beside the cartridge button. Toggle individual named variants, or use **All**, **None**, and the page arrows. All variants start enabled; choices are remembered per caliber across panels for the current game session. Restarting resets them. Disabling every variant disables the ammo roll while leaving its choices accessible.

The popup uses native ammo names and shows native property tags such as incendiary, tracer, and armor penetrating when hovering a row. It reads metadata without loading the cartridge prefabs. Multi-caliber firearms and installed/integrated attachable firearms contribute their supported calibers. Changing held objects closes the popup; changing choices cancels a pending ammo roll. The loaded cartridge's actual caliber and class are checked again before selection/spawning.

Mods can register ammo for the ammo station/T&H without supplying standalone spawner entries. Gundomizer gives those registered rounds temporary native details entries, using a generic preview where needed, so both instant spawning and selection followed by native Spawn work. These entries do not add browser categories or saved favorites. See [tested ammo packs and the local Bubba loader repair](docs/ammo-mod-compatibility.md).

With **Auto Fill Held Item** enabled, a successful ammo spawn replaces loaded rounds and fills matching magazines, clips, speedloaders, and weapon chambers. This includes existing inserted/integrated magazines and installed weapons of the rolled caliber; other calibers stay unchanged. It does not create a missing magazine, attach a belt, or consume the loose round. If you change held items while loading, the old target is not filled. Chambers whose declared caliber differs from the rolled round are skipped. Custom loading mechanisms outside these native components may need separate support.

Available entries retain their unlock rules, and the selected round must match the exact registered ammo variant. The native tag pager is shifted slightly right to leave room for the ammo group without reducing its text or hit targets.

## Research and development

- [Current scope and original requester background](docs/scope.md)
- [Roadmap and progress checklist](docs/roadmap.md)
- [Visible strings and templates for review](docs/visible-strings.md)
- [Original code findings and source map](docs/item-spawner-v2-analysis.md)
- [Current prototype behavior and validation](docs/extension-plan.md)
- [Research provenance](docs/research-baseline.md)
- [Persistent index, cache invalidation and limits](docs/persistent-index.md)
- [Original metadata reader experiment](docs/persistent-index-research.md)

The runtime targets .NET Framework 3.5, using the same initial dependency baseline as other H3VR mods. The independent policy tests run on .NET 5. The compile-time H3VR.GameLibs package is checked against the locally installed game through `check-game-api.ps1`.

To reproduce the original reference extraction:

```powershell
.\tools\decompile-reference.ps1 `
  -AssemblyPath '<H3VR>\h3vr_Data\Managed\Assembly-CSharp.dll' `
  -IlspyPath '<tools>\ilspycmd.exe'
```

Game binaries, decompiled sources, asset inspection output, screenshots, local backups, and build products stay Git-ignored. No remote repository or public release has been created.
