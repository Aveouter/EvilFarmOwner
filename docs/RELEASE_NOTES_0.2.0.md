# Evil Farm Owner v0.2.0

## 中文

v0.2.0 把单项工作合同升级为一次完整农活班次。雇佣一名当前有空的成年 NPC 后，工人会按顺序收获、浇水、照料动物、整理普通农场箱子，并进行一次有上限的最终复查。

### 主要更新

- 重做简洁的原版风格雇佣名单和确认界面，只显示工人、好感度、今日最高工资、工作摘要和产物去向，并补齐手柄焦点顺序。
- 新增果树、普通与重型树液采集器、安全的原版机器、蟹笼、鱼塘、浆果丛和茶树收取。
- 新增畜棚与鸡舍进出、抚摸动物、从自有筒仓取干草喂食，以及鸡蛋、牛奶和羊毛收集。
- 玩家离开农场后 NPC 仍会继续工作；选择玩家背包时，只要请求者仍在线且能放下完整堆叠即可跨地图接收。
- 完整班次结束前进行一次有上限的最终复查，减少班次途中刚成熟或刚变为可执行的目标遗漏。
- 自动合同现在执行与手动雇佣相同的完整班次。
- 为未来多人雇佣加入确定性的目标声明、工作分配、工资汇总和路线时隙预留；当前仍只开放一名工人。

### 安全规则

- 不会为了寻路破坏农场摆设。
- 已取得的物品不会静默消失、复制或自动出售。
- 所选产物目的地在合同执行中不会悄悄改变。
- 储存、路线或 NPC 状态不再安全时，合同会明确停止并保护已取得物品。

## English

v0.2.0 upgrades isolated jobs into one complete farm-work shift. After hiring a currently available adult NPC, the worker harvests, waters, cares for animals, sorts ordinary farm chests, and performs one bounded final reconciliation pass.

### Highlights

- Reworked the roster and confirmation into concise vanilla-style menus showing only worker, friendship, today's maximum wage, shift summary, and delivery destination, with complete controller focus order.
- Added collection from fruit trees, normal and heavy tappers, selected safe vanilla machines, crab pots, fish ponds, berry bushes, and tea bushes.
- Added barn and coop entry, animal petting, finite silo-hay feeding, and collection of eggs, milk, and wool.
- Workers continue after the requester changes maps. A selected requester inventory remains available while that player is online and can accept the complete stack.
- Added one bounded final reconciliation pass so work which becomes ready during the shift is checked once before settlement.
- Automatic contracts now run the same complete shift as manual hiring.
- Added deterministic target claims, assignment, wage aggregation, and route-slot reservations for future multi-worker hiring; current gameplay remains limited to one worker.

### Safety rules

- Workers do not destroy placed farm objects to make a route.
- Owned items are never silently deleted, duplicated, or automatically shipped.
- A running contract never changes its selected delivery destination silently.
- Unsafe storage, routes, or NPC state stop the contract explicitly while preserving owned cargo.

## Requirements / 运行要求

- Stardew Valley 1.6+
- SMAPI 4.0+
- Exact same Evil Farm Owner version on every multiplayer peer
- MIT License

## Deferred manual acceptance / 延期人工验收

The deterministic suite, clean Release build, package allowlist, manifest, license, translation parity, production-DLL scan, and public download checksum are automated release gates. The forced-storage fault matrix, real two-process multiplayer test, final visual review, and exact published-ZIP in-game smoke test remain unperformed unless separately scheduled by the maintainer.

确定性测试、干净 Release 构建、包内容白名单、清单、许可证、翻译键一致性、生产 DLL 扫描及公开下载哈希属于自动发布门禁。强制储存故障矩阵、真实双进程多人、最终视觉检查和精确发布 ZIP 的游戏内烟雾测试仍需维护者另行安排，未执行的项目不会被声明为通过。
