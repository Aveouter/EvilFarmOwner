# Evil Farm Owner v0.1.0

> **Stable release candidate / 稳定版候选**
>
> Do not publish this candidate until the linked single-player sorting, forced
> storage-recovery, real two-process multiplayer, and final packaged-artifact
> smoke gates are complete. Replace the integrity placeholders only with hashes
> from the audited merge commit and the asset downloaded from GitHub Releases.
>
> 在具名单机箱子整理、强制储存恢复、真实双进程多人及最终发布包烟雾门禁全部
> 完成前，不得发布此候选。完整性占位符只能替换为审计后合并提交生成、并从
> GitHub Releases 回下载验证的实际值。

## 中文

- 从当前安全可雇佣的成年 NPC 中选择工人，查看好感度、时薪、六小时授权上限与休息日三倍工资。
- 让具名工人从真实农场边界入口进入，在不破坏摆件的前提下完成全部可到达的浇水或收获目标并安全返回。
- 为每份收获合同固定选择“分类箱”或“请求者背包”，合同执行期间绝不随机切换目的地。
- 按“同品质可堆叠、同物品、同 `Item.Category`、空箱”确定性分类收获物，并在没有完整堆叠容量时安全停止。
- 手动雇佣具名 NPC 执行不可变箱子整理计划；完整堆叠只在同时验证来源箱、目标箱及双锁后移动一次。
- 通过持久溢出仓、可见掉落、隔离仓与恢复记录守恒已经取得所有权的货物，绝不自动出售或静默删除。
- 主机和农场工都可请求合同；只有主机修改作物、金币、NPC、货物和箱子。协议 8 提供幂等请求、阶段快照、重连及主机重启恢复。
- 支持一份主机专用自动合同授权，包含固定候选池、每日幂等选择、预算上限和休息日显式授权；自动箱子整理仍未开放。

安装：解压 ZIP，把 `EvilFarmOwner` 文件夹放入 Stardew Valley 的 `Mods`
目录。要求 Stardew Valley 1.6+、SMAPI 4.0+；所有联机玩家必须安装完全
相同的版本。

## English

- Choose a currently safe adult NPC and review friendship, hourly wage, the six-hour authorization cap, and explicit rest-day triple pay.
- Named workers enter through genuine farm-boundary entrances, avoid placed objects, complete every reachable watering or harvest target, and return safely.
- Fix each harvest contract to either classified chests or the requester inventory; a running contract never switches destination silently.
- Classify harvest output deterministically by compatible quality stack, same item, exact `Item.Category`, then empty chest, and stop safely when no destination can accept the whole stack.
- Hire a named NPC to execute one immutable chest-sorting plan; a whole stack moves exactly once only after both source and destination chests and locks are verified.
- Conserve owned cargo through persistent overflow, visible drop, quarantine inventory, or a recovery record; never auto-ship or silently delete it.
- Hosts and farmhands can request contracts while only the host mutates crops, money, NPCs, cargo, and chests. Protocol 8 provides idempotent requests, phase snapshots, reconnect synchronization, and host-restart recovery.
- Configure one host-owned automatic authorization with a fixed candidate pool, once-per-day deterministic selection, hard wage caps, and explicit rest-day opt-in; automatic chest sorting remains unavailable.

Install by extracting the ZIP and placing the `EvilFarmOwner` folder in Stardew
Valley's `Mods` directory. Stardew Valley 1.6+ and SMAPI 4.0+ are required.
Every multiplayer peer must install the exact same version.

## Known limitations / 已知限制

- Debris clearing, planting, fertilizing, automatic shipping, automatic chest sorting, and special/modded chest support are not included.
- Every multiplayer peer must use exactly the same mod version.

## Integrity / 完整性

- Asset: `EvilFarmOwner 0.1.0.zip`
- SHA-256: `{{ZIP_SHA256}}`
- Source commit: `{{SOURCE_COMMIT}}`
- Source tree: `{{SOURCE_TREE}}`
- Downloaded-asset SHA-256: `{{DOWNLOADED_ZIP_SHA256}}`
- License: MIT

After publishing, download the public asset into an otherwise clean directory
and run:

```bash
./scripts/verify-release-asset.sh \
  "EvilFarmOwner 0.1.0.zip" \
  "{{ZIP_SHA256}}" \
  "0.1.0"
```

The command must pass against the downloaded bytes before the release Issue is
closed. It verifies the filename, checksum, package allowlist, embedded manifest,
license, and absence of development-only or acceptance-test commands.
