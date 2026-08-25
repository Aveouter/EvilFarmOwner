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
  花钱雇佣镇民，让他们真正走进农场，帮你浇水、收获、照顾动物和整理箱子。<br>
  Hire townspeople to walk onto your farm and help with watering, harvesting, animal care, and chest sorting.
</p>

<p align="center">
  <a href="https://github.com/Aveouter/EvilFarmOwner/releases/latest">下载最新版 / Download</a> ·
  <a href="#中文说明">中文说明</a> ·
  <a href="#english">English</a>
</p>

---

## 中文说明

### 这是一个什么 Mod？

“邪恶农场主”让你用金币雇佣有空的成年 NPC 来做农活。

工人不会瞬间完成任务。他们会从农场入口走进来，在田地和箱子之间移动，完成工作后再离开。正在参加节日、执行剧情或忙于其他事情的 NPC 不会出现在名单中，因此不会被强行打断。

每次雇佣会自动按顺序处理：

- 浇灌所有能够安全到达的缺水作物；
- 收获所有能够安全到达的成熟作物，以及树上已经就绪的普通或重型树液采集器产物；
- 收取能够安全完成原版状态重置的普通生产机器成品；
- 进入畜棚和鸡舍，抚摸动物、用自有干草补满食槽，并收集鸡蛋、牛奶和羊毛；
- 按箱子里已有的物品整理普通箱子；
- 设置每天自动尝试执行的完整农活班次。

### 主要特点

- 按 `K` 打开工人名单，查看好感度、今日时薪和最高授权工资。
- 工资会受到好感度影响；休息日工作需要明确同意三倍工资。
- 工人会绕开箱子、木桶、机器、栅栏和棚架作物，不会为了赶路破坏农场摆设。
- 遇到临时障碍时会尝试换路；个别目标无法到达时会跳过并继续其他工作。
- 收获前可以选择把产物交给玩家，或送进农场里的分类箱。
- 箱子分类会优先寻找已有相同物品的箱子，再参考物品种类。
- 如果没有位置能放下完整一组物品，合同会安全停止。已经收获的物品不会静默消失，也不会自动出售。
- 出工时最多预留六小时工资，结束后按实际开始的小时结算，并退回未使用的部分。
- 支持中文和英文。

### 安装

