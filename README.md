# Offline Bodies

A [Vintage Story](https://www.vintagestory.at/) mod. When a player logs out in survival, their character stays in the world asleep, and anyone can loot it like a chest until the owner logs back in.

Available on the mod database: https://mods.vintagestory.at/offlinebodies

## What it does

- **Right-click** a sleeping body to open it like a chest and take what the player carries (hotbar + backpack contents). Bags, clothes and armor stay on the body.
- **Sneak + right-click** to start dragging the body; do it again anywhere to let go. It follows two blocks behind you. While dragging you walk 35% slower and your hands are full (no mining, attacking, placing or using items). The body drops automatically if you get more than 6 blocks away.
- Bodies can't be damaged, killed or targeted, and never despawn.
- When the owner logs back in they wake up where the body is now, with whatever is left in it.

Admins can run `/offlinebodies` to list all current sleeping bodies.

## How items are kept safe

Every body has a record stored inside the world save, written together with the chunks and player data on each save. If the body can't be found when the owner logs in, their items come back from that record. A body that loads without a matching record (for example an old copy after a crash) is removed without dropping items, so items are never lost or duplicated.

## Building

Requires the .NET 10 SDK and a Vintage Story install for the game DLLs.

```
dotnet build OfflineBodies.csproj -c Release -p:VintageStoryInstallDir="<path to your Vintage Story install>"
```

To package a release, zip these at the **root** of the zip (no enclosing folder): `offlinebodies.dll` (from `bin/Release/`), `modinfo.json`, the `assets/` folder, and `LICENSE-DeathCorpses.txt`.

## License

This mod is built on code and structure from [Death Corpses](https://github.com/cross/DeathCorpses), which is MIT licensed. The original copyright notice is kept in `LICENSE-DeathCorpses.txt` as that license requires.
