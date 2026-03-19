# Fish Slapper

[简体中文](./README.zh-CN.md) 
[Nexus Mods Page](https://www.nexusmods.com/stardewvalley/mods/43727)

Fish Slapper is a lightweight SMAPI mod for Stardew Valley that lets you deal with fish the direct way: smack them after a successful catch, or dive into the water and throw hands during the fishing minigame.

## Features

### Fish Slap (after catch)
- After landing a fish, you can give it a celebratory smack. Your farmer does a full hit animation with a hop, impact particles, and a custom slap SFX.
- A results summary pops up afterward with the player name, fish name, slap count, and clear time.
- Default slap key: `Right Mouse` or `Space`.

### Dive Slap (during fishing minigame)
- Press the key during the fishing bar minigame to jump into the water and fight the fish face-to-face.
- The whole sequence is fully animated: dive in, swim over, slap the fish, then head back to shore, complete with custom SFX and splash effects.
- Difficulty scales with the fish's difficulty rating and movement pattern (see the [difficulty reference](docs/dive-slap-difficulty.zh-CN.md)).
- Success: you KO the fish, and the HUD shows your time and slap count.
- Failure: the fish wins the exchange and smacks you back.
- Each dive costs 20 HP and 50 stamina, and dive slap runs never award treasure chest loot.
- Default dive slap key: `Q`.

### General
- On-screen key prompts appear while fishing, and can be turned off in the config.
- Optional Generic Mod Config Menu support makes it easy to rebind keys and toggle prompts.
- Includes English and Simplified Chinese translations.

## Demo

### Fish Slap

![Fish Slap demo 1](docs/FishSlapDemo.gif)
![Fish Slap demo 2](docs/FishSlapDemo2.gif)

### Dive Slap Success

![Dive Slap success demo](docs/DiveSlapSuccess.gif)

### Dive Slap Failure

![Dive Slap failure demo](docs/DiveSlapFailure.gif)

## Requirements

- Stardew Valley
- [SMAPI](https://smapi.io/) 4.0.0 or later
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) (optional)

## Installation

1. Install SMAPI.
2. Download the latest release of the mod.
3. Extract the `FishSlapper` folder into `Stardew Valley/Mods`.

## Development

The source code is in `FishSlapper/`.

```bash
dotnet build FishSlapper/FishSlapper.sln
```

## License

Licensed under the MIT License. See `LICENSE`.
