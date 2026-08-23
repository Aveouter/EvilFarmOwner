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

Press one key to review named worker candidates and see why someone is currently unavailable. An available adult NPC can be assigned a visible watering or harvest contract with an itemized wage, lossless harvest delivery, and a protected return to their original schedule position.

### Current Features

- Open a named worker roster with the `K` key.
- Show each NPC's current availability and an explicit reason when they cannot be hired.
- Choose watering or harvesting, then confirm a named contract after reviewing the itemized wage.
- Watch the selected NPC enter from the farm's left boundary, walk between reachable crops, water them, return to that entrance, and resume their prior state.
- Watch a selected NPC harvest one mature crop through the vanilla crop logic, carry every exact output, deliver it to ranked ordinary farm chests, and return safely.
- Route harvest outputs by exact stack compatibility, same item, same category, then real acceptable capacity; use persistent team overflow before any explicit emergency ground drop.
- Reserve the six-hour maximum on dispatch, charge each started work hour (minimum one), and refund the unused authorization on return.
- Water crops.
- Harvest one mature crop per visible named contract.
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

Select a green available row, choose watering or harvesting, then review the six-hour maximum authorization, one-hour minimum callout, friendship multiplier, workday or rest-day multiplier, baseline efficiency, and overtime policy. Confirm while standing on the main farm. The mod rechecks the NPC, funds, target, and required paths before changing money or NPC state. Watering handles every reachable dry crop before 9 PM. Harvesting handles one mature crop, visibly walks to the best eligible chest for each exact output, and falls back to persistent overflow without using player inventory or auto-shipping. Both tasks enter and return through the farm's left boundary. Settlement charges each started work hour up to six and refunds the unused authorization. Rest-day confirmation explicitly authorizes triple pay.

Named contracts must start by 4:00 PM and stop at the 10:00 PM safety boundary. Only one named contract can run at a time. The legacy instant executor remains available through the `efo_work` SMAPI console command for isolated testing.

In multiplayer, both the host and a connected farmhand can request watering or harvest contracts. A farmhand confirmation sends a versioned request to the host and never mutates money, NPCs, crops, cargo, or chests locally. The host revalidates the player, worker, funds, target, paths, and save/day/version identity; accepted contracts, phase changes, cargo/transfer state, actions, and final results are synchronized back to peers. Retries reuse the same processed result instead of charging or dispatching twice.

New configurations default to `K`. If an older config still uses `H` while UI Info Suite 2 is installed, the mod shows a conflict warning; change `OpenMenuKey` to `K` or use `efo_roster`.

You can also use SMAPI console commands:

