# ABCD 整合记录

## 2026-10-16：恢复原策划自动攻击

取消玩家攻击充能、F 键攻击和改键入口。头、手、腿管道恢复独立自动循环，默认槽位步进 0.15 秒、整轮周期 2 秒，在 `PlayerStatsBase` 调整。静止触发器的独立计数与大激光蓄力保留。自动攻击、休息期停止、现有器官及七波流程回归通过，日志为 `Logs/automatic-attack-checks.log`。

## 2026-10-16：同步 C 地图、相机与碰撞到统一 Demo

`SpringUpDemo.unity` 已接入当前 C 灰盒的九宫格底图、72×72 地图与边界墙、相机 Size 10.125 和边界约束、玩家尺寸/移速 10、刷怪范围及 (12,0) 木桩。保留统一实验室、库存、器官连锁和原有攻击节拍，波次由统一驱动控制。

同步时发现源场景木桩的 Prefab 内部编号覆盖已失配，Demo 使用有效 Dummy Prefab 重建该实例。新增运行时碰撞层初始化与 Kinematic 玩家墙体扫掠，验证穿过木桩而不推动、墙体阻挡及沿墙滑动。C 源场景文件没有改写。

场景接线、真实移动/伤害/碰撞、器官回归、七波与重开流程及多分辨率渲染检查通过。详细文件、调参位置、代码摘录、场景同步范围和日志见 [开发改动总览与调参手册](Docs/开发改动总览与调参手册.md)。本次未重新打包发布，仍需在正式依赖与目标设备上验收。

以下为此前整合记录。

下一步安排见 [待确认与试玩验收清单](Docs/待确认与试玩验收清单.md)。第一步基线为 `46d8905`；本轮在 main 汇总提交后续整合、新增功能及试玩修复。

## 本次交付汇总

- **主要整合**：A 器官管道接 C 真实敌人伤害、状态、位移和击杀链；B 实验室接 D 唯一库存与经济/波次流程；预览与实战共享 A 配置和 D 实例身份。
- **新增内容**：目标/死亡位置快照、实战适配与范围表现、正式库存适配、统一 SpringUpDemo 场景与流程驱动、失败/通关/重开、中文 HUD、Windows 构建工具及联合回归。
- **试玩修复**：移动输入引用、近战停靠与接触伤害、Run UI 显隐和视口分区、0.15 秒槽间隔 / 2 秒整链周期及躯干属性扩展点。
- **编辑器体验**：SpringUpGameView 在统一场景进入 Play 后隐藏 Game 视图 Gizmos，退出时恢复；提供手动菜单，不禁用 EventSystem 或运行时 UI。隐藏/恢复检查通过，证据为 `Logs/gameview-gizmos.log`。
- **验证**：A 51 组、B/C 回归、D 19 项测试、真实按键移动/自然追击、七波加速闭环、三分辨率渲染、Windows 构建与独立启动检查。完整人工七波平衡试玩仍待进行。
- **交付边界**：第六波暂用 TNT 替代蜘蛛巢，第七波计时通关；完整 Event → Event、Boss、其他器官及躯干升级树仍未实现。
- **提交范围**：源码、场景、Prefab、必要的 .meta、构建入口与文档。排除 Logs/、Builds/、测试副本和本机 IDE 文件；原灰盒场景及 white_16.png 不改动。

## 2026-10-12：人工试玩反馈修复

