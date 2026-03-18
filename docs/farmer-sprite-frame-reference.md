# Farmer Sprite Frame Reference

This file summarizes the `FarmerSprite` frame constants extracted during debugging.
It is intended to be easier to reuse than the old raw diagnostic dump.

## Notes

- Safe direct frame overrides are usually the low base frames, especially the early movement and grab frames.
- High action frames like `toolChoose`, `swordswipe`, and `punch` are not good candidates for raw `CurrentFrame = ...` or `setCurrentSingleFrame(...)` usage.
- For this mod, the slap animation currently uses `setCurrentFrame(272)` together with a `Game1.drawTool(...)` Harmony patch to avoid the extra held-tool render.
- If you only need a stable forced pose, prefer a low frame like `grabDown = 64`.

## Directional Frame Groups


| Action      | Down | Right | Up  | Left |
| ----------- | ---- | ----- | --- | ---- |
| walk        | 0    | 8     | 16  | 24   |
| run         | 32   | 40    | 48  | 56   |
| grab        | 64   | 72    | 80  | 88   |
| carryWalk   | 96   | 104   | 112 | 120  |
| carryRun    | 128  | 136   | 144 | 152  |
| tool        | 160  | 168   | 176 | 184  |
| toolChoose  | 192  | 194   | 196 | 198  |
| seedThrow   | 200  | 204   | 208 | 212  |
| swordswipe  | 232  | 240   | 248 | 256  |
| punch       | 272  | 274   | 276 | 278  |
| harvestItem | 281  | 280   | 279 | 282  |
| shear       | 285  | 284   | 283 | 286  |
| milk        | 289  | 288   | 287 | 290  |
| fishing     | 297  | 296   | 295 | 298  |
| fishingDone | 301  | 300   | 299 | 302  |


## Single Frames


| Name              | Value |
| ----------------- | ----- |
| eat               | 216   |
| sick              | 224   |
| tired             | 291   |
| tired2            | 292   |
| passOutTired      | 293   |
| drink             | 294   |
| pan               | 303   |
| showHoldingEdible | 304   |
| cheer             | 97    |


## Mod-Specific Usage Notes

### FishSlapper slap pose

- Current slap frame: `punchDown = 272`
- Current implementation:
  - force the farmer pose with `FarmerSprite.setCurrentFrame(272)`
  - intercept `Game1.drawTool(Farmer, int)`
  - when the player is in the slap window and holding a caught fish, call `rod.draw(spriteBatch)` directly and skip the default tool-layer draw path

### What to avoid

- `FarmerSprite.CurrentFrame = 272`
  - can desync the layered farmer render state
  - may cause missing face or limb layers
- `FarmerSprite.setCurrentSingleFrame(272, ...)`
  - same general issue for high action frames
- raw direct forcing of high action constants like `192`, `232`, `272`
  - these often depend on vanilla animation/tool state, not just the frame index

## Source

The values above were copied from an in-game diagnostic dump of `FarmerSprite` static frame constants.
