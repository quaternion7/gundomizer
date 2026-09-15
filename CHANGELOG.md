# Changelog

## 0.1.6 — compatible ammo and variant choices

- Add an ammo-roll button with an adjacent popup toggle in both viewer modes. Use the held object's caliber independently of the browser category or tags.
- Populate named variants and property descriptions from the native ammo catalog without requesting prefabs. Include firearms, magazines, clips, speedloaders, cartridges, and installed/integrated attachable firearms.
- Add individual variant toggles, All/None, and pagination. Remember choices per caliber for the game session; disable rolling when all choices are off.
- Respect Spawn Item Instantly, validate the loaded cartridge's actual caliber/class, and cancel a pending roll when its choices or held target change.
- Move the native tag pager slightly right to fit the new group at the existing button row; preserve its text, size, and callbacks.

## 0.1.5 — tag-search support

- Show the existing buttons in the same position in classic and tag-search modes, including grid and text lists.
- Reuse the complete native tag-filtered result set across all pages. Cancel pending rolls when the page, mode, or selected tags change.
- Keep compatible filtering, instant-spawn/selection behavior, and nearby 1 Hz hand detection in both modes.

## 0.1.4 — compatibility query reuse and timings

- Preserve the user's revised tooltips, selection message, and config description in a committed baseline.
- Capture the held assembly's mounts, wells, and loader requirements once per roll and reuse them across candidate checks. Rebuild for the final live compatibility check.
- Retain unknown connector metadata for native validation and keep reverse firearm matches. No startup scan or eager prefab loading is introduced.
- Log compatible-search outcome, candidate counts, requested/checked prefabs, observed load waits, and total duration to guide the next indexing step.
- Record the staged completion plan and native ammo, asset-loading, tag-filtering, and presentation findings.

## 0.1.3 — nearby held-item polling

- Poll held-item context once per second for each visible classic spawner, only within 8 metres measured from the player's VR head to that spawner.
- Use cached state for compatible-button readiness, hover text, and pending asset loads; distant and hidden panels do not scan hands or resolve grips.
- Refresh context on a compatible click and immediately before selecting/spawning so the polling delay cannot accept a stale held item. Newly picked-up items can be used before the next scheduled poll.
- Clarify the HAM evidence: the item is registered for spawning; a vanilla failure has not been reproduced. Preserve the guard as a conservative exclusion of the observed missing references.

## 0.1.2 — optional selection before spawning

- Add the **Spawn Item Instantly** boolean setting under General, enabled by default, for both randomizer buttons.
- Disable it to select a random entry in the native details panel and history without creating an object or advancing spawn pads/counters. Use native Spawn to accept the result, or roll again.
- Snapshot the setting at the start of each roll. Category, compatibility, cancellation, and malformed-prefab checks apply in both modes.

## 0.1.1 — compact controls and malformed sight guard

- Replace whole-button hue cycling with a repeat-wrapped rainbow texture sliding across each button.
- Use a dice icon and paired gun/hand icons; keep full-height VR hit targets while reducing width.
- Enlarge tooltip type from 34 to 52 canvas units, widen the panel, and let it grow with wrapped text.
- Skip active reflex-sight prefabs missing native initialization references, including the shipped HAM combo scope's missing UI spawn point. Log the item and missing field before instantiation.
- Preserve the incident logs and document the native initialization failure and the additional installed sight patch.
- First playtest confirmed basic random/compatible spawning and tooltips. Updated build, regression, and API checks pass; revised VR visuals await testing.

## 0.1.0 — local prototype

- Adds animated rainbow Randomizer and Compatible buttons to the bottom-left of the classic Item Spawner V2.
- Includes every page of the current category or subcategory.
- Filters compatible rolls using the item in the non-pointing hand.
- Checks native magazine wells, clip wells, authored speedloader compatibility, and available attachment mounts including installed adapters.
- Shows hover tooltips, disables compatible rolls with empty hands, and reports empty pools.
- Spawns one main object on the native pad, without the built-in random gun's attachment generation or bundled secondary objects.
- Bullet filtering is deferred. Runtime VR testing is pending.
