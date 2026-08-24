<p align="center">
  <img src="assets/banner-v4-lowres.png" alt="Evil Farm Owner banner" width="820">
</p>

<h1 align="center">
  <img src="assets/icon-v2.png" alt="Evil Farm Owner icon" width="48">
  Evil Farm Owner
</h1>

<p align="center">
  <strong>邪恶农场主</strong> · Stardew Valley SMAPI Mod · v0.1.0-beta.1
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

`v0.1.0-beta.1` is a public test release. Named watering and harvesting have production implementations, and multiplayer uses a host-authoritative protocol, but the real remote host/farmhand matrix and forced storage-recovery matrix are still explicitly unverified. Back up the save before testing and report issues with both peers' SMAPI logs.

Press one key to review only the named adult NPCs who are currently available for hire. NPCs in protected vanilla activities such as chores, exercise, scripted animation, or movement pauses are omitted instead of being interrupted. An available adult NPC can be assigned a visible watering or harvest contract with an itemized wage, lossless harvest delivery, and a protected return to their original schedule position.

### Current Features

- Open the currently available named-worker roster with the `K` key; each compact row shows friendship, today's hourly wage, and the six-hour maximum.
- Omit NPCs who cannot be hired safely at that moment; their availability is reevaluated each time the roster opens.
- Choose watering or harvesting, then confirm a named contract after reviewing the itemized wage.
- Watch the selected NPC prefer the right/east farm entrance, safely fall back to another genuine boundary entrance when needed, walk between reachable crops, return to the selected entrance, and resume their prior state.
- Watch a selected NPC harvest every reachable mature crop through the vanilla crop logic, carry every exact output, deliver it to the contract-selected destination, and return safely. Classified ordinary farm chests are the default; requester inventory is an explicit alternative.
- Route around kegs, chests, machines, fences, trellis crops, and other occupied tiles without moving or destroying them; dynamically retry another interaction edge if a route becomes blocked.
- Infer each chest's purpose from its contents and rank it by exact compatible stack, same item, exact game item category, then empty chest. Prefer purer category chests and use stable tile order so the worker's position cannot change an otherwise equal choice. A candidate must accept the complete stack; if no classified or empty chest can do that, the contract stops. Already harvested cargo uses the lossless emergency path, but work never continues from that fallback.
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

Select a green available row, choose watering or harvesting, then review the six-hour maximum authorization, one-hour minimum callout, friendship multiplier, workday or rest-day multiplier, the selected worker's task efficiency, and overtime policy. A harvest preview also shows a delivery choice. Classified chests are the default for manual and automatic contracts; clicking the row explicitly selects requester inventory. The host snapshots that choice, and one contract never silently switches destinations. If requester inventory is selected and the requester leaves the farm or cannot accept a complete output stack, the contract stops before another crop is changed and preserves the exact current cargo through the emergency path. Confirm while standing on the main farm. The mod rechecks the NPC, funds, target, required paths, and—when chests are selected—the presence of an ordinary farm chest before changing money or NPC state. Watering handles every reachable dry crop before 9 PM. Harvesting handles every reachable mature crop before 9 PM and classifies every exact output from chest contents when that destination is selected: compatible stack, same item, exact item category, then empty chest. Category purity and fixed chest coordinates determine the destination before walking distance. The selected chest must accept the complete stack; otherwise the contract stops immediately and preserves only the already harvested cargo through emergency overflow, a visible drop, quarantine, or a validated recovery record. It never auto-ships or continues harvesting after that storage stop. Both tasks prefer the right/east entrance, then try other genuine boundary entrances in a deterministic order when a safe round trip is unavailable, and return through the entrance they selected. A single-source shortest-path scan uses live collision, reachable adjacent interaction tiles, and actual walking cost. Ordinary crop tiles remain walkable according to vanilla collision, while trellises and placed objects are routed around rather than altered. If another activity takes control of the worker, the contract waits briefly for full restoration and then releases only this mod's lease without overriding the other controller. Settlement charges each started work hour up to six and refunds the unused authorization. Rest-day confirmation explicitly authorizes triple pay.

