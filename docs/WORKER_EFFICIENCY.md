# Worker efficiency / 工人效率

Worker efficiency changes only the duration of the visible work action. It never changes wages,
availability, movement speed, harvest quantity, item quality, or drop tables. A missing profile
uses `1.00x`. Bonuses are deliberately small, and no worker receives a penalty based on age,
disability, personality, or an unrelated occupation.

工人效率只改变可见农活动作的持续时间，不改变工资、可用性、移动速度、收获数量、物品品质或掉落表。
缺少资料时使用 `1.00x`。加成刻意保持较小，也不会因为年龄、残障、性格或无关职业对工人施加惩罚。

The deterministic duration rule is `ceil(base action ticks / efficiency)`, with a lower bound one
tick after the action takes effect. The host snapshots the selected task multiplier when the
contract starts, and the same value is used until settlement.

Generic Mod Config Menu exposes a worker-difference strength from 0% to 200%. At 0%, every
profile is pulled to the `1.00x` baseline. At 100%, the table below is used directly. Values above
100% amplify only the distance from baseline and remain inside the supported `1.00x–1.20x`
range. This setting changes action duration only and never changes wages or output.

Generic Mod Config Menu 提供 0%–200% 的“工人差异强度”。0% 会把所有人物拉回 `1.00x`
基准，100% 直接采用下表，超过 100% 只放大人物相对基准的差距，并始终限制在
`1.00x–1.20x` 的支持范围内。该设置只改变动作耗时，不改变工资或产出。

确定性耗时规则为 `ceil(基础动作帧数 / 效率)`，下限为动作实际生效后的下一帧。合同开始时由主机固化所选
工作的效率，此后直到结算都使用同一个数值。

| Worker | Watering | Harvesting | Gameplay reason | 玩法依据 |
| --- | ---: | ---: | --- | --- |
| Abigail | 1.00x | 1.00x | Baseline | 基准 |
| Alex | 1.10x | 1.05x | Athletic stamina for repetitive field work | 运动训练适合持续田间作业 |
| Caroline | 1.10x | 1.10x | Gardening experience | 园艺经验 |
| Clint | 1.10x | 1.05x | Repetitive manual trade work | 持续手工业经验 |
| Demetrius | 1.05x | 1.00x | Field science and crop observation | 田野科学与作物观察经验 |
| Elliott | 1.00x | 1.00x | Baseline | 基准 |
| Emily | 1.00x | 1.00x | Baseline | 基准 |
| Evelyn | 1.10x | 1.10x | Gardening experience | 园艺经验 |
| George | 1.00x | 1.00x | Baseline; no disability penalty | 基准；不因残障扣减 |
| Gus | 1.00x | 1.00x | Baseline | 基准 |
| Haley | 1.00x | 1.00x | Baseline | 基准 |
| Harvey | 1.00x | 1.00x | Baseline | 基准 |
| Jodi | 1.00x | 1.00x | Baseline | 基准 |
| Kent | 1.10x | 1.05x | Disciplined physical field work | 纪律化体力作业经验 |
| Leah | 1.05x | 1.10x | Outdoor foraging experience | 户外采集经验 |
| Lewis | 1.00x | 1.00x | Baseline | 基准 |
| Linus | 1.05x | 1.10x | Outdoor self-sufficiency and foraging | 户外自给与采集经验 |
| Marnie | 1.10x | 1.10x | Daily ranch work | 日常牧场工作经验 |
| Maru | 1.05x | 1.00x | Field science and engineering | 田野科学与工程经验 |
| Pam | 1.00x | 1.00x | Baseline | 基准 |
| Penny | 1.00x | 1.00x | Baseline | 基准 |
| Pierre | 1.00x | 1.00x | Baseline | 基准 |
| Robin | 1.10x | 1.05x | Sustained manual construction work | 持续建筑手工作业经验 |
| Sam | 1.00x | 1.00x | Baseline | 基准 |
| Sebastian | 1.00x | 1.00x | Baseline | 基准 |
| Shane | 1.10x | 1.10x | Ranch work experience | 牧场工作经验 |
| Willy | 1.10x | 1.05x | Sustained outdoor manual work | 持续户外手工作业经验 |

Future task types must opt into an explicit profile value. Until then, they use the same safe
`1.00x` fallback.

未来新增工作类型必须显式提供效率资料；在完成配置前统一使用安全的 `1.00x` 回退值。
