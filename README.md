# Evil Farm Owner v0.1.0

中文名：邪恶农场主

> 一个面向《星露谷物语》的 SMAPI Mod：雇佣农工，把重复农活交出去。

`邪恶农场主` 目前处于早期开发阶段。第一版目标不是做一个“全自动作弊器”，而是先搭好一套可扩展的农场劳工系统：玩家支付工资，农工根据配置完成浇水、收获、清理、施肥、播种等工作。后续可以继续扩展到仓库整理、箱子分类、机器补料、动物照料和真正可见的 NPC 劳工。

## 当前功能

- 按 `H` 在农场派工一次。
- 支持自动浇水。
- 支持自动收获成熟作物。
- 支持清理农场里的树枝、石头、杂草等杂物。
- 支持从背包消耗肥料并给空地施肥。
- 支持从背包消耗种子并播种。
- 支持工资系统，默认每次有效派工扣除 `500g`。
- 支持中文和英文文本。
- 支持 SMAPI 控制台命令，方便调试和临时开关功能。

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

主要配置项：

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

## 开发状态

当前版本是 `0.1.0`，核心目标是跑通“雇佣农工执行任务”的机制。

已经完成：

- SMAPI Mod 工程结构
- 配置文件
- 中英文 i18n
- 派工快捷键
- 控制台命令
- 浇水、收获、清理、施肥、播种任务

下一步计划：

- 增加游戏内雇佣菜单
- 增加可见 NPC/临时工人
- 增加真实寻路和工作动画
- 增加仓库整理与箱子分类
- 增加指定仓库作为种子、肥料、收获物中转点
- 增加联机同步逻辑

## 设计思路

这个 Mod 会按“任务系统”继续扩展，而不是把所有逻辑写成一个大方法。

理想结构是：

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

这样后续新增“仓库整理”“机器补料”“动物照料”时，只需要新增任务类型，而不需要重写派工逻辑。

## 开发环境

- Stardew Valley 1.6+
- SMAPI 4.0+
- .NET 6 SDK

构建：

```bash
dotnet build -c Release
```

构建成功后，`Pathoschild.Stardew.ModBuildConfig` 会自动生成可安装的 Mod 包。

## 联机说明

目前第一版建议由房主运行派工逻辑。联机正式支持会在后续加入：

- 房主统一修改农场状态
- 农场工人客户端发送派工请求
- ModMessage 同步任务状态和结果

## 许可

License 暂未指定。
