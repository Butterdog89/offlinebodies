# Changelog

## 0.1.6

- Added `/offlinebodies prepare-uninstall` so players can reclaim their items before the mod is removed. It pauses new sleeping bodies and lists the players who still need to log in. The pause is stored with the world save.

## 0.1.5 (release candidate)

- Combine one-block forward dragging and door-only interactions with the 0.1.3.5 rendering workaround.
- Remove dynamic player-skin compositing from sleeping bodies; default skin replaces copied skin/hair.
- First-join crash resolution still requires in-game confirmation.

## 0.1.4

- A dragged body now floats one block in front of you instead of trailing two blocks behind, so you can see where it will drop.
- While you are carrying a body, right-click no longer works on beds, chests or other interactable blocks (so a bed can't put you to sleep). Doors are the one exception and still open. This is enforced server-side.

## 0.1.2

- Fix a client crash when logging in while your body had been moved (for example while someone was dragging it, or after it had been dragged and dropped). The mod no longer teleports a player to their body until their game has finished loading.

## 0.1.1

- Add dragging: sneak + right-click a sleeping body to drag it, again to let go. Follows two blocks behind, 35% slower walk, hands full while dragging, drops if you get too far away.
- Fixes for joining while a body is being dragged.

## 0.1.0

- First release. Players who log out in survival leave a sleeping body that others can loot like a chest. The owner wakes up in it on login. Bodies can't be damaged and never despawn.

