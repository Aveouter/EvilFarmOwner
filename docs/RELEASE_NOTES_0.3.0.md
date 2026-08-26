# Evil Farm Owner v0.3.0

## 中文

v0.3.0 让完整农活班次更容易按自己的玩法调整，也减少了 NPC 在收获与储存之间的无效往返。

### 主要更新

- 安装可选的 Generic Mod Config Menu 后，主机可以设置基础时薪、好感度对工资的影响、休息日倍率、默认产物去向，以及是否启用收获、浇水、动物照料和箱子整理。
- 分类箱模式会先连续收集最多 12 个独立产物堆叠，再按照现有的同品质堆叠、同物品、同类别和空箱规则分组送货；同一批中属于同一箱子的物品只走访并锁定箱子一次。
- 修复玩家背包明明能通过多个未满堆叠共同容纳产物，却被错误判断为没有空间的问题。收获物和动物产物现在使用同一套完整容量判断。
- 修复玩家离开农场后，蟹笼的《捕蟹秘籍》双倍产物可能被错误取消的问题。只要玩家在线且背包容量足够，跨地图接收仍然有效。

### 安全与兼容

- 每件已取得的物品仍保留独立的转移记录；分类、容量和箱子状态会在主机持有箱锁时重新检查。
- 运行中的合同使用开工时保存的设置，不会被中途修改。
- 多人游戏仍由主机执行工作和修改金币、NPC、作物、动物与储存；所有玩家必须安装完全相同的 Mod 版本。
- 需要 Stardew Valley 1.6+、SMAPI 4.0+；Generic Mod Config Menu 为可选依赖。

## English

v0.3.0 makes complete farm-work shifts easier to tune and reduces wasted travel between collection targets and storage.

### Highlights

- With the optional Generic Mod Config Menu installed, the host can configure the base hourly wage, friendship discount strength, rest-day multiplier, default output destination, and whether harvesting, watering, animal care, and chest sorting are enabled.
- Classified-chest delivery now carries up to 12 independent output stacks, groups them through the existing compatible-quality, same-item, exact-category, and empty-chest rules, then visits and locks each selected chest once per batch.
- Fixed requester inventory checks which rejected output even when several compatible partial stacks had enough combined space. Harvest cargo and animal products now share one complete-capacity calculation.
- Fixed the Crabbing Book doubled crab-pot output being suppressed after the requester left the farm. Cross-map delivery remains valid while that player is online and has enough inventory capacity.

### Safety and compatibility

- Every captured output keeps its independent transfer record. Classification, capacity, and live chest state are rechecked while the host owns the chest mutex.
- A running contract keeps the settings snapshot taken when the shift began.
- Multiplayer work and mutations remain host-authoritative, and every player must install the exact same mod version.
- Requires Stardew Valley 1.6+ and SMAPI 4.0+. Generic Mod Config Menu remains optional.

## Deferred manual acceptance / 延期人工验收

The deterministic suite, clean Release build, package allowlist, manifest, license, translation parity, production-DLL scan, and published download checksum are automated release gates. Per maintainer direction, this cycle does not launch the game. The forced-storage fault matrix, real two-process multiplayer test, final visual review, and exact published-ZIP in-game smoke test remain unperformed and are not claimed as passed.

确定性测试、干净 Release 构建、包内容白名单、清单、许可证、翻译键一致性、生产 DLL 扫描及公开下载哈希属于自动发布门禁。按照维护者要求，本轮不启动游戏。强制储存故障矩阵、真实双进程多人、最终视觉检查和精确发布 ZIP 的游戏内烟雾测试仍未执行，也不会被声明为已通过。
