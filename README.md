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
  Pay named NPC farmhands to water crops and deliver harvested produce.
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
- Watch the selected NPC enter from a genuine external farm entrance, walk between reachable crops, water them, return to that entrance, and resume their prior state.
- Watch a selected NPC harvest every reachable mature crop through the vanilla crop logic, carry every exact output, deliver it to ranked ordinary farm chests, and return safely.
- Route around kegs, chests, machines, fences, trellis crops, and other occupied tiles without moving or destroying them; dynamically retry another interaction edge if a route becomes blocked.
- Route harvest outputs by exact stack compatibility, same item, same category, then real acceptable capacity; use persistent team overflow before any explicit emergency ground drop.
- Reserve the six-hour maximum on dispatch, charge each started work hour (minimum one), and refund the unused authorization on return.
- Let both the host and connected farmhands request contracts through host-authoritative multiplayer synchronization.
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

Select a green available row, choose watering or harvesting, then review the six-hour maximum authorization, one-hour minimum callout, friendship multiplier, workday or rest-day multiplier, baseline efficiency, and overtime policy. Confirm while standing on the main farm. The mod rechecks the NPC, funds, target, and required paths before changing money or NPC state. Watering handles every reachable dry crop before 9 PM. Harvesting handles every reachable mature crop before 9 PM, visibly walks to the best eligible chest for each exact output, and falls back to persistent overflow without using player inventory or auto-shipping. Both tasks enter and return through a genuine external farm entrance. A single-source shortest-path scan uses reachable adjacent interaction tiles and actual walking cost, so trellis crops and placed objects are routed around rather than altered. Settlement charges each started work hour up to six and refunds the unused authorization. Rest-day confirmation explicitly authorizes triple pay.

Farm travel is always non-destructive. Workers can open gates, dynamically replan after a short movement stall, and safely stop and restore if no object-safe route remains.

The route algorithm and object-safety invariants are documented in [`docs/ROUTE_PLANNING.md`](docs/ROUTE_PLANNING.md).

Named contracts must start by 4:00 PM and stop at the 10:00 PM safety boundary. Only one named contract can run at a time. There is no instant global work command in the release build: all production farm mutation must pass through a named contract.

In multiplayer, both the host and a connected farmhand can request watering or harvest contracts. A farmhand confirmation sends a versioned request to the host and never mutates money, NPCs, crops, cargo, or chests locally. The host revalidates the player, worker, funds, target, paths, and save/day/version identity; accepted contracts, phase changes, cargo/transfer state, actions, and final results are synchronized back to peers. Retries reuse the same processed result instead of charging or dispatching twice.

New configurations default to `K`. If an older config still uses `H` while UI Info Suite 2 is installed, the mod shows a conflict warning; change `OpenMenuKey` to `K` or use `efo_roster`.

You can also use SMAPI console commands:

```text
efo_roster
efo_overflow
efo_netstatus
```

`efo_overflow` opens the persistent team inventory used only when no eligible farm chest can accept a harvest result. Emergency ground drops are announced explicitly.

`efo_netstatus` reports the local network role, host session, active contract, pending request, processed-request count, and synchronized state version for multiplayer acceptance testing.

### Configuration

If Generic Mod Config Menu is installed, Evil Farm Owner appears in its Mod Options list with a localized roster-hotkey setting. The integration is optional; direct `config.json` editing remains supported. Work scope, task rules, and wages belong to each named NPC contract, so there is no player-centered global scan setting.

The config file is created at:

```text
Mods/EvilFarmOwner/config.json
```

| Key | Description | Default |
| --- | --- | --- |
| `OpenMenuKey` | Hotkey for the read-only worker roster | `K` |

---

## 中文说明

### 这个 Mod 是做什么的？

`邪恶农场主` 是一个《星露谷物语》SMAPI Mod。它的核心玩法是：你花钱雇佣农工，把重复农活交给他们处理。

当前版本可以查看具名 NPC 候选人及其当前不可雇佣原因，并给有空的成年 NPC 指定一份可见执行的浇水或收获合同。工资会逐项显示；收获成果会无损交付；NPC 完成后安全返回原位置并恢复日程。

### 当前功能

