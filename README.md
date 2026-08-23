<p align="center">
  <img src="assets/banner-v4-lowres.png" alt="Evil Farm Owner" width="900">
</p>

<p align="center">
  <img src="assets/icon-v2.png" alt="Evil Farm Owner icon" width="128">
</p>

<h1 align="center">Evil Farm Owner v0.1.0</h1>

<p align="center">
  <strong>中文名：邪恶农场主</strong><br>
  Hire farmhands, pay wages, and let someone else do the repetitive work.
</p>

<p align="center">
  <a href="#中文说明">中文</a> ·
  <a href="#english">English</a> ·
  <a href="#roadmap">Roadmap</a>
</p>

---

<details open>
<summary id="中文说明"><strong>中文说明</strong></summary>

## 项目定位

`邪恶农场主` 是一个面向《星露谷物语》的 SMAPI Mod。它的核心设定是：玩家支付工资，雇佣农工完成重复农活，包括浇水、收获、清理、施肥和播种。

当前版本是 `0.1.0`，属于早期可玩原型。重点是先跑通“雇佣 -> 派工 -> 执行任务 -> 支付工资”的基础链路，后续再加入可见 NPC、真实寻路、仓库整理、箱子分类、机器补料和联机同步。

## 当前功能

- 按 `H` 在农场派工一次。
- 自动浇水。
- 自动收获成熟作物。
- 自动清理树枝、石头、杂草等农场杂物。
- 可选：从背包消耗肥料并给空地施肥。
- 可选：从背包消耗种子并播种。
- 工资系统：每次有效派工默认扣除 `500g`。
- 中文和英文文本。
- SMAPI 控制台命令，方便测试和调试。

## 使用方式

安装后进入游戏，站在农场内按 `H`，农工会在配置范围内执行一次已开启的任务。

也可以在 SMAPI 控制台输入：

```text
efo_work
```

查看当前状态：

```text
efo_status
```

切换单项任务：

```text
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

## 配置

配置文件位于：

```text
Mods/EvilFarmOwner/config.json
```

| 配置项 | 作用 | 默认值 |
| --- | --- | --- |
| `OpenMenuKey` | 派工快捷键 | `H` |
| `WorkRadius` | 工作扫描范围 | `64` |
| `DailyWage` | 每次有效派工费用 | `500` |
| `MaxTilesPerJob` | 单次最多处理的地块/对象数 | `250` |
| `WaterCrops` | 自动浇水 | `true` |
| `HarvestCrops` | 自动收获 | `true` |
| `ClearDebris` | 自动清理杂物 | `true` |
| `FertilizeEmptyDirt` | 自动施肥 | `false` |
| `PlantSeedsFromInventory` | 自动播种 | `false` |
| `DepositHarvestToNearestChest` | 收获物转入附近箱子，待开发 | `false` |

</details>

<details>
<summary id="english"><strong>English</strong></summary>

## What is this?

`Evil Farm Owner` is a Stardew Valley SMAPI mod about hiring farmhands to handle repetitive farm work. Pay a wage, send workers out, and let them water crops, harvest mature crops, clear debris, fertilize soil, and plant seeds.

Version `0.1.0` is an early playable prototype. The goal is to establish the core loop first: hire, assign work, execute tasks, and charge wages. Future versions can build on this foundation with visible NPC workers, pathfinding, warehouse logic, chest sorting, machine refilling, and multiplayer sync.

## Current Features

- Press `H` on the farm to run one work pass.
- Water crops.
- Harvest mature crops.
- Clear farm debris like twigs, stones, and weeds.
- Optional: fertilize empty dirt using fertilizer from the player inventory.
- Optional: plant seeds from the player inventory.
- Wage system, defaulting to `500g` per successful work pass.
- Chinese and English text.
- SMAPI console commands for testing and debugging.

## Usage

After installing the mod, enter the farm and press `H`.

You can also run:

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
| `MaxTilesPerJob` | Max tiles or objects handled per pass | `250` |
| `WaterCrops` | Water crops | `true` |
| `HarvestCrops` | Harvest mature crops | `true` |
| `ClearDebris` | Clear debris | `true` |
| `FertilizeEmptyDirt` | Fertilize empty dirt | `false` |
| `PlantSeedsFromInventory` | Plant seeds from inventory | `false` |
| `DepositHarvestToNearestChest` | Deposit harvests into nearby chests, planned | `false` |

</details>

---

## Roadmap

- In-game hiring menu.
- Named workers or temporary worker NPCs.
- Visible pathfinding and work animations.
- Warehouse and storage anchor system.
- Chest sorting and item classification.
- Machine refilling and product collection.
- Animal care tasks.
- Host-authoritative multiplayer support.

## Design Notes

The mod is designed around a task system, so future work types can be added without rewriting the hiring flow.

```text
Hire System
├── Worker Contract
├── Task Scheduler
├── Farm Scan
├── Work Tasks
│   ├── Water
│   ├── Harvest
│   ├── Clear Debris
│   ├── Fertilize
│   ├── Plant
│   └── Sort Chests
└── Storage / Warehouse
```

## Development

Requirements:

- Stardew Valley 1.6+
- SMAPI 4.0+
- .NET 6 SDK

Build:

```bash
dotnet build -c Release
```

The project uses `Pathoschild.Stardew.ModBuildConfig`, which can generate a ready-to-install mod package after a release build.

## Multiplayer

The current prototype should be run by the host player. Proper multiplayer support is planned around host-side game-state mutation and client-side work requests.

## License

License not yet specified.
