# Evil Farm Owner v0.3.2

## 中文

v0.3.2 集中解决单工人班次中的路线中断与诊断问题，不加入多工人或新的存档格式。

### 路线可靠性

- 收获、浇水、动物照料和箱子整理现在使用同一套中断分类：超时、像素进度停滞、控制器提前结束或被替换、第一步碰撞和控制器创建失败。
- 每次中断会记录地图、NPC 格子与像素坐标、目的地、下一路径点、剩余路径、控制器归属和实时碰撞结果。
- 动态障碍按地图记录为格子或有向路径边；每个路线阶段最多尝试三次，不会反复撞向同一段路线。
- 农场内始终使用非破坏寻路并允许打开门。单个作物、动物或建筑入口失败时只跳过对应目标；棚舍出口或返程失败时安全停止并恢复 NPC。
- 箱子整理在移动阶段不会取出源物品；锁内转移若异常，必须完整回滚或写入持久恢复区后才能结束。

### 可验证的开发检查

- 与游戏程序集无关的规划、工资、存储守恒、协议和路线决策代码已进入独立的 .NET 8 Core 项目。
- GitHub Actions 会在 Ubuntu 上编译 Core 并运行全部 122 项确定性测试；完整 Mod 编译和发布包检查仍由本地干净发布验证器负责。

### 兼容与限制

- 不修改多人协议、存档结构、配置格式或单工人上限。
- 真实双进程多人验收仍是 v0.5.0 的发布门槛，本版本不声称已完整验证远程多人游戏。
- 需要 Stardew Valley 1.6+、SMAPI 4.0+；Generic Mod Config Menu 仍为可选依赖。

## English

v0.3.2 focuses on interrupted-route recovery and diagnostics for single-worker shifts. It does not add concurrent workers or a new save format.

### Route reliability

- Harvesting, watering, animal care, and chest sorting now share the same interruption classes: timeout, pixel-progress stall, early or replaced controller, rejected first step, and controller setup failure.
- Every interruption records the map, NPC tile and pixel position, destination, next and remaining waypoints, controller ownership, and a live collision probe.
- Dynamic obstacles are stored per location as tiles or directed edges. Each route stage gets at most three attempts and cannot keep retrying the same failed segment.
- Farm movement stays non-destructive and can open gates. An isolated crop, animal, or building entrance is skipped; an exhausted shed exit or return route stops safely and restores the NPC.
- Chest sorting never removes a source item during travel. A locked-transfer failure must fully roll back or enter durable recovery before finalization.

### Verifiable development checks

- Game-independent planning, wage, storage-conservation, protocol, and route-decision source now lives in a standalone .NET 8 Core project.
- GitHub Actions compiles Core and runs all 122 deterministic tests on Ubuntu. The clean local release verifier remains responsible for the proprietary-game Mod build and package checks.

### Compatibility and limits

- No multiplayer protocol, save-schema, configuration-format, or one-worker-limit changes.
- Real two-process multiplayer acceptance remains a v0.5.0 release gate; this release does not claim complete remote-multiplayer verification.
- Requires Stardew Valley 1.6+ and SMAPI 4.0+. Generic Mod Config Menu remains optional.
