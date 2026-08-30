<p align="center">
  <img src="assets/banner-v4-lowres.png" alt="Evil Farm Owner banner" width="820">
</p>

<h1 align="center">
  <img src="assets/icon-v2.png" alt="Evil Farm Owner icon" width="48">
  Evil Farm Owner
</h1>

<p align="center">
  <strong>邪恶农场主</strong> · Stardew Valley SMAPI Mod · v0.5.0
</p>

<p align="center">
  雇佣有空的镇民，让他们真正走进农场完成一整班农活。<br>
  Hire available townspeople to walk onto your farm and complete a full work shift.
</p>

<p align="center">
  <a href="https://github.com/Aveouter/EvilFarmOwner/releases/latest">下载最新版 / Download</a> ·
  <a href="#中文">中文</a> ·
  <a href="#english">English</a> ·
  <a href="https://github.com/Aveouter/EvilFarmOwner/issues">问题反馈 / Issues</a>
</p>

---

## 中文

### 简介

“邪恶农场主”允许你花金币雇佣当前有空的成年 NPC。工人会从农场边界入口出现，绕开箱子、机器、栅栏和棚架作物，完成工作后返回入口。

参加节日、剧情、工作日程或其他受保护活动的 NPC 不会出现在候选名单中。Mod 不会为了雇佣而中断原版活动，也不会让儿童工作。

### 快速开始

