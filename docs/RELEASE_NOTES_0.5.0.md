# Evil Farm Owner v0.5.0

## 中文

v0.5.0 开放多人同时雇佣。默认仍然一次雇佣 1 人，主机可以在 Generic Mod Config Menu 中把上限调整为 2–4 人。

### 主要变化

- 名单支持多选，并显示合计的最高授权工资；自动选择会综合工作效率、工资、好感度和姓名，结果稳定且不超预算。
- 所有工人都可以分担收获和浇水，同一个目标只会被一人领取。动物照料和箱子整理各由一人负责，避免重复操作。
- 工人会为同一格、相向路线、农场入口、畜棚入口和箱子访问进行等待或重新规划，不会为了通行摧毁农场摆设。
- 每名工人拥有独立的 NPC 状态、货物和工资结算。某人失败时，只会进行一次有限的任务转交，不会让其他工人重复付费。
- 联机协议升级为 schema 11，支持每名工人的状态、原请求 ID 重连、主机重启后的历史结果，以及旧单工人配置迁移。

### 安装

需要 Stardew Valley 1.6+ 与 SMAPI 4.0+。下载 ZIP，解压后把 `EvilFarmOwner` 文件夹放入游戏的 `Mods` 文件夹。联机双方必须安装完全相同的版本。

Generic Mod Config Menu 仍为可选依赖。并发上限默认为 1，只有主机设置会控制合同。

### 已知限制与验收说明

- 维护者明确选择不执行 1–4 人完整实机矩阵、保存/重载、断线重连、真实双进程多人和最终发布 ZIP 的游戏内烟雾测试，直接基于自动证据验收。本说明表示风险已被接受，不表示这些场景已经通过。
- 自动证据包括 132 项确定性逻辑测试、无警告/错误的生产构建、源码边界检查、包内容白名单、生产命令扫描和 SHA-256 审计。
- 重要联机存档请先备份；反馈问题时请同时附上主机和农场工的完整 SMAPI 日志。
- 仍不支持清理杂物、播种、施肥、自动补充机器原料、自动出售、特殊箱子或其他 Mod 添加的储存容器。

## English

v0.5.0 enables concurrent hiring. The default remains one worker per contract, and the host can raise the limit to two, three, or four through Generic Mod Config Menu.

### Highlights

- The roster supports multi-selection and shows one combined maximum authorization. Automatic selection uses stable efficiency, wage, friendship, and name ordering without exceeding the budget.
- Every worker may share harvesting and watering, with one owner per target. Animal care and chest sorting each retain a single owner to prevent duplicate actions.
- Workers wait or replan around same-tile, opposing-edge, farm-entrance, building-entrance, and chest-access conflicts without destroying placed farm objects.
- Each worker has independent NPC state, cargo, and wage settlement. One failed worker permits one bounded reassignment without charging another worker twice.
- Multiplayer protocol schema 11 synchronizes each worker, retains the original request ID across reconnects, restores prior results after a host restart, and migrates legacy single-worker settings.

### Installation

Stardew Valley 1.6+ and SMAPI 4.0+ are required. Download and extract the ZIP, then place the `EvilFarmOwner` folder in the game's `Mods` directory. Every multiplayer peer must install this exact version.

Generic Mod Config Menu remains optional. The concurrent-worker limit defaults to one, and only the host's settings control contracts.

### Known limitations and acceptance notice

- The maintainer explicitly waived the full live 1–4 worker matrix, save/reload, reconnect, real two-process multiplayer, and final in-game smoke test of the published ZIP, accepting the release from automated evidence. This records accepted risk; it does not claim those scenarios passed.
- Automated evidence includes 132 deterministic logic tests, a warning/error-free production build, Core boundary checks, the package allowlist, production-command scan, and SHA-256 audit.
- Back up important multiplayer saves and attach both complete SMAPI logs when reporting a problem.
- Debris clearing, planting, fertilizing, automatic machine input, automatic shipping, special chests, and modded storage are still unsupported.