The host can select **Automatic contract** at the bottom of the roster to create one save-specific standing authorization. Choose a preferred worker and task, then authorize either that worker alone or the currently listed adults as a fixed substitute pool. The authorization begins on the next game day. Between 6:10 AM and 4:00 PM, the host evaluates it once when entering the main farm: the preferred worker wins when eligible, otherwise only an explicitly saved substitute can be considered. The candidate must still pass every live availability, budget, target, route, and storage preflight. Rest days are off unless the authorization screen separately enables the displayed triple-pay cap; overtime and automatic shipping remain unavailable. The same manager pauses, resumes, replaces, or deletes future authorization and shows the latest selection, rejection reasons, work count, payment, and refund.

Farm travel is always non-destructive. Workers can open gates and dynamically replan after a short movement stall. Before reserving wages, each arrival route is checked with the selected NPC's translated collision bounds and first pixel step; every later crop and chest path repeats that first-step probe before movement starts. A target or delivery phase may fail at most three consecutive routes from the same origin tile. After that, remaining crops are reported unreachable or the harvest contract stops and preserves only its current cargo instead of retrying every destination. If the live controller still cannot leave an entrance, that entire side is excluded and the same contract visibly switches to the next boundary entrance instead of retrying every crop from the blocked tile. The worker safely stops and restores if no object-safe entrance or route remains.

The route algorithm and object-safety invariants are documented in [`docs/ROUTE_PLANNING.md`](docs/ROUTE_PLANNING.md).

Named contracts must start by 4:00 PM and stop at the 10:00 PM safety boundary. Only one named contract can run at a time. There is no instant global work command in the release build: all production farm mutation must pass through a named contract.

In multiplayer, both the host and a connected farmhand can request watering or harvest contracts. A farmhand confirmation sends a versioned request to the host and never mutates money, NPCs, crops, cargo, or chests locally. The host revalidates the player, worker, funds, target, paths, and save/day/version identity; accepted contracts, phase changes, cargo/transfer state, actions, and final results are synchronized back to peers. The bounded processed-request ledger and latest per-player results are saved with the host, then rebound to a fresh network session after restart. Retries reuse the same processed result instead of charging or dispatching twice. An incompatible, inconsistent, or unclean recovery record disables new contracts rather than guessing.

New configurations default to `K`. If an older config still uses `H` while UI Info Suite 2 is installed, the mod shows a conflict warning; change `OpenMenuKey` to `K` or use `efo_roster`.

You can also use SMAPI console commands:

```text
efo_roster
efo_overflow
efo_quarantine
efo_netstatus
efo_report
efo_auto
```

`efo_overflow` opens the persistent team inventory used only to preserve cargo already captured when a storage failure stops the contract. It is not a normal destination and never permits more harvesting. Emergency ground drops are announced explicitly and placed at the on-farm requester or a collision-free farmhouse/selected-entrance delivery tile before the worker position is considered.

`efo_quarantine` opens the separate persistent emergency inventory used only when both ordinary overflow and a visible drop cannot be verified. Every quarantined stack carries its transfer ID. If even that inventory is temporarily unavailable, the host stores a size-bounded validated recovery record, blocks new harvest contracts, and retries exact reconstruction before allowing further harvest work. Day end, ordinary saving, and initial save creation recheck this ownership before the game writes the save; any transient remainder is forced into the private team quarantine before the contract can settle.

`efo_netstatus` reports the local network role, host session, active contract, pending request, processed-request count, replay recovery health, host quarantine health, and synchronized state version for multiplayer acceptance testing.

`efo_report` prints the current player's latest authoritative named-contract result without rerunning work or charging wages. It includes the worker, task, status or stop reason, completed count, grouped item/quality totals, destinations, and billing. Host reports survive a clean save/reload through the bounded recent-result ledger; farmhands read the latest validated host result synchronized for them.

`efo_auto` opens the same host-only automatic-contract manager, including while a named contract is already running. Pausing or deleting affects only future dispatch and never interrupts the active contract's delivery or settlement.

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

`v0.1.0-beta.1` 是公开测试版本。具名浇水、收获以及主机权威多人协议均已有生产实现，但真实远程主机/农场工矩阵和强制储存恢复矩阵仍明确标记为“未验证”。测试前请备份存档；反馈问题时请同时提供两端 SMAPI 日志。

当前名单只显示此刻可以雇佣的具名成年 NPC。正在做家务、锻炼、脚本动画或处于移动暂停等原版活动的 NPC 会直接从名单中隐藏，不会被强行中断。有空的成年 NPC 可以接受可见执行的浇水或收获合同；工资会逐项显示，收获成果会无损交付，NPC 完成后安全返回原位置并恢复日程。