1. 安装 [SMAPI](https://smapi.io/) 4.0 或更高版本。
2. 从 [Releases](https://github.com/Aveouter/EvilFarmOwner/releases/latest) 下载 ZIP。
3. 解压后，把 `EvilFarmOwner` 文件夹放进游戏的 `Mods` 文件夹。
4. 通过 SMAPI 启动游戏，载入存档并站在主农场。
5. 按 `K`，选择工人，确认总工资和产物去向，然后开始班次。默认上限为 1 人，主机可在设置中提高到 4 人。

需要 Stardew Valley 1.6 或更高版本。Generic Mod Config Menu 是可选的，可用于修改按键、工资和综合班次偏好。

### 一次雇佣会做什么？

一份合同会按以下顺序完成所有当前可执行的工作：

1. **收获**：成熟作物、果树果实、普通或重型树液采集器、部分可安全收取的原版机器、蟹笼、鱼塘、浆果丛和茶树。
2. **浇水**：所有能够安全到达的缺水作物。
3. **动物照料**：进入畜棚和鸡舍，抚摸动物、从自有筒仓取干草填食槽，并收集鸡蛋、牛奶和羊毛。
4. **整理箱子**：根据箱子里已有的物品，把普通农场箱子中的完整物品堆叠按同物品和同类别整理。
5. **最终复查**：再进行一次有上限的检查，处理班次途中刚刚变为可执行的工作。

没有工作的阶段会自动跳过。个别目标无法到达时，工人会尝试换路或跳过；安全条件失效时合同会明确停止，不会破坏农场摆设。

综合班次把主农场、温室和自有农场建筑内部视为一组连续工作地点：主农场始终包含，温室与棚屋、农场洞穴等内部地点可在设置中分别启用。收获物从室内带回主农场后，才进入同一套分类箱或玩家背包交付流程。畜棚和鸡舍仍由动物照料阶段处理；本版本不处理姜岛农场。

使用分类箱时，NPC 会连续收集产物，按照原版可堆叠规则模拟携带空间，达到 12 个实际物品格后再集中送货；同物品同品质会共用一格，而不是每件作物各占一格。送货时按目标箱分组，每个箱子只走访和加锁一次；目标已经采完或接近停止采集时间时也会立即送货。玩家背包模式仍即时交付，因为它不需要 NPC 往返。

玩家离开农场后，NPC 仍会在农场继续工作。同一时间只执行一份合同，但一份合同可以包含 1–4 名工人。多人班次会让所有工人分担收获和浇水；动物照料与箱子整理各只交给一人，避免重复操作。

### 工资

- 名单会显示 NPC 的好感度和今日最高授权工资。
- 默认基础工资为每小时 100g；好感度越高，工资越低。
- 普通班次最多按六个已开始的小时结算。
- 休息日需要明确授权三倍工资。
- 开工时暂时预留最高工资，工人返回后按实际工时收费并退回剩余金币。
- 如果班次在任何工作完成前失败或停止，最低一小时工资也不会收取，预留金币将全额退回。

安装可选的 Generic Mod Config Menu 后，主机可以调整基础时薪（50g–500g）、好感度影响（0%–40%）、工人差异强度（0%–200%）、休息日判定方式与工资倍率（1.0–5.0 倍）、同时雇佣上限（1–4 人）和默认产物去向。收获、浇水、动物照料、箱子整理可以多选；温室和其他农场建筑内部也可分别启用。至少保留一个工作阶段；已经开工的班次不会被中途修改。

### 收获物去向

确认雇佣时可以选择：

- **分类箱**（默认设置）：寻找主农场上的普通玩家箱子，依次优先同品质可堆叠物品、同物品、同 `Item.Category`，最后才使用容量最大的空箱。
- **玩家背包**：请求合同的玩家仍在线、位于主农场且背包能完整放下当前物品时直接接收。

一份合同开始后不会偷偷切换目的地。所选位置无法接收完整物品堆叠时，合同会安全停止；已经取得的物品会保存在背包、箱子、可见掉落或恢复存储中，不会静默消失，也不会自动出售。

### 自动合同

主机可以从工人名单底部打开“自动合同”，设置：

- 首选工人；
- 明确允许的替补名单；
- 普通日工资上限；
- 是否允许休息日三倍工资。

授权从下一个游戏日开始。主机在上午 6:10 到下午 4:00 之间进入主农场时，Mod 每天最多尝试一次。自动合同会在授权名单中按工作效率、工资、好感度和姓名稳定选择最多 4 名合适工人，并与手动雇佣执行同一套完整班次；NPC、预算、路线或储存条件不合适时会跳过当天。

### 多人游戏

主机和农场工都可以申请合同，但只有主机修改金币、NPC、作物、动物和箱子。所有玩家必须安装完全相同的 Mod 版本。

协议包含重复请求防护、每名工人的状态同步、断线重连和主机重启后的历史结果恢复。多人班次的任务认领、路线等待、货物和工资都分别记录，单个工人失败不会重复收取其他人的工资。

维护者选择在未执行真实双进程和完整实机矩阵的情况下发布本版本，因此这些场景仍属于未验证状态。重要存档请先备份；报告联机问题时请同时提供主机与农场工的 SMAPI 日志。

### 常用命令

大多数玩家只需要按 `K`。排查问题时可在 SMAPI 控制台使用：

```text
efo_roster       打开工人名单
efo_auto         打开自动合同
efo_report       查看最近一次合同结果
efo_overflow     查看因储存失败而保留的物品
efo_quarantine   查看需要人工取回的应急保护物品
efo_netstatus    查看联机合同状态
```

如果旧配置使用 `H` 并与 UI Info Suite 2 冲突，请通过 Generic Mod Config Menu 改回 `K`，或编辑 `Mods/EvilFarmOwner/config.json`。多人游戏中的合同始终使用主机设置；农场工会在联机同步后看到相同的工资预览和默认去向。

### 当前限制

- 暂不支持清理杂物、播种、施肥、自动补充机器原料或自动出售。
- 暂不处理姜岛农场；当前统一范围仅包括主农场、温室和主农场上的自有建筑内部。
- 不接管动物自动采集器；也不收取会在交互时重新计算产物或自动续产的机器。
- 仅支持主农场上的普通玩家箱子，不支持特殊箱子和其他 Mod 添加的箱子。
- 自定义农场如果没有安全路线，合同会拒绝开始或提前停止。
- 2–4 人实机矩阵、强制储存故障矩阵、真实双进程多人和最终发布 ZIP 的游戏内烟雾测试未执行；本版本基于自动测试直接验收。

---

## English

### Overview

Evil Farm Owner lets you pay currently available adult NPCs to work a full farm shift. A worker enters through a farm-boundary entrance, routes around chests, machines, fences, and trellis crops, then returns when the shift ends.

NPCs who are busy with festivals, events, work schedules, or other protected activities are omitted from the roster. The mod never interrupts those activities and never permits child labor.

### Quick start

1. Install [SMAPI](https://smapi.io/) 4.0 or later.
2. Download the ZIP from [Releases](https://github.com/Aveouter/EvilFarmOwner/releases/latest).
3. Extract it and place the `EvilFarmOwner` folder in your game's `Mods` folder.
4. Launch through SMAPI, load a save, and stand on the main farm.
5. Press `K`, select workers, review the combined wage and delivery destination, then confirm. The default limit is one; the host may raise it to four in settings.

Stardew Valley 1.6 or later is required. Generic Mod Config Menu is optional and exposes the hotkey, wage, and complete-shift preferences.

### What does one hire do?

A contract performs every currently ready stage in this order:

1. **Harvest** mature crops, fruit trees, normal and heavy tappers, selected safe vanilla machines, crab pots, fish ponds, berry bushes, and tea bushes.
2. **Water** every safely reachable dry crop.
3. **Care for animals** by entering barns and coops, petting livestock, filling troughs from owned silo hay, and collecting eggs, milk, and wool.
4. **Sort chests** by moving complete stacks among ordinary farm chests according to their existing items and categories.
5. **Reconcile once** with one bounded final pass for work that became ready during the shift.

Empty stages are skipped. The worker replans or skips isolated unreachable targets and stops explicitly when safety conditions change instead of destroying placed objects.

A complete shift treats the main farm, greenhouse, and owned farm-building interiors as one ordered set of work locations. The main farm is always included; the greenhouse and interiors such as sheds and the farm cave can be enabled separately. Interior harvest cargo returns to the main farm before using the same classified-chest or requester-inventory delivery pipeline. Barns and coops remain owned by animal care, and Ginger Island is excluded in this version.

With classified chests selected, the NPC simulates vanilla-compatible stacking and keeps collecting until the cargo occupies 12 actual slots; compatible items share a slot instead of every crop counting separately. Delivery then groups cargo by destination so each chest is visited and locked once. Cargo is also delivered when no target remains or the acquisition cutoff is reached. Requester-inventory delivery remains immediate because it requires no NPC round trip.

NPCs continue working on the farm when the player changes maps. Only one contract runs at a time, but that contract may include one to four workers. All workers can share harvesting and watering; animal care and chest sorting each have one owner to prevent duplicate actions.

### Wages

- The roster shows friendship and today's maximum authorization.
- The default base wage is 100g per hour; higher friendship reduces the rate.
- A normal shift charges at most six started hours.
- Rest-day work requires explicit authorization for triple pay.
- The maximum is reserved at dispatch, then unused gold is refunded after the worker returns.
- A shift which stops before completing any work charges no one-hour minimum and refunds the full reservation.

With the optional Generic Mod Config Menu installed, the host can adjust the base rate (50g–500g), friendship impact (0%–40%), worker-difference strength (0%–200%), rest-day rule and multiplier (1.0x–5.0x), simultaneous-worker limit (1–4), and default harvest destination. Harvesting, watering, animal care, and chest sorting are independent multi-select options; the greenhouse and other farm-building interiors can also be enabled separately. At least one stage remains enabled, and a running shift keeps its starting snapshot.

### Harvest delivery

Choose one destination at confirmation:

- **Classified chests** (default setting): ordinary player chests on the main farm are ranked by compatible quality stack, same item, exact `Item.Category`, then the empty chest with the most capacity.
- **Requester inventory**: delivery succeeds while that player is online on the main farm and can accept the complete current stack.

A running contract never changes destination silently. If the selected destination cannot accept the whole stack, the contract stops safely. Already owned items remain in inventory, a chest, a visible drop, or recovery storage; they are never silently deleted or automatically shipped.

### Automatic contracts

The host can open “Automatic contract” from the bottom of the roster and choose a preferred worker, an explicit substitute pool, a regular-day wage cap, and whether rest-day triple pay is allowed.

Starting the next game day, the mod tries at most once when the host enters the main farm between 6:10 AM and 4:00 PM. It selects up to four authorized workers in a stable efficiency, wage, friendship, and name order. Automatic hiring uses the same complete shift as manual hiring and skips the day when worker, budget, route, or storage checks fail.

### Multiplayer

Hosts and farmhands may request contracts, but only the host changes money, NPCs, crops, animals, and storage. Every player must install the exact same mod version.

The protocol includes duplicate-request protection, per-worker state synchronization, reconnect handling, and prior-result recovery after a host restart. Multi-worker targets, route waits, cargo, and billing are tracked independently so one worker's failure does not charge the others twice.

The maintainer chose to publish without a real two-process run or the full live matrix, so those scenarios remain unverified. Back up important saves and include both host and farmhand SMAPI logs in multiplayer reports.

### Useful commands

Most players only need `K`. For troubleshooting, the SMAPI console provides:

```text
efo_roster
efo_auto
efo_report
efo_overflow
efo_quarantine
efo_netstatus
```

If an old `H` hotkey conflicts with UI Info Suite 2, change it to `K` through Generic Mod Config Menu or edit `Mods/EvilFarmOwner/config.json`. In multiplayer, contracts always use the host's settings; farmhands receive the same wage preview and default destination after synchronization.

### Current limitations

- No debris clearing, planting, fertilizing, automatic machine refilling, or automatic shipping.
- Ginger Island is excluded; the unified scope currently covers only the main farm, greenhouse, and owned buildings on the main farm.
- Auto-grabbers and machines which recalculate or automatically restart on collection are excluded.
- Only ordinary player-owned main-farm chests are supported; special and modded chests are excluded.
- A custom farm without a safe route may reject or stop a contract.
- The live 2–4 worker matrix, forced-storage fault matrix, real two-process multiplayer run, and final in-game smoke test of the published ZIP were not performed; this version was accepted from automated evidence.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs
- Optional: Generic Mod Config Menu

## License

[MIT](LICENSE) © 2026 Aveouter.
