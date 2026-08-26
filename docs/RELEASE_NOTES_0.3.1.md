# Evil Farm Owner v0.3.1

## 中文

v0.3.1 是一个仅包含收获送货修复的稳定性补丁。

### 修复内容

- NPC 的 12 格携带上限现在按原版可堆叠规则计算。同物品、同品质的产物会共用一个物品格，不再把每次采集记录误算为独立格子。
- 收获产物送往分类箱时，如果路线动态卡住，NPC 会排除失败的下一格并重新规划，而不是连续重试同一段路线。
- 如果三次安全路线都失败，画面会显示工人、起点、箱子和具体中断原因；SMAPI 日志还会记录像素坐标、下一路径点、控制器状态和实时碰撞探测。

### 安全与兼容

- 每件产物仍保留独立转移编号；批量携带不会改变箱子分类、容量检查或物品守恒规则。
- 无法完成送货时，已有产物仍进入原有的受保护恢复流程，不会静默消失。
- 本补丁不改变配置、多人协议、存档结构或同时可雇佣的工人数。
- 需要 Stardew Valley 1.6+、SMAPI 4.0+；Generic Mod Config Menu 仍为可选依赖。

## English

v0.3.1 is a narrowly scoped stability patch for harvest delivery.

### Fixes

- The NPC's 12-slot carrying threshold now follows vanilla-compatible stacking. Compatible items with the same quality share one carried slot instead of every capture record counting separately.
- When a classified-chest delivery route stalls dynamically, the worker excludes the failed next tile and replans instead of retrying the same broken segment.
- If all three safe routes fail, the HUD identifies the worker, origin, chest, and interruption reason. The SMAPI log also records pixel coordinates, the next waypoint, controller state, and a live collision probe.

### Safety and compatibility

- Every output keeps its independent transfer ID. Batched carrying does not change chest classification, capacity checks, or conservation accounting.
- If delivery cannot finish, captured cargo still enters the existing protected recovery path and is never silently discarded.
- This patch does not change configuration, multiplayer protocol, save data, or the one-worker concurrency limit.
- Requires Stardew Valley 1.6+ and SMAPI 4.0+. Generic Mod Config Menu remains optional.

## Release verification / 发布验证

The exact release ZIP must pass the clean deterministic suite, package allowlist, production-DLL scan, SHA-256 re-download audit, and SMAPI load smoke test before the draft release is made public. The forced-storage fault matrix and real two-process multiplayer acceptance remain deferred and are not claimed as passed by this patch.

正式公开前，精确发布 ZIP 必须通过干净的确定性测试、包内容白名单、生产 DLL 扫描、重新下载 SHA-256 校验和 SMAPI 加载烟雾测试。本补丁仍不声明强制储存故障矩阵或真实双进程多人验收已经完成。
