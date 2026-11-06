# Offline Bodies

A [Vintage Story](https://www.vintagestory.at/) mod. When a player logs out in survival, their character stays in the world asleep, and anyone can loot it like a chest until the owner logs back in.

Available on the mod database: https://mods.vintagestory.at/offlinebodies

## What it does

- **Right-click** a sleeping body to open it like a chest and take what the player carries (hotbar + backpack contents). Bags, clothes and armor stay on the body.
- **Sneak + right-click** to start dragging the body; do it again anywhere to let go. It stays one block in front of you, at its normal size. While dragging you walk 35% slower and your hands are full (no mining, attacking, placing, beds or storage; doors remain usable). The body drops automatically if you get more than 6 blocks away.
- Bodies can't be damaged, killed or targeted, and never despawn.
- When the owner logs back in they wake up where the body is now, with whatever is left in it.

Admins can run `/offlinebodies` to list all current sleeping bodies.

## How items are kept safe

Every body has a record stored inside the world save, written together with the chunks and player data on each save. If the body can't be found when the owner logs in, their items come back from that record. A body that loads without a matching record (for example an old copy after a crash) is removed without dropping items, to reduce the risk of item loss or duplication. Crash recovery still needs in-game verification.

## Removing the mod

1. Keep Offline Bodies 0.1.6 or later installed and run `/offlinebodies prepare-uninstall` as an admin. This pauses new sleeping bodies, including after a saved restart.
2. Have each listed player log in and finish waking up to reclaim the items still on their body. Run the command again to check who is left.
3. Once the command reports that no sleeping bodies remain, save and stop the server cleanly. Then remove the mod.

While a body remains, its items are held by the mod and will be inaccessible when the mod is absent.

## Building

Requires the .NET 10 SDK and a Vintage Story install for the game DLLs.

```
dotnet build OfflineBodies.csproj -c Release -p:VintageStoryInstallDir="<path to your Vintage Story install>"
```

To package a release, zip these at the **root** of the zip (no enclosing folder): `offlinebodies.dll` (from `bin/Release/`), `modinfo.json`, the `assets/` folder, `README.md`, and `LICENSE-DeathCorpses.txt`.

## License

This mod is built on code and structure from [Death Corpses](https://github.com/cross/DeathCorpses), which is MIT licensed. The original copyright notice is kept in `LICENSE-DeathCorpses.txt` as that license requires.

## 0.1.5 release candidate

Combines the 0.1.4 dragging changes with the 0.1.3.5 rendering workaround. Sleeping bodies use default skin/hair while retaining display clothes and armor. The reported first-join crash fix is not yet verified in-game. This release does not include the separate body-tamper notification add-on.

Back up the world before updating. Install the same version on server and clients. For unpublished testing, provide the ZIP to testers manually; published releases use ModDB auto-download. Close the client completely after changing versions. Avoid removing this mod while sleeping bodies still hold player items; have their owners reclaim them first.

Maintained with assistance from Claude and OpenAI Codex.

