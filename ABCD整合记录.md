# ABCD 整合记录

## 2026-10-11：第一步——范围索敌与攻击发动门槛

### 本次范围

落实玩家靠近敌人后才发动器官攻击的规则。仅实现整合方案第一步，未修改场景文件、JSON 数值或现有器官资源；第二步的「范围攻击以触发敌人为中心」尚未实施。

### 行为规则

- 玩家索敌距离独立于敌人攻击距离、TNT 爆炸半径和黑洞吸引半径。默认 3 个世界单位，为可调的联调初值，不是策划最终数值。
- 每次出手选择范围内最近的存活目标，包含恰好位于边界的目标。同距时保持候选顺序。
- 单体、TNT、黑洞的普通攻击均须有目标。出手前再次校验目标状态与距离；无有效目标则跳过本次攻击，不积攒攻击次数。正常槽位节拍仍继续。
- 已成立的击杀触发链中的范围效果沿用触发敌人的身份，允许该敌人已经死亡；追加单体攻击重新索敌。持续流血、已生成黑洞不会因玩家离开索敌范围被取消。
- 范围攻击中心仍沿用旧实现，保存触发位置与修改中心属于第二步，本轮不宣称已完成。

### 代码改动

- `Assets/Scripts/Organs/AttackTargeting.cs`：公共距离校验与最近目标选择，拒绝死亡目标、非法坐标和非法范围。
- `OrganPipelineController.cs`：所有攻击形状共用目标选择；增加出手前校验入口；击杀追加范围攻击继承触发目标身份。
- `ParameterisedBehaviour.cs`：攻击请求工厂对所有形状拒绝空目标。
- `Assets/Scripts/Battle/EnemyRuntime.cs`：新增每次启用重新生成的 `TargetId`。原 `Id` 仍是配置编号，现有 C 事件接口不变。
- `Assets/Scripts/Systems/BattleOrganTargeting.cs`：复用 C 的 `PlayerLocator` 和 `EnemyRuntime.ActiveEnemies`，过滤其他场景、停用及死亡敌人；停用时解除绑定。
- `BattleRunDriver.cs`：建立手部管道时自动挂载战场索敌适配器。此时只产生器官攻击请求，尚未接通真实敌人扣血，后者属于整合第三步。
- `HandPipelineLoadout.cs`：修复刚合入分支已有的同名类型解析错误，显式使用 `SpringUp.Organs.OrganDefinition`；原类型在 D 的命名空间内被解析为 D 的图鉴定义，导致两处 CS1503 编译错误。
- `HandOrganDemo.cs`、`OrganPipelineDemo.cs`、`LaboratoryCombatDemo.cs`：演示和实验室预览采用相同距离判定。含范围器官的手部演示可从本场景索敌；实验室只查询自己的私有目标。
- 新增 `AttackTargetingChecks.cs`；调整原范围/黑洞测试中「空场仍可发动普通攻击」的过时断言，并调整 Play 检查中的玩家位置。
- 新增脚本均附带 `.meta`。
- A/B 使用说明开头增加当前规则说明，避免沿用旧版「按列表顺序索敌」「空场可以发动范围攻击」的口径。

### 调整与检查入口

- 灰盒 Play 后，在运行时的器官管道对象上查看 `BattleOrganTargeting`，调整 `Attack Range`。自动生成对象的 Play 修改不会保存，后续统一场景将显式保存组件配置。
- A 演示入口和 B 预览组件也有 `Attack Range`；B 以当前预览的 `explosionCenter` 作为模拟玩家位置。
- 编辑模式检查菜单：`Tools > SpringUp > Run Attack Targeting Checks`。
- 独立副本完整验证入口：`SpringUp.EditorChecks.AttackTargetingChecks.RunBatch`，执行编辑模式检查、A 的整合回归、B 的装备事务/结算回归，然后真实进入 Play 检查 C 的出生、回池、重新启用与目标绑定。此入口会创建临时场景并退出编辑器，只能在独立 batchmode 副本使用。

### 验证结果

使用 Unity 6000.5.11f1，在 `Logs/TargetingValidation` 隔离工程验证，日志为 `Logs/targeting-validation.log`，两者不进入版本控制。

- C# 编译通过。仍有原有弃用 API 和未使用字段警告，本轮未扩展为全项目警告清理。
- 新增编辑模式检查通过：最近目标、稳定同距、边界内外、非法坐标/距离、空目标、三种攻击形状的出手前校验、击杀链身份继承与同链防重复。
- A `OrganConsolidationChecks` 共 51 组通过，包含 49 对有序器官组合，以及状态、爆炸、黑洞、流血延迟击杀、触手防环和清理。
- B `TemporaryEquipmentChecks`、`LaboratorySettlementChecks` 通过；追加验证拳头、TNT、黑洞在预览索敌范围外不发动、进入范围后发动一次。
- 真实 Play 检查通过：C 的同类敌人不同身份、最近敌人选择、移出范围立即失效、回池后拒绝旧身份、再次出生分配新身份、死亡/停用过滤、跨场景隔离，以及索敌组件停用解绑和重新启用绑定。
- `git diff --check` 通过。

测试副本使用本地缓存的 Input System、uGUI、Test Framework 和必要内建模块，不修改主工程依赖。本轮没有做完整 URP 画面验收、正式构建或七波可玩闭环验证。日志中的许可握手重试后成功启动，不影响上述测试通过；不能把隔离依赖检查解释为完整项目发布验收。

### 保留的工作区内容

本轮开始前已有 `white_16.png`、`Battle_Greybox.unity` 改动及未跟踪 `.meta`，均不属于本轮修改。没有提交或推送。