1. 安装 [SMAPI](https://smapi.io/)。
2. 从 [Releases](https://github.com/Aveouter/EvilFarmOwner/releases/latest) 下载最新版 ZIP。
3. 解压后，把里面的 `EvilFarmOwner` 文件夹放进游戏的 `Mods` 文件夹。
4. 通过 SMAPI 启动《星露谷物语》。

需要 Stardew Valley 1.6 或更高版本，以及 SMAPI 4.0 或更高版本。

### 怎么玩

1. 载入存档并站在主农场。
2. 按 `K` 打开工人名单。
3. 选择一位绿色显示、当前可以雇佣的 NPC。
4. 查看工资、收获物目的地和完整工作范围，确认雇佣。
5. 工人会依次收获、浇水、照顾动物、整理箱子；当前没有工作的阶段会自动跳过。
6. 等待工人完成整个班次并返回。

同一时间只能执行一份合同。合同最迟需要在下午 4:00 前开始，并会在晚上 10:00 前安全结束。

如果名单为空，通常表示镇民此刻都在忙。稍等一段游戏时间后重新打开名单即可。

### 收获物会放到哪里？

确认雇佣时可以为本班次的收获物选择：

- **分类箱**：默认选项。工人会根据箱子里已经存放的内容寻找合适位置。
- **玩家背包**：适合玩家仍在农场、并且背包有足够空间的情况。

一份合同开始后不会偷偷改变目的地。若所选位置无法完整接收当前物品，工人会停止继续收获，并保护已经拿到的产物。

分类箱仅支持主农场上的普通玩家箱子。出售箱不会被当作备用仓库。

### 箱子整理

箱子整理会观察普通箱子里已经放了什么，并尽量把相同物品、相近类别的物品放在一起。

开始前会先确认整个整理计划能够完成。工人只移动完整的一组物品；如果箱子内容在途中发生变化，或没有足够空间，合同会停止，而不是猜测应该把物品放到哪里。

箱子整理是完整班次的最后一个阶段；没有可执行的整理计划时会自动跳过。

### 自动合同

主机可以在工人名单底部选择“自动合同”，设置一份每天使用的授权：

- 选择首选工人；
- 决定是否允许名单中的其他成年 NPC 作为替补；
- 设置工资预算；
- 决定休息日是否允许三倍工资。

授权从下一个游戏日开始。主机在上午 6:10 到下午 4:00 之间进入主农场时，Mod 会尝试安排一次工作。NPC、预算、作物、路线或储存条件不合适时，当天会跳过，不会强行派工。

### 多人游戏

主机和农场工都可以提出合同请求，但实际的金币、NPC 和农场内容由主机处理。

所有玩家必须安装完全相同版本的 Evil Farm Owner。多人功能尚未完成真实双进程的完整验收，因此建议先备份存档，并在发现问题时同时提供主机和农场工的 SMAPI 日志。

### 常用命令与排查

大多数玩家只需要按 `K`。遇到问题时可以在 SMAPI 控制台使用：

```text
efo_roster       打开工人名单
efo_report       查看最近一份合同的结果
efo_overflow     查看因储存失败而保留的收获物
efo_quarantine   查看应急保护的物品
efo_auto         打开自动合同设置
```

如果旧配置仍使用 `H`，它可能与 UI Info Suite 2 冲突。请在 Generic Mod Config Menu 中把快捷键改为 `K`，或编辑：

```text
Mods/EvilFarmOwner/config.json
```

遇到物品、合同或多人问题时，请保留 SMAPI 日志，并通过 [Issues](https://github.com/Aveouter/EvilFarmOwner/issues) 提交。

### 当前限制

- 暂不支持清理杂物、播种、施肥、自动补充机器原料或自动出售；需要在收取时重新计算或自动续产的机器，以及动物自动采集器内部箱子，不会被本 Mod 接管。
- 暂不支持特殊箱子和其他 Mod 添加的箱子。
- 自动合同与手动雇佣执行同一套完整班次，包括当前可执行的箱子整理。
- 个别农场布局可能没有安全路线；这种情况下合同会拒绝开始或提前停止。
- v0.1.0 是维护者决定提前发布的首个正式版本；强制储存故障的全部场景、真实双进程多人和最终游戏内发布包测试尚未完整验证。重要存档请先备份。

---

## English

### What is Evil Farm Owner?

Evil Farm Owner lets you pay available adult townspeople to help with farm chores.

Workers do not finish jobs instantly. They enter through a farm boundary, walk between crops and chests, do the visible work, and leave when the contract ends. NPCs who are busy with festivals, events, or protected schedules are left alone and do not appear in the hiring list.

Each hire automatically works through these stages in order:

- water every safely reachable dry crop;
- harvest every safely reachable mature crop and every ready normal or heavy tree tapper;
- collect outputs from simple ready vanilla machines whose collection state can be preserved safely;
- enter barns and coops to pet animals, fill troughs from owned silo hay, and collect eggs, milk, and wool;
- organize ordinary farm chests based on their existing contents;
- set up a daily automatic complete farm-work shift.

### Highlights

- Press `K` to see available workers, friendship, today's hourly wage, and the maximum authorization.
- Friendship affects pay. Rest-day work requires explicit approval for triple wages.
- Workers route around chests, machines, fences, kegs, and trellis crops without destroying placed objects.
- Harvest output can go to classified farm chests or directly to the requesting player's inventory.
- A running contract keeps the destination you selected. If it cannot hold the full stack, work stops safely.
- Harvested items are never silently deleted or automatically shipped.
- Up to six hours of wages are reserved when work starts; unused money is refunded afterward.
- English and Chinese are included.

### Install

1. Install [SMAPI](https://smapi.io/).
2. Download the latest ZIP from [Releases](https://github.com/Aveouter/EvilFarmOwner/releases/latest).
3. Extract it and place the `EvilFarmOwner` folder in your game's `Mods` folder.
4. Launch Stardew Valley through SMAPI.

Requires Stardew Valley 1.6+ and SMAPI 4.0+.

### Play

Stand on the main farm, press `K`, choose a green available worker, review the wage and harvest destination, then confirm. The worker harvests, waters, cares for livestock, and sorts ordinary farm chests in that order; stages with no ready work are skipped automatically.

Only one named contract can run at a time. Contracts must start by 4:00 PM and stop safely before 10:00 PM.

For harvesting, classified chests are the default. You can instead choose the requester inventory when that player is on the farm and has enough room. The shipping bin is never used as a fallback.

Chest sorting only supports ordinary player-owned chests on the main farm. It groups matching items and similar categories, and stops if the complete plan is no longer safe or possible. It is the final stage of both manual and automatic shifts.

### Automatic contracts

The host can create one daily authorization from the bottom of the worker list. Choose a preferred worker, allowed substitutes, budget, and whether rest-day triple pay is allowed. Automatic hiring runs the same complete farm-work shift as manual hiring.

Starting the next day, the mod tries once when the host enters the farm between 6:10 AM and 4:00 PM. It skips the day if the worker, budget, crops, route, or storage is unsuitable.

### Multiplayer

Hosts and farmhands can request contracts, while the host applies all money, NPC, crop, and storage changes.

Every player must install the exact same Evil Farm Owner version. Real two-process multiplayer acceptance is not yet complete, so back up important saves and include both SMAPI logs when reporting a multiplayer issue.

### Useful commands

```text
efo_roster
efo_report
efo_overflow
efo_quarantine
efo_auto
```

Most players only need the `K` key. The extra commands help inspect recent work or recover items protected after a storage problem.

### Current limitations

- No debris clearing, planting, fertilizing, automatic machine refilling, or automatic shipping. Machines which recalculate or restart on collection and auto-grabber contents are deliberately excluded.
- No special or modded chest support.
- Automatic contracts use the same harvest, watering, and chest-sorting sequence as manual hiring.
- Some farm layouts may not provide a safe route; the contract will refuse or stop instead of breaking objects.
- v0.1.0 was released early by maintainer decision. The full forced-storage matrix, real two-process multiplayer, and final in-game published-package smoke test remain incomplete. Back up important saves.

## Compatibility

- Stardew Valley 1.6+
- SMAPI 4.0+
- No required content packs.

## License

[MIT](LICENSE) © 2026 Aveouter.
