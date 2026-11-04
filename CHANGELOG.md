# Changelog

## 0.1.3.5

- Fix a client crash that hit every new player the first time they joined the server (a texture-atlas error, `ArgumentOutOfRangeException` in `TextureAtlasManager.RuntimeUploadTextureToPos`). Players had to quit and rejoin to get in. Sleeping bodies no longer composite the owner's skin onto the body at render time, which was overflowing the texture atlas on the first load of a heavily-modded client. Bodies still keep the owner's name, shape, pose and worn clothes/armor, but show a default face/skin instead of the owner's exact skin and hair.

## 0.1.2

- Fix a client crash when logging in while your body had been moved (for example while someone was dragging it, or after it had been dragged and dropped). The mod no longer teleports a player to their body until their game has finished loading.

## 0.1.1

- Add dragging: sneak + right-click a sleeping body to drag it, again to let go. Follows two blocks behind, 35% slower walk, hands full while dragging, drops if you get too far away.
- Fixes for joining while a body is being dragged.

## 0.1.0

- First release. Players who log out in survival leave a sleeping body that others can loot like a chest. The owner wakes up in it on login. Bodies can't be damaged and never despawn.
