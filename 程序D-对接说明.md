# 程序 D：库存与波次对接

更新：2026-10-16。正式入口是 `SpringUpDemo` 中的 `SpringUpRunDriver`，不要再挂载第二个流程驱动。

## 数据由谁负责

- `Assets/Data/Organs/organs.json`：40 条库存定义，含尚无战斗实现的旧条目；负责 ID、价格、经济等元数据。
- `Assets/Resources/OrganCatalog.asset`：当前 36 种实际战斗定义；伤害、效果、触发参数以它引用的 `.asset` 为准。
- `Assets/Data/Waves/waves.json`：7 波的时长与奖励。
- `Assets/Scripts/Systems/Inventory.cs`：物品实例、装备位置、未装备容量、出售和丢弃。
- `Assets/Scripts/Systems/RunController.cs`：本局状态与规则。
- `Assets/Scripts/Systems/SpringUpRunDriver.cs`：把波次、战场、器官和实验室连接起来。

主场景启动时将 A 的战斗定义投影到运行中的库存图鉴，不改原 JSON。第六波的旧蜘蛛巢奖励临时映射为 TNT。旧 `mvp_baseline.json` 没有消费者，已删除；玩家属性看 PlayerStatsBase，敌人基础表看 EnemyArchetypeConfig。

## 正式流程

`boot → combat → wave_settling → lab → combat`；最后一波结束进入 `run_won`，玩家死亡进入 `run_lost`。

- `StartRun()` 开局，给手部第 1 槽拳头。
- `CombatTimerFinished()` 结算本波奖励，进入实验室或通关。
- `CanStartNextWave()` 同时检查休息阶段和未装备容量；`StartNextWave()` 开下一波。
- `Equip / Unequip / Replace / Sell / Discard` 只允许在 `lab` 调用。
- `LoseRun()` 结束战斗并发失败信号。Unity 场景重开由 SpringUpRunDriver 处理。

`TryBuy`、`TryUpgradeTorso` 当前返回 `not_open`，不代表商店或升级已经实现。

## 信号和槽位约定

- `LoadoutChanged(InventorySnapshot)`：更新器官管道与界面。
- `RunStarted(RunStartedPayload)`：包含 Wave；本波时长可读 `Run.WaveDuration`。
- `WaveEnded(WaveEndedPayload)`：停止战场，Reason 当前为 `timer`。
- `LabOpened(LabOpenedPayload)`：Drops 已进库存，MarrowDelta 是本次髓质奖励。
- `RunEnded(RunEndedPayload)`：Result 为 `won` 或 `lost`。

库存槽号从 **1** 开始；管道/预览内部下标从 **0** 开始，由适配层转换。正式库存中记录 InstanceId；通过 `Inventory.OrganOf(instanceId)` 查询定义。不要把 DefinitionId 当成一件物品的唯一身份。

## 哪些旧入口仍有用途

`BattleRunDriver` / `BattleRunLink` 只服务独立 C 灰盒的临时流程，运行时检测到 SpringUpRunDriver 会退出安装。它们仍有消费者和回归测试，因此保留。

旧 `GameFlowBootstrap` 只是无人挂载的接线示例，已删除。新功能应接现有主驱动，不再复制它。

检查入口：`ProgramDTests`、`BattleRunLinkTests` 与整合 Play 检查。具体运行方式见 [工程配置操作单](工程配置操作单.md)。
