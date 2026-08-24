# Evil Farm Owner v0.1.0-beta.1

> **Public test release / 公开测试版本**
>
> Back up your save before testing. The production implementations and deterministic tests are complete, but the real remote host/farmhand matrix and forced storage-recovery matrix have not yet been run. This prerelease does not claim those gates passed.
>
> 测试前请备份存档。生产实现和确定性测试已经完成，但真实远程主机/农场工矩阵与强制储存恢复矩阵尚未运行；本测试版不会把这些门禁描述为已通过。

## 中文

- 从当前安全可雇佣的成年 NPC 中选择具名工人，查看好感度、时薪、六小时上限与休息日三倍工资。
- 让工人从农场真实边界入口进入，绕开箱子、机器、栅栏和棚架，浇灌全部可到达的缺水作物。
- 通过原版作物逻辑收获全部可到达的成熟作物，并按“同品质堆叠 → 同物品 → 同 `Item.Category` → 最大空箱容量”稳定分类。
- 没有箱子能完整接收堆叠时停止继续收获；已采货物通过请求者背包、持久溢出、可见掉落或隔离恢复守恒，不会自动出售或静默消失。
- 主机和农场工都能请求合同，但作物、金币、NPC、货物和箱子只由主机权威修改；协议支持幂等请求、阶段快照、重连和主机重启恢复。
- 支持一份主机专用的自动合同授权，包含固定候选池、每日幂等选择、预算上限和休息日显式授权。

安装：解压 ZIP，把 `EvilFarmOwner` 文件夹放入 Stardew Valley 的 `Mods` 目录。要求 Stardew Valley 1.6+、SMAPI 4.0+；联机双方必须安装完全相同的版本。

## English

- Select a currently safe adult NPC and review friendship, hourly wage, the six-hour cap, and explicit rest-day triple pay.
- Workers enter through genuine farm-boundary entrances, route around chests, machines, fences, and trellises, and water every reachable dry crop.
- Harvest every reachable mature crop through vanilla crop logic and classify exact output by compatible quality stack, same item, exact `Item.Category`, then greatest empty-chest capacity.
- Stop further harvesting if no chest accepts the complete stack. Already harvested cargo remains conserved through requester inventory, persistent overflow, visible drop, or quarantine recovery; it is never auto-shipped or silently deleted.
- Hosts and farmhands can request contracts, while only the host mutates crops, money, NPCs, cargo, and chests. The protocol includes idempotent requests, phase snapshots, reconnect synchronization, and host-restart recovery.
- Configure one host-owned automatic authorization with a fixed candidate pool, once-per-day deterministic selection, hard wage caps, and explicit rest-day opt-in.

Install by extracting the ZIP and placing the `EvilFarmOwner` folder in Stardew Valley's `Mods` directory. Stardew Valley 1.6+ and SMAPI 4.0+ are required. Every multiplayer peer must install this exact version.

## Known limitations / 已知限制

- Real two-process host/farmhand watering, harvest, duplicate-request, disconnect/reconnect, and host-restart acceptance remains unverified.
- Forced overflow/drop/quarantine/recovery fault acceptance remains unverified and must use a disposable save.
- Debris clearing, planting, fertilizing, automatic shipping, and special/modded chest support are not included.

## Integrity / 完整性

- Asset: `EvilFarmOwner 0.1.0-beta.1.zip`
- SHA-256: `{{ZIP_SHA256}}`
- Source commit: `{{SOURCE_COMMIT}}`
- Source tree: `{{SOURCE_TREE}}`
- License: MIT
