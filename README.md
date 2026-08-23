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
  Pay farmhands to handle watering, harvesting, fertilizing, and planting.
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

Press one key to review named worker candidates and see why someone is currently unavailable. The current version is an early playable prototype; the roster is deliberately read-only while safe contracts, schedules, and visible NPC work are developed.

### Current Features

- Open a read-only named worker roster with the `K` key.
- Show a current availability state and reason without moving, hiring, or reserving an NPC.
- Select a preview-eligible NPC to inspect a read-only watering contract and itemized wage estimate.
- Water crops.
- Harvest mature crops.
- Debris cleanup is temporarily disabled while resource-safe delivery is implemented.
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

Load a save and press:

```text
K
```

The roster and watering contract are read-only previews. Select a green preview-eligible row to see the six-hour estimate, friendship multiplier, workday or rest-day multiplier, baseline efficiency, and overtime policy. There is no confirm button, charge, or farm work. The prototype work executor remains available only through the `efo_work` SMAPI console command for isolated testing.

In multiplayer, both the host and farmhands may open their local read-only roster. It sends no work request and changes no NPC or farm state. A future contract action will be host-authoritative and will recheck availability instead of trusting this preview.

New configurations default to `K`. If an older config still uses `H` while UI Info Suite 2 is installed, the mod shows a conflict warning; change `OpenMenuKey` to `K` or use `efo_roster`.

You can also use SMAPI console commands:

```text
efo_work
efo_roster
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

`efo_toggle clear` is retained for config compatibility, but reports that debris cleanup is temporarily disabled.

### Configuration

If Generic Mod Config Menu is installed, Evil Farm Owner appears in its Mod Options list with a localized roster-hotkey setting. The integration is optional; direct `config.json` editing remains supported. Work area, tasks, and wages belong to each future NPC contract instead of a global player-centered scan, so legacy prototype controls aren't exposed in the menu.

The config file is created at:

```text
Mods/EvilFarmOwner/config.json
```

| Key | Description | Default |
| --- | --- | --- |
| `OpenMenuKey` | Hotkey for the read-only worker roster | `K` |
| `WorkRadius` | Work scan radius around the player (`1`–`256`) | `64` |
| `DailyWage` | Legacy `efo_work` prototype charge (`0`–`100000000`); the named contract preview uses its itemized hourly model | `500` |
| `MaxTilesPerJob` | Maximum handled tiles or objects per work pass (`1`–`10000`) | `250` |
| `WaterCrops` | Enable crop watering | `true` |
| `HarvestCrops` | Enable crop harvesting | `true` |
| `ClearDebris` | Debris cleanup compatibility setting; forced off for safety | `false` |
| `FertilizeEmptyDirt` | Enable fertilizing from inventory | `false` |
| `PlantSeedsFromInventory` | Enable planting from inventory | `false` |
| `DepositHarvestToNearestChest` | Planned chest deposit behavior | `false` |

---

## 中文说明

### 这个 Mod 是做什么的？

`邪恶农场主` 是一个《星露谷物语》SMAPI Mod。它的核心玩法是：你花钱雇佣农工，把重复农活交给他们处理。

当前版本是早期可玩原型。现在可以先查看具名 NPC 候选人及其当前不可雇佣原因；在安全合同、日程保护和可见工作流程完成前，这个名单保持只读。

### 当前功能

- 按 `K` 打开只读的具名工人候选名单。
- 显示当前可用状态和原因，不移动、雇佣或预留 NPC。
- 选择可预览的 NPC，查看只读浇水合同和逐项工资估算。
- 自动浇水。
- 自动收获成熟作物。
- 杂物清理暂时停用，等待实现不会丢失资源的交付逻辑。
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

载入存档后按：

```text
K
```

候选名单和浇水合同都仅用于预览。选择绿色“可预览合同”行，可以查看六小时工资、好感度系数、工作日或休息日系数、基础效率和加班政策。界面没有确认按钮，不会扣钱或开始农活。原型工作执行器只保留在 SMAPI 控制台命令 `efo_work` 中，用于隔离测试。

多人游戏中，主机和农场助手都可以打开各自的本地只读名单。这个操作不会发送工作请求，也不会改变 NPC 或农场状态。未来真正签订合同时将由主机重新检查可用性，不会直接相信这次预览结果。

新配置默认使用 `K`。如果旧配置仍使用 `H` 且安装了 UI Info Suite 2，模组会显示冲突警告；请把 `OpenMenuKey` 改成 `K`，或使用 `efo_roster`。

也可以使用 SMAPI 控制台命令：

```text
efo_work
efo_roster
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

为了兼容已有配置，仍保留 `efo_toggle clear`；执行时会提示杂物清理暂时停用。

### 配置文件

如果安装了 Generic Mod Config Menu，“邪恶农场主”会出现在它的“MOD 选项”列表中，并提供本地化的候选名单快捷键设置。该集成是可选的，仍可直接编辑 `config.json`。工作区域、任务和工资属于未来的每份 NPC 合同，不使用以玩家为中心的全局扫描范围，因此菜单不会暴露旧原型设置。

配置文件位置：

```text
Mods/EvilFarmOwner/config.json
```

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `OpenMenuKey` | 打开只读工人名单的快捷键 | `K` |
| `WorkRadius` | 以玩家为中心的工作扫描范围（`1`–`256`） | `64` |
| `DailyWage` | 旧版 `efo_work` 原型扣款（`0`–`100000000`）；具名合同预览使用逐项时薪模型 | `500` |
| `MaxTilesPerJob` | 单次最多处理的地块或对象数量（`1`–`10000`） | `250` |
| `WaterCrops` | 开启自动浇水 | `true` |
| `HarvestCrops` | 开启自动收获 | `true` |
| `ClearDebris` | 杂物清理兼容配置；当前会被安全地强制关闭 | `false` |
| `FertilizeEmptyDirt` | 开启背包施肥 | `false` |
| `PlantSeedsFromInventory` | 开启背包播种 | `false` |
| `DepositHarvestToNearestChest` | 计划中的箱子入库功能 | `false` |

---

## Current Status

### Plan List

- Add a safe contract confirmation after the read-only preview.
- Acquire and release an NPC work lease without disrupting protected schedules.
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
- Debris cleanup is disabled until normal drops and a safe delivery destination are implemented.
- Harvest output uses the game's default behavior for now.
- `DepositHarvestToNearestChest` is listed in config but not implemented yet.
- Planting currently uses the first available seed stack in the player inventory.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

License not yet specified.
