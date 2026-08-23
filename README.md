<p align="center">
  <img src="assets/banner-v4-lowres.png" alt="Evil Farm Owner banner" width="820">
</p>

<h1 align="center">
  <img src="assets/icon-v2.png" alt="Evil Farm Owner icon" width="48">
  Evil Farm Owner
</h1>

<p align="center">
  <strong>邪恶农场主</strong> · Stardew Valley SMAPI Mod · v0.1.0
</p>

<p align="center">
  Pay farmhands to handle watering, harvesting, debris cleanup, fertilizing, and planting.
</p>

<p align="center">
  <a href="#english">English</a> ·
  <a href="#中文说明">中文说明</a> ·
  <a href="#current-status">Current Status</a>
</p>

---

## English

### What is Evil Farm Owner?

`Evil Farm Owner` is a Stardew Valley mod for players who want to turn repetitive farm chores into paid labor.

Press one key, pay a wage, and let hired farmhands run a work pass on your farm. The current version is an early playable prototype, focused on the core loop: hire workers, run enabled jobs, and charge money only when work is actually completed.

### Current Features

- Hire farmhands with the `H` key.
- Water crops.
- Harvest mature crops.
- Clear farm debris such as twigs, stones, and weeds.
- Optionally fertilize empty dirt using fertilizer from your inventory.
- Optionally plant seeds from your inventory.
- Charge a wage after successful work, defaulting to `500g`.
- Support English and Chinese text.

### Installation

1. Install SMAPI.
2. Download or build `EvilFarmOwner`.
3. Place the `EvilFarmOwner` folder in your `Stardew Valley/Mods` folder.
4. Launch Stardew Valley through SMAPI.

### How to Use

Stand on your farm and press:

```text
H
```

The hiring menu shows the current wage and which jobs are enabled. Confirm to
run one work pass, or cancel to leave the farm and your money unchanged.

You can also use SMAPI console commands:

```text
efo_work
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

### Configuration

The config file is created at:

```text
Mods/EvilFarmOwner/config.json
```

| Key | Description | Default |
| --- | --- | --- |
| `OpenMenuKey` | Hotkey for hiring farmhands | `H` |
| `WorkRadius` | Work scan radius around the player | `64` |
| `DailyWage` | Wage charged after successful work | `500` |
| `MaxTilesPerJob` | Maximum handled tiles or objects per work pass | `250` |
| `WaterCrops` | Enable crop watering | `true` |
| `HarvestCrops` | Enable crop harvesting | `true` |
| `ClearDebris` | Enable debris cleanup | `true` |
| `FertilizeEmptyDirt` | Enable fertilizing from inventory | `false` |
| `PlantSeedsFromInventory` | Enable planting from inventory | `false` |
| `DepositHarvestToNearestChest` | Planned chest deposit behavior | `false` |

---

## 中文说明

### 这个 Mod 是做什么的？

`邪恶农场主` 是一个《星露谷物语》SMAPI Mod。它的核心玩法是：你花钱雇佣农工，把重复农活交给他们处理。

当前版本是早期可玩原型，重点先跑通最基础的循环：雇佣农工、执行已开启的工作、只有真正完成工作后才扣钱。

### 当前功能

- 按 `H` 雇佣农工执行一次工作。
- 自动浇水。
- 自动收获成熟作物。
- 自动清理树枝、石头、杂草等农场杂物。
- 可选：从背包拿肥料给空地施肥。
- 可选：从背包拿种子播种。
- 有工资系统，默认每次有效工作扣除 `500g`。
- 支持中文和英文文本。

### 安装方式

1. 安装 SMAPI。
2. 下载或构建 `EvilFarmOwner`。
3. 把 `EvilFarmOwner` 文件夹放进 `Stardew Valley/Mods`。
4. 通过 SMAPI 启动游戏。

### 使用方式

站在农场里按：

```text
H
```

雇佣菜单会显示当前工资和已开启的任务。确认后执行一轮工作；取消则不会
改变农场状态，也不会扣除金币。

也可以使用 SMAPI 控制台命令：

```text
efo_work
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

### 配置文件

配置文件位置：

```text
Mods/EvilFarmOwner/config.json
```

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `OpenMenuKey` | 雇佣农工快捷键 | `H` |
| `WorkRadius` | 以玩家为中心的工作扫描范围 | `64` |
| `DailyWage` | 有效工作后的工资费用 | `500` |
| `MaxTilesPerJob` | 单次最多处理的地块或对象数量 | `250` |
| `WaterCrops` | 开启自动浇水 | `true` |
| `HarvestCrops` | 开启自动收获 | `true` |
| `ClearDebris` | 开启杂物清理 | `true` |
| `FertilizeEmptyDirt` | 开启背包施肥 | `false` |
| `PlantSeedsFromInventory` | 开启背包播种 | `false` |
| `DepositHarvestToNearestChest` | 计划中的箱子入库功能 | `false` |

---

## Current Status

### Plan List

- Add an in-game hiring menu.
- Show enabled jobs and wage before confirmation.
- Add visible farmhand workers or simple worker feedback.
- Add a warehouse or office anchor for storage and hiring.
- Add safe host-authoritative multiplayer behavior.

### Idea List

- Sort chests by item type, quality, season, or custom labels.
- Refill machines and collect finished products.
- Add animal care jobs.
- Add worker tiers with different wages and speeds.
- Add flavor text, worker complaints, and capitalist farm-owner jokes.

### Bug List / Known Limitations

- Multiplayer support is not complete; the host should run work passes for now.
- Workers are not visible yet; tasks execute instantly.
- Harvest output uses the game's default behavior for now.
- `DepositHarvestToNearestChest` is listed in config but not implemented yet.
- Planting currently uses the first available seed stack in the player inventory.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

License not yet specified.