### 当前功能

- 按 `K` 打开当前可雇佣工人名单；紧凑列表直接显示好感度、今日时薪和六小时最高工钱。
- 不显示此刻不能安全雇佣的 NPC；每次打开名单都会重新判断可用性。
- 选择浇水或收获，查看逐项工资后确认具名合同。
- 观看所选 NPC 优先从农场右侧入口进入；右侧没有安全往返路线时使用其他真实边界入口，逐格完成可到达的农活，再从所选入口返回并恢复原状态。
- 观看所选 NPC 通过游戏原生作物逻辑收获全部可到达的成熟作物，携带每一件实际产物、送到合同指定的目的地，再安全返回。默认使用分类普通农场箱，也可显式选择请求者背包。
- 木桶、箱子、机器、栅栏、棚架作物和其他占用格都作为不可破坏的障碍绕行；途中路线失效时会换一个交互边重新规划，不会移动或清除摆件。
- 根据箱内物品推断箱子用途，依次按“同品质可堆叠、同物品、游戏中的同一物品类别、空箱”选择。类别越纯、匹配格越多越优先；最后用固定箱子坐标消除 NPC 位置造成的跳箱。候选箱必须一次容纳完整堆叠；没有匹配箱或足够空间时立即终止合同。已经采下的货物进入无损应急流程，但不会因此继续收获。
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

选择绿色“当前可雇佣”行，再选择浇水或收获，可以查看六小时最高授权金额、一小时最低出工费、好感度系数、工作日或休息日系数、所选工人在该任务上的效率和加班政策。收获预览还会显示交付目的地：手动和自动合同默认使用分类箱，点击该行才会显式选择请求者背包。主机会快照这项选择，同一合同绝不会在两者之间悄悄切换；若选择背包后请求者离开农场或背包无法完整接收当前堆叠，合同会在改变下一株作物前停止，并通过应急路径保全当前货物。站在主农场确认后，模组会重新检查 NPC、资金、目标、所需路线；选择箱子时还会检查是否存在普通农场箱子。所有检查通过后才预留工资并让 NPC 出工。浇水会处理晚上 9 点前全部可到达的缺水作物；收获会处理晚上 9 点前全部可到达的成熟作物，并在选择箱子时根据箱内物品按“同品质可堆叠、同物品、游戏中的同一物品类别、空箱”分类。类别纯度和固定箱子坐标先于步行距离决定目标。候选箱必须完整容纳当前堆叠；否则合同立即终止，只把已经采下来的货物保存在应急溢出仓、明确可见的地面掉落、隔离仓或经过验证的恢复记录中，不会继续收获，也绝不会自动出售。两种任务都优先从农场右侧入口进入；右侧无法安全往返时，才按确定顺序尝试其他真实边界入口，完成后从实际选中的入口返回。每次动作后只做一次基于实时碰撞的单源最短路扫描，以可到达的相邻交互格和实际步行成本选择下一个目标；普通作物格按原版规则可以通行，棚架作物和玩家摆件则会绕行。若其他活动接管工人，合同会短暂等待完整恢复，之后只归还本 Mod 的租约，不会覆盖对方控制器。工资按已开始的小时结算，未使用的授权金额会退还；休息日按钮会明确要求授权三倍工资。

主机可以点击名单底部的“自动合同”，为当前存档创建一份常驻授权。选择首选工人和任务后，可以只授权该工人，也可以把当时名单中明确显示的成年 NPC 固定为替补池；候选池保存后不会自行扩大。授权从下一游戏日开始：主机在上午 6:10 至下午 4:00 第一次进入主农场时只评估一次，首选工人符合条件时必定优先，否则只会考虑已批准替补。实际候选仍须重新通过 NPC 活动、预算、目标、路线和储存前置检查。休息日默认跳过，只有单独打开并接受页面显示的三倍工资上限后才会执行；自动加班和自动出售始终关闭。同一界面可以暂停、恢复、替换或删除未来授权，并显示最近选择、拒绝原因、工作数量、实付与退款。

