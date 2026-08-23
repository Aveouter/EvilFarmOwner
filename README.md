<p align="center">
  <img src="assets/banner-v4-lowres.png" alt="Evil Farm Owner banner" width="820">
</p>

<h1 align="center">
  <img src="assets/icon-v2.png" alt="Evil Farm Owner icon" width="52" align="center">
  Evil Farm Owner v0.1.0
</h1>

<p align="center">
  <strong>邪恶农场主</strong><br>
  Hire farmhands, pay wages, and let someone else handle the repetitive farm work.
</p>

<p align="center">
  <a href="#中文">中文</a> ·
  <a href="#english">English</a> ·
  <a href="#plan-list">Plan List</a> ·
  <a href="#idea-list">Idea List</a> ·
  <a href="#bug-list">Bug List</a>
</p>

---

<details open>
<summary id="中文"><strong>中文</strong></summary>

## 这个 Mod 是做什么的？

`邪恶农场主` 让你在《星露谷物语》中花钱雇佣农工，把重复农活交出去。当前版本已经可以在农场内一键派工，帮助你浇水、收获、清理杂物，并可选择施肥和播种。

这不是一个完整的经营系统，当前版本更像“雇佣农工”的第一版可玩原型。后续会逐步加入雇佣菜单、可见 NPC、仓库整理和更完整的自动化流程。

## 当前能做什么？

- 按 `H` 派工一次。
- 自动浇水。
- 自动收获成熟作物。
- 自动清理树枝、石头、杂草等农场杂物。
- 可选：从背包拿肥料给空地施肥。
- 可选：从背包拿种子播种。
- 有工资消耗，默认每次有效派工扣除 `500g`。
- 支持中文和英文文本。

## 怎么用？

1. 安装 SMAPI。
2. 把 Mod 文件夹放进 `Stardew Valley/Mods`。
3. 通过 SMAPI 启动游戏。
4. 进入农场，按 `H` 派工。

也可以在 SMAPI 控制台输入：

```text
efo_work
```

查看当前配置：

```text
efo_status
```

开关单项工作：

```text
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

## 配置文件

配置文件位于：

```text
Mods/EvilFarmOwner/config.json
```

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `OpenMenuKey` | 派工快捷键 | `H` |
| `WorkRadius` | 工作扫描范围 | `64` |
| `DailyWage` | 每次有效派工费用 | `500` |
| `MaxTilesPerJob` | 单次最多处理数量 | `250` |
| `WaterCrops` | 自动浇水 | `true` |
| `HarvestCrops` | 自动收获 | `true` |
| `ClearDebris` | 自动清理杂物 | `true` |
| `FertilizeEmptyDirt` | 自动施肥 | `false` |
| `PlantSeedsFromInventory` | 自动播种 | `false` |
| `DepositHarvestToNearestChest` | 收获物放入附近箱子，待开发 | `false` |

</details>

<details>
<summary id="english"><strong>English</strong></summary>

## What does this mod do?

`Evil Farm Owner` lets you hire farmhands in Stardew Valley so repetitive farm work is no longer all on you. The current version can run one work pass on the farm to water crops, harvest mature crops, clear debris, and optionally fertilize or plant seeds.

This is not a full management system yet. Version `0.1.0` is the first playable prototype for the hired-worker loop. Future versions will add a proper hiring menu, visible NPC workers, warehouse sorting, and richer automation.

## Current Features

- Press `H` to send farmhands out for one work pass.
- Water crops.
- Harvest mature crops.
- Clear farm debris like twigs, stones, and weeds.
- Optional: fertilize empty dirt using fertilizer from your inventory.
- Optional: plant seeds from your inventory.
- Wage cost, defaulting to `500g` per successful work pass.
- Chinese and English text.

## How to Use

1. Install SMAPI.
2. Put the mod folder into `Stardew Valley/Mods`.
3. Launch the game through SMAPI.
4. Enter the farm and press `H`.

You can also run this in the SMAPI console:

```text
efo_work
```

Show current settings:

```text
efo_status
```

Toggle individual jobs:

```text
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

## Config

The config file is generated at:

```text
Mods/EvilFarmOwner/config.json
```

| Key | Description | Default |
| --- | --- | --- |
| `OpenMenuKey` | Work hotkey | `H` |
| `WorkRadius` | Work scan radius | `64` |
| `DailyWage` | Wage per successful work pass | `500` |
| `MaxTilesPerJob` | Max handled tiles or objects per pass | `250` |
| `WaterCrops` | Water crops | `true` |
| `HarvestCrops` | Harvest mature crops | `true` |
| `ClearDebris` | Clear debris | `true` |
| `FertilizeEmptyDirt` | Fertilize empty dirt | `false` |
| `PlantSeedsFromInventory` | Plant seeds from inventory | `false` |
| `DepositHarvestToNearestChest` | Deposit harvests into nearby chests, planned | `false` |

</details>

---

## Plan List

- Add an in-game hiring menu instead of relying on only a hotkey and console commands.
- Add visible hired workers or temporary NPC workers.
- Add worker movement, pathfinding, and work animations.
- Add a warehouse or office anchor for hiring, seed storage, fertilizer storage, and harvest output.
- Add host-authoritative multiplayer support.

## Idea List

- Chest sorting by item type, quality, season, or custom labels.
- Machine refilling and product collection.
- Animal care jobs such as petting, feeding checks, and product pickup.
- Tiered worker contracts with different wages, speeds, and job limits.
- Evil capitalist flavor text, worker complaints, and event-style messages.

## Bug List

- Multiplayer support is not complete; the host should run the work pass for now.
- Visible NPC workers are not implemented yet; tasks execute instantly.
- Harvest output currently follows the game's default harvest behavior instead of a custom warehouse route.
- `DepositHarvestToNearestChest` is listed in config but not implemented yet.
- Planting uses the first available seed stack in the player inventory and does not yet provide a crop selection UI.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

License not yet specified.