```text
efo_work
efo_roster
efo_overflow
efo_netstatus
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

`efo_toggle clear` is retained for config compatibility, but reports that debris cleanup is temporarily disabled.

`efo_overflow` opens the persistent team inventory used only when no eligible farm chest can accept a harvest result. Emergency ground drops are announced explicitly.

`efo_netstatus` reports the local network role, host session, active contract, pending request, processed-request count, and synchronized state version for multiplayer acceptance testing.

### Configuration

If Generic Mod Config Menu is installed, Evil Farm Owner appears in its Mod Options list with a localized roster-hotkey setting. The integration is optional; direct `config.json` editing remains supported. Work area, tasks, and wages belong to each named NPC contract instead of a global player-centered scan, so legacy prototype controls aren't exposed in the menu.

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
| `DepositHarvestToNearestChest` | Legacy prototype field; named harvest always uses lossless ranked delivery | `false` |

---

## 中文说明

### 这个 Mod 是做什么的？

`邪恶农场主` 是一个《星露谷物语》SMAPI Mod。它的核心玩法是：你花钱雇佣农工，把重复农活交给他们处理。

当前版本可以查看具名 NPC 候选人及其当前不可雇佣原因，并给有空的成年 NPC 指定一份可见执行的浇水或收获合同。工资会逐项显示；收获成果会无损交付；NPC 完成后安全返回原位置并恢复日程。

### 当前功能

- 按 `K` 打开具名工人候选名单。
- 显示 NPC 当前是否可雇佣，以及不能雇佣的明确原因。
- 选择浇水或收获，查看逐项工资后确认具名合同。
- 观看所选 NPC 从农场左侧边界进入、逐格走到可到达的干旱作物旁浇水、从同一入口返回并恢复原状态。
- 观看所选 NPC 通过游戏原生作物逻辑收获一株成熟作物，携带每一件实际产物、送入按规则选择的普通农场箱子，再安全返回。
- 收获成果依次按“可堆叠同品质、同物品、同类别、真实可用容量”选择箱子；普通箱不可用时进入持久化队伍溢出仓，最后才会明确警告并掉落。
- 出工时预留六小时最高工资，完成后按已开始的工作小时计费（最少一小时）并退还未使用的授权金额。
- 自动浇水。
- 每份可见具名收获合同收获一株成熟作物。
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

选择绿色“当前可雇佣”行，再选择浇水或收获，可以查看六小时最高授权金额、一小时最低出工费、好感度系数、工作日或休息日系数、基础效率和加班政策。站在主农场确认后，模组会重新检查 NPC、资金、目标以及所需路线；所有检查通过后才预留工资并让 NPC 出工。浇水会处理晚上 9 点前全部可到达的缺水作物；收获会处理一株成熟作物，逐件走到最合适的箱子交付，无法入箱时进入持久化溢出仓，不会借用玩家背包或自动出售。两种任务都从农场左侧入口进入并返回。工资按已开始的小时结算，未使用的授权金额会退还；休息日按钮会明确要求授权三倍工资。

具名合同最迟必须在下午 4:00 开始，并受晚上 10:00 安全停止时间约束；同一时间只能执行一份。旧版瞬时执行器仍保留在 SMAPI 控制台命令 `efo_work` 中，仅用于隔离测试。

多人游戏中，主机和已连接的农场工都可以请求浇水或收获合同。农场工确认后只会向主机发送带版本的请求，本地绝不会直接改动金币、NPC、作物、货物或箱子。主机会重新检查玩家、工人、资金、目标、路线以及存档、日期和版本身份，再把已接受合同、阶段、货物与转移状态、动作和最终结果同步给其他玩家；重试会返回同一处理结果，不会重复扣钱或重复出工。

新配置默认使用 `K`。如果旧配置仍使用 `H` 且安装了 UI Info Suite 2，模组会显示冲突警告；请把 `OpenMenuKey` 改成 `K`，或使用 `efo_roster`。

也可以使用 SMAPI 控制台命令：

```text
efo_work
efo_roster
efo_overflow
efo_netstatus
efo_status
efo_toggle water
efo_toggle harvest
efo_toggle clear
efo_toggle fertilize
efo_toggle plant
```

为了兼容已有配置，仍保留 `efo_toggle clear`；执行时会提示杂物清理暂时停用。

`efo_overflow` 用于打开持久化队伍溢出仓；只有普通农场箱子都无法接收产物时才会使用它。紧急地面掉落一定会明确提示。

`efo_netstatus` 用于多人验收，显示本地网络角色、主机会话、活动合同、待处理请求、已处理请求数量和同步状态版本。

### 配置文件

如果安装了 Generic Mod Config Menu，“邪恶农场主”会出现在它的“MOD 选项”列表中，并提供本地化的候选名单快捷键设置。该集成是可选的，仍可直接编辑 `config.json`。工作区域、任务和工资属于每份具名 NPC 合同，不使用以玩家为中心的全局扫描范围，因此菜单不会暴露旧原型设置。

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
| `DepositHarvestToNearestChest` | 旧版原型字段；具名收获始终使用无损的规则入库 | `false` |

---

## Current Status

### Plan List

- Validate the visible one-crop named harvest and lossless delivery flow in a live save.
- Validate host-authoritative network multiplayer with a real host and remote farmhand for watering and harvest delivery.
- Add a warehouse or office anchor for storage and hiring.

### Idea List

- Sort chests by item type, quality, season, or custom labels.
- Refill machines and collect finished products.
- Add animal care jobs.
- Add worker tiers with different wages and speeds.
- Add flavor text, worker complaints, and capitalist farm-owner jokes.

### Bug List / Known Limitations

- Host-authoritative multiplayer is implemented but still requires the release-gate test with a real remote host/farmhand session; split-screen alone is not accepted as proof.
- Named watering and one-crop harvest contracts are visible; legacy `efo_work` tasks still execute instantly.
- Every peer must install the same Evil Farm Owner version; mismatched protocol/save/day/player/task messages are rejected without mutation.
- Debris cleanup is disabled until normal drops and a safe delivery destination are implemented.
- Named harvest supports ordinary player-owned main-farm chests and persistent overflow; special or modded chest subclasses are excluded for safety.
- `DepositHarvestToNearestChest` remains a legacy prototype config field; named harvest routing is always lossless and does not use this switch.
- Planting currently uses the first available seed stack in the player inventory.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

License not yet specified.