- 按 `K` 打开具名工人候选名单。
- 显示 NPC 当前是否可雇佣，以及不能雇佣的明确原因。
- 选择浇水或收获，查看逐项工资后确认具名合同。
- 观看所选 NPC 从真实农场边界入口进入、逐格走到可到达的干旱作物旁浇水、从同一入口返回并恢复原状态。
- 观看所选 NPC 通过游戏原生作物逻辑收获全部可到达的成熟作物，携带每一件实际产物、送入按规则选择的普通农场箱子，再安全返回。
- 木桶、箱子、机器、栅栏、棚架作物和其他占用格都作为不可破坏的障碍绕行；途中路线失效时会换一个交互边重新规划，不会移动或清除摆件。
- 收获成果依次按“可堆叠同品质、同物品、同类别、真实可用容量”选择箱子；普通箱不可用时进入持久化队伍溢出仓，最后才会明确警告并掉落。
- 出工时预留六小时最高工资，完成后按已开始的工作小时计费（最少一小时）并退还未使用的授权金额。
- 主机和已连接的农场工都可以通过主机权威多人同步请求合同。
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

选择绿色“当前可雇佣”行，再选择浇水或收获，可以查看六小时最高授权金额、一小时最低出工费、好感度系数、工作日或休息日系数、基础效率和加班政策。站在主农场确认后，模组会重新检查 NPC、资金、目标以及所需路线；所有检查通过后才预留工资并让 NPC 出工。浇水会处理晚上 9 点前全部可到达的缺水作物；收获会处理晚上 9 点前全部可到达的成熟作物，逐件走到最合适的箱子交付，无法入箱时进入持久化溢出仓，不会借用玩家背包或自动出售。两种任务都从真实农场边界入口进入并返回。每次动作后只做一次单源最短路扫描，以可到达的相邻交互格和实际步行成本选择下一个目标，因此会绕过棚架作物和玩家摆件。工资按已开始的小时结算，未使用的授权金额会退还；休息日按钮会明确要求授权三倍工资。

农场内的移动始终禁止破坏物品；工人可以打开栅栏门，在短暂卡住后动态重新规划，并在不存在安全路线时停止工作和恢复原状态。

具名合同最迟必须在下午 4:00 开始，并受晚上 10:00 安全停止时间约束；同一时间只能执行一份。发布构建不提供瞬时全局工作命令：所有生产环境农场变更都必须经过具名合同。

多人游戏中，主机和已连接的农场工都可以请求浇水或收获合同。农场工确认后只会向主机发送带版本的请求，本地绝不会直接改动金币、NPC、作物、货物或箱子。主机会重新检查玩家、工人、资金、目标、路线以及存档、日期和版本身份，再把已接受合同、阶段、货物与转移状态、动作和最终结果同步给其他玩家；重试会返回同一处理结果，不会重复扣钱或重复出工。

新配置默认使用 `K`。如果旧配置仍使用 `H` 且安装了 UI Info Suite 2，模组会显示冲突警告；请把 `OpenMenuKey` 改成 `K`，或使用 `efo_roster`。

也可以使用 SMAPI 控制台命令：

```text
efo_roster
efo_overflow
efo_netstatus
```

`efo_overflow` 用于打开持久化队伍溢出仓；只有普通农场箱子都无法接收产物时才会使用它。紧急地面掉落一定会明确提示。

`efo_netstatus` 用于多人验收，显示本地网络角色、主机会话、活动合同、待处理请求、已处理请求数量和同步状态版本。

### 配置文件

如果安装了 Generic Mod Config Menu，“邪恶农场主”会出现在它的“MOD 选项”列表中，并提供本地化的候选名单快捷键设置。该集成是可选的，仍可直接编辑 `config.json`。工作范围、任务规则和工资属于每份具名 NPC 合同，不存在以玩家为中心的全局扫描设置。

配置文件位置：

```text
Mods/EvilFarmOwner/config.json
```

| 配置项 | 说明 | 默认值 |
| --- | --- | --- |
| `OpenMenuKey` | 打开只读工人名单的快捷键 | `K` |

---

## Current Status

### Plan List

- Validate the visible multi-crop named harvest, obstacle-safe routing, and lossless delivery flow in a live save.
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
- Named watering and multi-crop harvest contracts are visible; debris cleanup, fertilizing, planting, and automatic shipping are not part of v0.1.0.
- Every peer must install the same Evil Farm Owner version; mismatched protocol/save/day/player/task messages are rejected without mutation.
- Named harvest supports ordinary player-owned main-farm chests and persistent overflow; special or modded chest subclasses are excluded for safety.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

License not yet specified.