- 保留用户在 SpringUpDemo 中修好的 Move/Sprint 输入引用。场景生成器改为显式绑定现有 Settings 输入资源，缺少 Move 引用时给出错误提示；没有覆盖原灰盒场景。
- 实验室布局不变。Run UI 的底部 Settlement 默认关闭并在 Awake 关闭；战斗只显示顶部状态栏，主相机为其预留 44 个 UI 单位的屏幕区域，实验室阶段恢复原视口。
- 槽间隔与整链周期拆开，统一入口在 PlayerStatsBase 配置 **Base Cooldown = 0.15、Base Cycle Duration = 2**。两者走共享属性修正体系，预览与实战同源；无部位修正时倍率为 1。修复属性缓存失效后仍返回旧值的问题。
- 整链周期按首槽到下一轮首槽计算。初次出手等 0.15 秒，六槽依次在 0.15～0.90 秒执行，下轮首槽在 2.15 秒执行。空槽跳过，空部位不积攒时间，开波重置，追加击杀扫描仍即时且不改变正常周期。周期不能短于本轮占用槽数乘间隔；新属性在下一轮采用。
- 近战/移动射击敌人的停靠距离不再绑定远程射程，而按接触半径计算。普通与快速敌人的 Prefab 及生成器不再填远程 6 米射程。接触伤害补充碰撞体间距判定，避免实体碰撞使中心距永远达不到 0.7 米。

本轮验证：

- `Logs/playtest-fixes.log`：时序、稀疏槽、空槽清零、防重叠和属性刷新通过；模拟 D 键后玩家实际移动 2.4 米，普通敌人从 6 米外自然追近并造成接触伤害；装配、下一波、七波加速流程、重开与失败通过。
- `Logs/playtest-regression.log`：A 51 组、B 装配/结算、索敌与 C 真实伤害/状态/黑洞/池化回归通过。
- `Logs/playtest-visual.log`：1280×720、1920×1080、1024×768 的战场与实验室渲染通过。截图工具分别渲染战场视口和全屏 Overlay，避免把 HUD 错误缩进战场视口。截图在完整依赖副本的 `Logs/VisualChecks/`。
- 隐藏编辑器的模拟按键检查需临时忽略 Game View 焦点，仅在隔离检查中设置，未改变主工程输入设置。完整依赖编辑器仍有既有 SearchDatabase 启动异常。
- 新 Windows 构建通过：`Logs/playtest-build.log`，输出 `Builds/SpringUpDemo/SpringUpDemo.exe`。独立程序持续运行 15 秒并启动第一波，无游戏脚本异常；Unity 云服务网络连接提示仍存在，日志为 `Logs/playtest-standalone.log`。测试进程已结束。
- 本轮构建时主工程 C# 源码与验证/构建副本逐文件哈希一致，场景保留用户输入修复并保存 0.15 / 2 秒配置；`git diff --check` 通过。随后新增的 Gizmos 工具仅用于编辑器，另行验证。

仍未替代完整七波人工平衡试玩，也未实现躯干器官或升级树本体。此次只准备其属性接入点。

## 2026-10-12：第二至七步，初步闭环完成

本节记录初步闭环的实施结果；下方第一步记录保留为历史。后续试玩修复与交付汇总见上方。

### 入口与配置口径

- Unity 入口：`Assets/Scenes/SpringUpDemo.unity`，当前启用的构建场景；旧 A/B/C 演示保留。
- Windows 程序：`Builds/SpringUpDemo/SpringUpDemo.exe`，构建产物不入库。
- A 的共享 OrganDefinition 是战斗数值唯一来源；D JSON 提供库存图鉴、经济、奖励与波次。统一入口在运行时投影 A 的伤害、半径、状态等参数到 D 图鉴，不改动原始 JSON 和器官资源。
- D RunController/Inventory 是唯一装备记录。B 通过 RunLaboratoryEquipment 操作它，预览和实战使用同一库存实例 ID 与定义。

### 已实现内容

