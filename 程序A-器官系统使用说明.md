# 程序 A：器官系统使用说明

本次器官目录整理：[六项修改与持续效果说明](Docs/器官资源整理与持续效果说明.md)。逐个查脚本和器官：[程序 A 脚本逐项说明与器官对照](Docs/程序A脚本逐项说明与器官对照.md)。

更新：2026-10-17。完整参数见 [调参手册](Docs/开发改动总览与调参手册.md)，器官结构见 [执行器统一模板](Docs/器官执行器统一模板-审阅稿.md)。

## 日常使用

主场景是 `Assets/Scenes/SpringUpDemo.unity`。器官自动攻击，头、手、腿各有独立六槽管道；普通器官跨部位装配，躯干专用。仅休息期可修改装备。

配置共有 19 种执行器、10 种触发器、7 种躯干。它们统一由 `Assets/Resources/OrganCatalog.asset` 引用。原 Hand 目录已移除，六种执行器移入 Actuators，触手移入 Triggers。改变效果和参数请改各器官的 Behaviour/Trigger/Torso `.asset`，无需复制一套战斗配置给实验室。

## 保留的旧器官演示

打开 `Assets/Scenes/OrganDemo.unity`，选择 `OrganDemo` 物体，在 `OrganDemoSetup → Preset` 选择，然后 Play：

- 综合组合：展示多种旧器官配合。
- 拳头与两个触手：检查击杀追加和同链保护。
- 拳头：单体伤害。
- 眩晕、流血、逐层减速：状态结算。
- TNT 范围爆炸：距离和范围命中。
- 黑洞吸引：范围、位移和寿命。
- 手动设置 HandOrganDemo：保留自己配置的槽位和目标。

这些预设替代原先七个分散测试场景；`Assets/Scenes/Tests/` 和旧 `OrganPipelineDemo.cs` 已移除。演示使用简化目标；真实墙体、敌人和角色行为请在 SpringUpDemo 验证。全部新器官的快速预览用 LaboratoryDemo。

## 从哪里追代码

- `OrganPipelineController` / `BodyRuntime`：三条管道的自动节拍与槽位执行。
- `OrganDefinition` / `OrganInstance`：只读定义和独立器官实例。
- `OrganActuatorConfig` / `DirectActuatorConfig` / `PatternActuatorConfig`：执行器的公共模板、直接作用与复合效果配置；实际结算在 `ParameterisedBehaviour.cs`、`AttackResolver.cs`、`ActuatorRuntime.cs`、`EffectExecutions.cs`。
- `OrganTriggerConfig` / `OrganTriggerRuntime`：条件与连锁。
- `TorsoOrganConfig` / `TorsoEquipmentRuntime`：躯干配置及装备倍率。
- `BattleOrganCombat` / `BattleActuatorWorld`：将器官攻击接入真实敌人和 Physics2D。
- `LaboratoryCombatDemo`：同一套逻辑在实验室的预览适配。

## 检查与未完成项

`Tools → SpringUp → Run All Organ Checks` 检查旧七器官及基础管道回归；它不是全部新器官的完整验收。完整批量检查入口在 [工程配置操作单](工程配置操作单.md)。

完整攻击逐级改造、平衡与正式表现等剩余事项统一见 [试玩验收清单](Docs/待确认与试玩验收清单.md)，不再在此维护第二份进度表。
