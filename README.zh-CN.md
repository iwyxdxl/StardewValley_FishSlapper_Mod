# Fish Slapper

[English](./README.md) · [Nexus Mods 页面](https://www.nexusmods.com/stardewvalley/mods/43727)

Fish Slapper 是一个给《星露谷物语》用的轻量 SMAPI 模组。你可以在鱼钓上来后补上一巴掌，也可以在钓鱼小游戏途中直接跳进水里，跟鱼正面开打。

## 功能

### 钓上扇鱼（钓到鱼之后）
- 成功把鱼钓起来后，可以顺手补上一巴掌。角色会播一套带小跳、打击特效和专属音效的动作演出。
- 结束后会弹出结算，显示玩家名、鱼名、扇了几下，以及总共用了多久。
- 默认按键为 `鼠标右键` 或 `空格`。

### 下水扇鱼（钓鱼小游戏中）
- 在钓鱼小游戏进行时按下按键，角色会直接跳进水里，跟鱼贴脸对打。
- 整套过程都有动画演出：跳水、游过去、扇鱼、再回到岸上，并带有专属音效和水花特效。
- 难度会根据鱼本身的难度值和行动模式自动变化（详见[难度说明](docs/dive-slap-difficulty.zh-CN.md)）。
- 成功：直接把鱼拍晕，HUD 会显示你的用时和扇鱼次数。
- 失败：鱼会当场还手，反过来给你一巴掌。
- 每次下水会消耗 20 点生命和 50 点体力；用这种方式过关不会拿到宝箱奖励。
- 默认按键为 `Q`。

### 通用
- 钓鱼时会在屏幕上显示按键提示，也可以在配置里关闭。
- 可选支持 Generic Mod Config Menu，方便改按键和开关提示。
- 提供英文和简体中文翻译。

## 演示

### 钓上扇鱼

![钓上扇鱼演示 1](docs/FishSlapDemo.gif)
![钓上扇鱼演示 2](docs/FishSlapDemo2.gif)

### 下水扇鱼成功

![下水扇鱼成功演示](docs/DiveSlapSuccess.gif)

### 下水扇鱼失败

![下水扇鱼失败演示](docs/DiveSlapFailure.gif)

## 运行需求

- 《星露谷物语》
- [SMAPI](https://smapi.io/) 4.0.0 或更高版本
- [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)（可选）

## 安装方法

1. 安装 SMAPI。
2. 下载这个 mod 的最新发布版本。
3. 将 `FishSlapper` 文件夹解压到 `Stardew Valley/Mods` 目录。

## 开发

源码在 `FishSlapper/` 目录中。

```bash
dotnet build FishSlapper/FishSlapper.sln
```

## 许可证

本项目采用 MIT License，详见 `LICENSE`。