1. CastEvent 和 TriggerSignal 携带可空且有限的位置快照，合法原点与缺失位置严格区分。普通范围攻击保存目标位置，击杀追加范围效果使用实际死亡位置；延迟流血保留原链。
2. AttackResolver 固定本次命中名单，范围内每个目标只命中一次。TNT 表现、伤害与黑洞吸引使用同一中心，黑洞生成后不追踪原目标。
3. BattleOrganCombat 将 A 接到 C 的真实扣血、状态计时、流血、移动减速/眩晕、黑洞位移和击杀通知。尊重 CanBeDisplaced；出生身份隔离对象池旧效果；清场不触发击杀链。
4. 库存替换是原子事务，失败不丢物品；装卸保留实例身份。B 支持正式奖励展示、装卸替换、出售丢弃、携带容量与下一波。
5. 战场暂停时 B 的结算预览仍使用非缩放时间运行。统一驱动控制开局、波次结算、实验室、下一波、失败、通关和重开，并清理持续效果与投射物。
6. 新增统一场景、中文 HUD 字体适配与爆炸/黑洞圆圈表现。资源引用显式序列化，Windows 构建入口为 SpringUp.EditorChecks.SpringUpBuild.WindowsBatch。

关键新增脚本在 `Assets/Scripts/Systems/`：BattleOrganCombat、BattleOrganEffects、RunLaboratoryEquipment、SpringUpRunDriver、RunUIFont。场景生成器与构建入口在 `Assets/Editor/`。新增流程、真实战斗与画面检查在 `Assets/Tests/Editor/`。

### 验证结果

使用 Unity 6000.5.11f1，测试均在 Logs 下的独立副本进行，不占用用户正在编辑的工程。完整依赖副本保留原工程 URP 等依赖，场景在同步主工程脚本 GUID 后生成并复制回主工程。

| 检查 | 结果与证据 |
| --- | --- |
| A 回归与 B 装配/结算 | A 51 组（含 49 个有序组合）通过；B 配置、装卸、实例、状态、链与守恒通过；`Logs/integration-validation.log` |
| 索敌与真实 C 战斗 Play | 距离门槛、快照、场景隔离、池化、真实伤害、范围、减速、眩晕、黑洞、流血击杀链及清理通过；同上 |
| D Edit Mode | ProgramDTests 与 BattleRunLinkTests 共 19 项通过、0 失败；`Logs/d-tests.xml` |
| 完整依赖 Play 闭环 | 首波真实伤害、45 髓质奖励、UI 装配、共享身份/配置、暂停预览、第二波、容量门槛、七波、重开与失败通过；`Logs/full-flow-validation.log` |
| 画面 | 实际敌人位置的 TNT 圆圈及实验室渲染通过；1280×720、1920×1080、1024×768 非空检查和截图核验；`Logs/visual-validation.log` |
| Windows 构建 | PASS，输出上述 exe；`Logs/windows-build.log` |
| 独立程序启动 | 隐藏启动持续 15 秒，日志确认第 1/7 波开始，无游戏脚本异常，随后只结束本次测试进程；`Logs/standalone-smoke.log` |

截图位于 `Logs/FullIntegrationValidation/Logs/VisualChecks/`。日志、隔离副本、截图和构建产物均不纳入提交。

### 边界与遗留项

- 七波流程检查调用波次完成接口加速，不是完整七个 60 秒的人工生存/平衡试玩。Windows 独立程序只做启动冒烟，完整交互闭环在 Editor Play 中验证。
- 第六波 spider_nest 在统一入口临时映射为 TNT；第七波计时通关。蜘蛛巢和 Boss 本体未实现。
- 完整 Event → Event、其余器官/部位、商店、躯干升级、正式美术与墙体交互不在此次完成范围。
- 完整依赖 Editor 日志仍出现 UnityEditor.Search.SearchDatabase 的启动 ArgumentOutOfRangeException；与游戏检查分开记录，不能宣称 Console 完全无错误。独立程序有 Unity 云服务网络连接失败提示，未阻止开波。
- 保留原 Battle_Greybox.unity、white_16.png 及用户已有未跟踪文件；未做全项目重构或数值平衡。

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

本轮开始前已有 `white_16.png`、`Battle_Greybox.unity` 改动及未跟踪 `.meta`，均不属于本轮修改，也未纳入第一步提交。第一步代码、测试与记录已提交为 `46d8905`，尚未推送。
