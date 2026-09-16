# Gundomizer

[![Complete Item Spawner V2 with Gundomizer's rainbow buttons](docs/images/item-spawner.png)](docs/images/item-spawner-original.png)

Randomize items directly in **Item Spawner V2**, including the **toolbox tablet**, in classic and tag-search modes.

## Features

- **Random item:** roll from the current category and its subcategories, or your selected tags, across all pages.
- **Compatible item:** roll items that fit what you're holding, including magazines, attachments, and installed rail adapters.
- **Random ammo:** roll a compatible ammo variant. The arrow beside it opens the variant toggles.
- **Spawn or preview:** spawn immediately, or select a result to inspect, accept, or reroll.
- **Auto-fill:** replace loaded rounds and fill the held magazine or weapon, while still spawning the loose round.
- **Continuous search:** shows loading progress; click the active button again to cancel.

## Mod compatibility

Supports modded items, including **Modul / Modular Workshop** content and **OtherLoader**'s custom item IDs.

## Optimization

A background metadata index checks compatibility without loading entire assets just to inspect their connectors. It's saved between launches and updates changed packages, making rolls faster as indexing completes.

## Mod Options

- **Spawn Item Instantly** — ON: spawn immediately; OFF: preview first.
- **Auto Fill Held Item** — ON: refill with the rolled ammo after spawning.
- **Persistent Connector Index** — ON: build and reuse the metadata cache.
- **Reset Metadata Indexing** — OFF: clear and rebuild the cache once, then switch itself off.

Edit `quaternion.gundomizer.cfg` through your mod manager's Config Editor. Requires **BepInExPack H3VR**.

## Changelog

- **1.0.0:** First public release: item, compatible gear and ammo rolls, optional auto-fill, and persistent background indexing.

[Development version history](CHANGELOG.md)

## Credits

- Special thanks to Glicen for requesting and sponsoring the mod.
- Anton Hand and RUST LTD. for **H3VR**.
- The **H3VR Homebrew community**, mod authors, and tool and loader maintainers.
- **nesrak1 / AssetsTools.NET** for the bundle metadata reader library.

## Support

Tips and donations are welcome on [Ko-fi](https://ko-fi.com/quaternion7). You can find my other projects and links at [quaternion7.github.io](https://quaternion7.github.io/).