农场内的移动始终禁止破坏物品；工人可以打开栅栏门，在短暂卡住后动态重新规划。预留工资前，系统会用所选 NPC 平移到入口后的真实碰撞框检查第一像素步；之后每条作物和箱子路线在启动前也会重新检查第一步。相同起点的目标或交付路线最多连续失败三次；达到上限后，剩余作物会明确报告为不可达，或直接终止收获合同并只保全当前货物，不再遍历重试所有目的地。如果实机控制器仍无法离开入口，合同会排除整个入口侧并可见地切换到下一个边界入口，不会站在原地逐株重试；不存在安全入口或路线时会停止工作并恢复原状态。

具名合同最迟必须在下午 4:00 开始，并受晚上 10:00 安全停止时间约束；同一时间只能执行一份。发布构建不提供瞬时全局工作命令：所有生产环境农场变更都必须经过具名合同。

多人游戏中，主机和已连接的农场工都可以请求浇水或收获合同。农场工确认后只会向主机发送带版本的请求，本地绝不会直接改动金币、NPC、作物、货物或箱子。主机会重新检查玩家、工人、资金、目标、路线以及存档、日期和版本身份，再把已接受合同、阶段、货物与转移状态、动作和最终结果同步给其他玩家。主机会随存档保存有界请求账本和每位请求者的最近结果，重启后把它们绑定到新的网络会话；重试只返回原结果，不会重复扣钱或出工。若恢复记录不兼容、内部不一致或保存时仍有未收尾合同，模组会禁用新合同，而不是猜测恢复。

新配置默认使用 `K`。如果旧配置仍使用 `H` 且安装了 UI Info Suite 2，模组会显示冲突警告；请把 `OpenMenuKey` 改成 `K`，或使用 `efo_roster`。

也可以使用 SMAPI 控制台命令：

```text
efo_roster
efo_overflow
efo_quarantine
efo_netstatus
efo_report
efo_auto
```

`efo_overflow` 用于打开持久化队伍溢出仓；它只保全因储存失败而终止合同时已经采下的货物，不是普通目标，也不会让工人继续收获。紧急地面掉落一定会明确提示，并优先落在仍在农场的请求者处；否则选择农舍交付区或本次入口附近无碰撞的空格，最后才考虑工人位置。

`efo_quarantine` 用于打开独立的持久应急隔离仓；仅在普通溢出和明确掉落都无法验证时使用。每一组隔离物品都保留转移 ID。若隔离仓也暂时不可用，主机会保存经过大小限制和验证的恢复记录、禁止新的收获合同，并在允许后续收获前重试精确恢复。日终、普通保存和首次创建存档都会在写盘前再次核对货物所有权；任何仍处于临时合同中的余货都会先强制进入私有队伍隔离仓，合同才能结算。

`efo_netstatus` 用于多人验收，显示本地网络角色、主机会话、活动合同、待处理请求、已处理请求数量、请求账本恢复状态、主机隔离仓恢复状态和同步状态版本。

`efo_report` 只读显示当前玩家最近一份主机权威具名合同结果，不会重新执行工作或扣费。内容包括工人、任务、完成或停止原因、完成数量、按物品与品质汇总的产物、各存放去向以及工资结算。主机的最近结果会随干净存档恢复；农场工查看的是主机同步并验证后的本人最近结果。

`efo_auto` 用于打开同一个主机专用自动合同管理界面；即使当前具名合同正在执行也可以打开。暂停或删除只影响未来派工，绝不会中断当前合同的交付与结算。

### 配置文件

如果安装了 Generic Mod Config Menu，“邪恶农场主”会出现在它的“MOD 选项”列表中，并提供本地化的可雇佣工人名单快捷键设置。该集成是可选的，仍可直接编辑 `config.json`。工作范围、任务规则和工资属于每份具名 NPC 合同，不存在以玩家为中心的全局扫描设置。

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
- This beta is not the stable `v0.1.0`; use a backed-up save until the remaining multiplayer and forced-recovery gates pass.
- Named watering and multi-crop harvest contracts are visible; debris cleanup, fertilizing, planting, and automatic shipping are not part of v0.1.0.
- Every peer must install the same Evil Farm Owner version; mismatched protocol/save/day/player/task messages are rejected without mutation.
- Named harvest supports content-classified ordinary player-owned main-farm chests. Persistent overflow is emergency preservation after a storage-triggered stop; special or modded chest subclasses are excluded for safety.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

[MIT](LICENSE) © 2026 Aveouter.
