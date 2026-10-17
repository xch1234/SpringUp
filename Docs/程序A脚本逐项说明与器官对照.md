# 程序 A：脚本逐项说明与器官对照

更新：2026-10-17；按当前工作区源码与资源引用整理。本页是查阅手册，不要求从头读完。只调数值时，直接看“每个器官对应哪些文件”；想理解实现时，再看核心脚本与调用示例。

这里的“程序 A”包括器官核心，以及它与 B 实验室、C 战场、D 库存相接的代码。接线部分单独列出，不表示这些文件全部由 A 独立编写。

## main 整合后的新入口与接线

当前优先从 `Assets/Scenes/Flow/Bootstrap.unity` 启动。本文所有器官核心与资源对照继续有效；`SpringUpRunDriver` 是旧 Demo 的适配器，新场景使用以下接线：

- [FlowApplication.cs](../Assets/Scripts/Flow/FlowApplication.cs)：常驻入口，负责主菜单、场景切换、暂停窗口、B/Tab 背包和 E 商店。打开窗口暂停世界；战斗背包只读。
- [FlowContent.cs](../Assets/Scripts/Flow/FlowContent.cs)：每个战斗/休整场景的入口；接入三条自动管道、TorsoEquipmentRuntime、OrganCombatReport、RunEquipmentHud。装备变更立即重算躯干并清理旧效果；退出场景解绑。
- [RunConfiguration.cs](../Assets/Scripts/Systems/RunConfiguration.cs)：新旧入口共用的 JSON 与 OrganCatalog 校验，接受通用普通器官和躯干专用器官，保留 main 的经济数值。
- [RunSession.cs](../Assets/Scripts/Flow/RunSession.cs)：保存一局库存、波次和跨场景生命；不保存临时弹体、执行进度或攻击充能。
- [FlowLaboratoryWindow.cs](../Assets/Scripts/Flow/FlowLaboratoryWindow.cs)：把正式库存接到 LaboratoryPresenter，处理只读与可编辑模式；样板菜单也会刷新已打开背包。
- [EconomySwing.cs](../Assets/Scripts/Systems/EconomySwing.cs)：main 新增的购买、出售和奖励浮动；器官伤害仍读取 `.asset`，不由商店价格决定。

新流程和旧 Demo 共用器官配置，各自保存场景玩家属性。新流程调基础生命、速度与节奏，修改 `Flow/Battle` 和 `Flow/Rest` 的 `World/Player → PlayerStatsBase`；相机使用场景自身 Camera，躯干视野在运行时乘到其基础尺寸上。最新整合范围与验证见 [整合记录](2026-10-17-main与A整合记录.md)。

## 怎么查这份文档

- [先分清文件和对象](#先分清文件和对象)：为什么一个器官有两个 asset，却找不到同名 cs。
- [配置与定义脚本](#配置与定义脚本)：名称、参数和合法性检查。
- [运行与结算脚本](#运行与结算脚本)：自动出手、弹道、状态和连锁。
- [公共数据脚本](#公共数据脚本)：一次攻击中传递的信息。
- [战场和实验室接线](#战场和实验室接线)：最后是谁扣血、回血、画特效。
- [每个器官对应哪些文件](#每个器官对应哪些文件)：19 种执行器、10 种触发器、7 种躯干逐一对照。
- [独立演示脚本](#独立演示脚本)：Debug 目录的六个脚本。
- [编辑器与检查脚本](#编辑器与检查脚本)：菜单、Inspector 和各项回归。
- [沿一次直线子弹追代码](#沿一次直线子弹追代码)：用一个例子把全部环节连起来。
- [常见修改应该动哪里](#常见修改应该动哪里)：调参、改规则和新增器官。

## 先分清文件和对象

**`.cs` 定义规则和字段，`.asset` 保存某种器官的具体参数，`.unity` 保存场景中的组件与引用。** 同一个脚本可以服务很多份配置，不需要每个器官复制一份脚本。

以导弹为例，Project 里有两份配置：

1. `Missile.asset` 的类型是 **OrganConfig**，是器官目录收录的外层入口。它的 `actuator` 指向下一份资源。
2. `MissileBehaviour.asset` 的类型是 **PatternActuatorConfig**，保存 Pattern=Missile、速度、伤害、爆炸半径等。改导弹数值主要选这份。
3. 运行时在 `EffectExecutions.cs` 创建 **MovingEffectExecution**。导弹、吸血针、追踪等共用这个类，通过 Pattern 区分分支。

这两份 asset 有实际引用关系，不是重复资源。触发器的 `Hit.asset → HitTrigger.asset`、躯干的 `Overdrive.asset → OverdriveTorso.asset` 也是同样的外层入口与行为参数分工。原 Hand 下六种执行器也已采用入口 + Behaviour 配置；多肢触手迁入 Triggers，采用入口 + Trigger 配置。Hand 资源目录已删除。

**同一种器官与同一件器官也不同。** DefinitionId 例如 `missile` 表示种类；InstanceId 表示背包中的某一件；ExecutionId 表示它某次发射。多件导弹可共用配置，但计时、飞行位置、触发记录不能共用。

**不是所有 cs 都能拖到 GameObject 上。** MonoBehaviour 才是场景组件；ScriptableObject 配置应在 Project 中创建/选择资源；普通 C# 类由运行代码创建。尤其 `BlackHoleBehaviour` 虽然名字含 Behaviour，却不是 MonoBehaviour；`OrganTriggerRuntime.cs` 是 OrganPipelineController 的另一部分，也不是第二个可挂载组件。

当前攻击是自动循环：没有 F 键攻击或玩家攻击充能。代码里遗留的 ChargeCost 是隐藏兼容数据，不参与攻击限制。静止触发器计数和大激光 ChargeTime 是另外两项仍有效的规则。

## 配置与定义脚本

### OrganConfig.cs

[OrganConfig.cs](../Assets/Scripts/Organs/OrganConfig.cs) · ScriptableObject，外层配置入口

**负责什么：** 只关联一份执行器、触发器或躯干配置；目录、库存和场景都通过该入口读取器官。旧 kind/damage 等混合字段已移除。

**从哪里读：** TryCreateDefinition 拒绝没有关联或同时关联多种行为，再转交具体配置。OrganCatalog 从这里获取定义。

**什么时候改：** 改入口关联和配置校验时；伤害、状态、范围等参数应改所引用的 Behaviour/Trigger/Torso 资源。

### DirectActuatorConfig.cs

[DirectActuatorConfig.cs](../Assets/Scripts/Organs/DirectActuatorConfig.cs) · ScriptableObject，直接作用执行器模板

**负责什么：** 统一拳头、钢管、锈刀、粘液腺、坍缩体、TNT：shape 决定单体、爆炸或黑洞，statusEffect 可选。

**从哪里读：** CreateDefinition 读取伤害/范围/黑洞参数，把状态资源转成快照；实际伤害仍经 ParameterisedBehaviour 与 AttackResolver。

**什么时候改：** 六种器官调对应 Behaviour.asset。单体/爆炸可以拖入有效的 StatusEffectConfig，None 表示不附带状态；黑洞只位移，不施加命中状态。

### OrganActuatorConfig.cs

[OrganActuatorConfig.cs](../Assets/Scripts/Organs/OrganActuatorConfig.cs) · 抽象 ScriptableObject，执行器配置基类

**负责什么：** 集中 organId、displayName、damage 和 TryCreateDefinition；把非法参数转为可读错误。

**从哪里读：** 派生类实现 CreateDefinition，基类统一包装创建与错误报告。

**什么时候改：** 所有执行器都需要某个公共字段时；单种弹道参数不要塞入这里。它是基类，通常选择具体子类的 asset。

### StraightProjectileConfig.cs

[StraightProjectileConfig.cs](../Assets/Scripts/Organs/StraightProjectileConfig.cs) · ScriptableObject + 普通参数类

**负责什么：** 定义直线子弹的 speed、hitRadius、lifetime，并生成 StraightProjectileSettings。

**从哪里读：** CreateDefinition 生成带 projectile 设置的 OrganDefinition；StraightProjectileSettings 构造器校验参数。

**什么时候改：** 调现有数值选 StraightProjectileBehaviour.asset；新增直线弹体特有参数才改这个脚本及消费它的执行类。

### PatternActuatorConfig.cs

[PatternActuatorConfig.cs](../Assets/Scripts/Organs/PatternActuatorConfig.cs) · ScriptableObject + 枚举 + 参数快照

**负责什么：** ActuatorPattern 列出另外 12 种效果；配置存储速度、半径、散射、吸血、蓄力、召唤生命等字段；PatternActuatorSettings 是运行快照。

**从哪里读：** CreateDefinition 与 PatternActuatorSettings 构造器；RequiresTarget 指定环绕、毒雾、召唤物可以无目标发动。

**什么时候改：** 调整数据结构或哪些效果需要目标时。修改字段默认值不会覆盖已有 asset 的序列化值。

### OrganTriggerConfig.cs

[OrganTriggerConfig.cs](../Assets/Scripts/Organs/OrganTriggerConfig.cs) · ScriptableObject + TriggerSettings

**负责什么：** 用 condition 区分 9 种条件，保存概率、延迟、击杀门槛、静止积累速率和门槛。

**从哪里读：** TryCreateDefinition 生成 OrganType.Trigger，目标规则由 triggerTarget 选择 SelfSlots（整条管道）或 FollowingSlots（后续槽）；TriggerSettings 校验概率 0～1 和正数门槛。

**什么时候改：** 改可配置条件/字段时。实际如何计数和触发在 OrganTriggerRuntime.cs，不在本文件。

### TorsoOrganConfig.cs

[TorsoOrganConfig.cs](../Assets/Scripts/Organs/TorsoOrganConfig.cs) · ScriptableObject + TorsoSettings + TorsoLoadout

**负责什么：** 配置躯干倍率；TorsoSettings 保存快照和按参数生成的说明；TorsoLoadout 合并已装备躯干。

**从哪里读：** TryCreateDefinition、TorsoLoadout.Set、FrontMultiplier、ScaleDamage。多个倍率相乘，吸血比例相加后限制在 0～1。

**什么时候改：** 改躯干参数结构、叠加规则、前方角度判定时。装卸时整体重算，不靠除回旧倍率。

### StatusEffectConfig.cs

[StatusEffectConfig.cs](../Assets/Scripts/Organs/StatusEffectConfig.cs) · ScriptableObject，状态参数

**负责什么：** 定义眩晕、流血、减速的施加概率、持续时间、流血间隔/伤害、减速层数/比例。

**从哪里读：** TryCreateData 转成 StatusEffectData。Inspector 的 chancePercent 是 0～100，运行数据转成 0～1；不要与新触发器 Probability 的单位混淆。

**什么时候改：** 调状态选 Data/Statuses 下的 Stun、Bleed、Slow；改字段和校验再改脚本。

### OrganDefinition.cs

[OrganDefinition.cs](../Assets/Scripts/Organs/OrganDefinition.cs) · 普通 C# 类，运行时只读定义

**负责什么：** 把器官身份、类别、伤害、状态、范围、弹体、触发器和躯干设置放入一个经过校验的定义；CanAttack 等属性供调度判断。

**从哪里读：** 构造器、MountKind、CanAttack。主场景和实验室读取相同定义，配置运行中不会自动热更新到既有快照。

**什么时候改：** 改各系统共同依赖的定义结构时。它不保存飞行位置、目标当前血量或某件触发器的计数。

### OrganInstance.cs

[OrganInstance.cs](../Assets/Scripts/Organs/OrganInstance.cs) · 普通 C# 类，一件器官

**负责什么：** 将一个 InstanceId 与共享 OrganDefinition 绑定。同一种配置可以创建多个不同实例。

**从哪里读：** 构造器可接受库存已有 instanceId，否则创建新 ID。

**什么时候改：** 改实例身份和数据传递时；同步正式库存必须保留原 ID，避免伤害归因或重复装备判断错位。

### OrganCatalog.cs

[OrganCatalog.cs](../Assets/Scripts/Organs/OrganCatalog.cs) · ScriptableObject，配置目录

**负责什么：** 维护 OrganConfig 引用数组；一次读取全部定义，拒绝缺项、重复 ID 或无效配置。

**从哪里读：** CreateDefinitions；LoadDefault 用 Resources.Load 加载 Resources/OrganCatalog.asset。

**什么时候改：** 新增器官时把外层 OrganConfig 加入目录。改检测或目录加载方式才动脚本，不能只创建行为 asset 就期待自动出现在游戏里。

## 运行与结算脚本

### BodyRuntime.cs

[BodyRuntime.cs](../Assets/Scripts/Organs/BodyRuntime.cs) · 普通 C# 类，一条六槽管道

**负责什么：** 保存该部位的槽位、游标、步进时间和整轮周期；执行时跳过空槽，防止同一实例重复装配。

**从哪里读：** SetSlot / GetSlot 管装配；Tick、TickCycle、BeginRound 管节拍；ResetTiming 清计时；ExecuteAllSlots 供旧触手追加扫描。

**什么时候改：** 改空槽处理、循环起点、槽位先后顺序时。单纯调攻击速度先改 PlayerStatsBase 的实际场景值。

### OrganPipelineController.cs

[OrganPipelineController.cs](../Assets/Scripts/Organs/OrganPipelineController.cs) · MonoBehaviour，三条管道的统一入口

**负责什么：** 持有头、手、腿三个 BodyRuntime；自动推进，询问目标，生成旧攻击事件或新执行器上下文，并分派触发信号。

**从哪里读：** Update → AdvanceAutomatic → BodyRuntime.Tick → ForwardSlotExecuted → ExecuteAttack；PublishSignal / ProcessSignal 处理事件队列；TryExecuteSlot 是工具可调用的单槽入口。

**什么时候改：** 改出手条件、目标规则或管道事件时；现有触手的范围规则在 OrganTriggerRuntime。管道负责发出攻击意图，真正扣敌人血由战场适配完成。

### OrganTriggerRuntime.cs

[OrganTriggerRuntime.cs](../Assets/Scripts/Organs/OrganTriggerRuntime.cs) · OrganPipelineController 的 partial 文件

**负责什么：** 保存每件新触发器的计数和待执行计时；检查最近前级；条件成立后按 triggerTarget 扫描整条管道或后方执行器。

**从哪里读：** ProcessConfiguredTrigger、IsPrevious、ActivateConfigured、AdvanceTriggers、ResetTriggers；NotifyExecutionStarted/Ended 与 NotifyOwner 是事件入口。

**什么时候改：** 改 9 种触发规则时；新条件既要有消费逻辑，也要让战场/预览实际发布对应信号。取消效果不当成正常失效触发。

### ParameterisedBehaviour.cs

[ParameterisedBehaviour.cs](../Assets/Scripts/Organs/ParameterisedBehaviour.cs) · 静态逻辑辅助类

**负责什么：** 从旧器官定义创建 CastEvent，并判断触发器是否匹配信号。它不扣血、不画特效。

**从哪里读：** TryCreateAttack：有 projectile 设置的新执行器不走旧直接攻击路径；Matches：按 TriggerOn 匹配。

**什么时候改：** 改旧直接攻击生成条件时。不要因为名字通用就把所有新弹道逻辑写进这里。

### AttackTargeting.cs

[AttackTargeting.cs](../Assets/Scripts/Organs/AttackTargeting.cs) · 静态索敌算法

**负责什么：** 过滤死亡/非法/超距离目标，选择最近有效目标；同距保持候选顺序。索敌距离与效果半径独立。

**从哪里读：** SelectNearest、IsInRange，DefaultRange=3 是默认值。调用方提供目标快照和实际范围。

**什么时候改：** 改“选谁攻击”的通用规则时；调实战范围优先检查 BattleOrganTargeting 的 Inspector 值。

### AttackResolver.cs

[AttackResolver.cs](../Assets/Scripts/Organs/AttackResolver.cs) · AttackTarget 快照 + 静态命中名单算法

**负责什么：** 为旧单体/TNT 攻击生成固定命中名单；范围目标去重，边界算命中，黑洞交给持续逻辑。

**从哪里读：** Resolve 返回每个目标的 CastEvent，不直接扣血；使用 EffectCenter 作为爆炸中心。

**什么时候改：** 改旧范围命中判定时。新子弹/激光的碰撞查询主要在 World 适配中，不能只改这里。

### ActuatorRuntime.cs

[ActuatorRuntime.cs](../Assets/Scripts/Organs/ActuatorRuntime.cs) · 普通类与接口集合

**负责什么：** 定义 ActuatorContext、ActuatorHit、IActuatorWorld、IOrganActuator、IActuatorExecution、ActuatorRunner，以及直线弹体实现和预览几何辅助。

**从哪里读：** StraightProjectileExecution.Tick 移动并扫掠；ActuatorRunner.Start/Advance/Clear 管生命周期。StraightProjectileActuator.Create 实际调用 EffectExecutionFactory，名称虽旧，入口服务所有新执行器。

**什么时候改：** 改直线弹运动/命中/寿命或所有效果的生命周期协议时。Runner 清理会 Cancel，必须保留来源与结束原因。

### EffectExecutions.cs

[EffectExecutions.cs](../Assets/Scripts/Organs/EffectExecutions.cs) · 普通执行类集合

**负责什么：** 实现其余 12 种新效果，定义 IEffectWorld 与可视数据 EffectVisual；共享基类 EffectExecution 管时间、伤害和结束。

**从哪里读：** 先看文件末尾 EffectExecutionFactory.Create 的 Pattern 分流，再跳到 MovingEffectExecution、TripleProjectileExecution、BeamExecution、FogExecution、SuckerExecution 或 SummonExecution。

**什么时候改：** 改弹道、爆炸、持续区域、抓取、激光或召唤行为时。一个文件有多个类，只改对应类/分支，别把共同规则误改为某个器官专用。

### StatusEffectRuntime.cs

[StatusEffectRuntime.cs](../Assets/Scripts/Organs/StatusEffectRuntime.cs) · 普通 C# 类，一个目标的状态容器

**负责什么：** 记录眩晕剩余时间、流血周期、减速层数/衰减；给出速度倍率，保留流血伤害来源链。

**从哪里读：** TryApply 决定是否施加；Tick 推进状态和流血；MovementSeconds / SpeedMultiplier 供移动适配；Clear 清理。

**什么时候改：** 改状态刷新、叠层、衰减或流血频率结算时。每个目标有自己的实例，不能在配置 asset 中共用这些状态。

### BlackHoleBehaviour.cs

[BlackHoleBehaviour.cs](../Assets/Scripts/Organs/BlackHoleBehaviour.cs) · 普通 C# 类，不是组件

**负责什么：** 表示旧坍缩体生成的一次黑洞：中心、来源、持续时间，以及把某个位置向中心拉动的计算。

**从哪里读：** 构造器接收 CastEvent；PullPosition 计算结果；Tick 推进寿命。实战 BattleOrganCombat 负责真正移动敌人。

**什么时候改：** 改黑洞吸引计算或寿命时。半径/持续/速度数值先改 CollapseBodyBehaviour.asset。

### CastContext.cs

[CastContext.cs](../Assets/Scripts/Organs/CastContext.cs) · 普通 C# 类，一条触发链

**负责什么：** 保存 ChainId、Depth 和该链已使用的触发器实例集合，限制同实例同链重复触发和最大链深。

**从哪里读：** CanEnterTrigger / TryEnterTrigger；MaxChainDepth 当前为 4，追加攻击沿用链身份。

**什么时候改：** 调整连锁保护时；不要为了让触发“更容易”随意为每次追加创建新链，否则可能无限触发。

### CriticalDamage.cs

[CriticalDamage.cs](../Assets/Scripts/Organs/CriticalDamage.cs) · 静态暴击计算

**负责什么：** 根据概率、倍率和传入的随机值生成带暴击伤害的攻击数据，保持来源与链信息。

**从哪里读：** 查看计算入口及 BattleOrganCombat 的调用；是否发布 Critical 信号由结算层接线。

**什么时候改：** 改暴击计算规则时。单纯改玩家暴击参数应先查看 PlayerStatsBase。

## 公共数据脚本

### OrganTypes.cs

[OrganTypes.cs](../Assets/Scripts/Core/OrganTypes.cs) · 枚举与装备规则

**负责什么：** 定义 BodyPart、OrganType、OrganMountKind、AttackShape、TriggerOn、TriggerTargetKind、TriggerBlockReason 及 OrganEquipmentRules。

**从哪里读：** Head/Hand/Leg 是普通管道；Torso 是类别/装配类型，不是第四条 BodyRuntime 管道。

**什么时候改：** 新增类型时检查所有分支与序列化枚举值。不要直接重排现有枚举，旧 asset 可能按数值保存。

### CastEvent.cs

[CastEvent.cs](../Assets/Scripts/Core/CastEvent.cs) · readonly struct，攻击数据

**负责什么：** 携带目标、伤害、定义/实例来源、链、状态、范围、黑洞参数和效果中心快照。

**从哪里读：** 构造器校验；IsFinite 验证位置。后续血量变化、触发和统计依赖其中的来源信息。

**什么时候改：** 增加需要跨系统传递的攻击信息时。它不等于已命中或已造成同额实际伤害。

### TriggerSignal.cs

[TriggerSignal.cs](../Assets/Scripts/Core/TriggerSignal.cs) · readonly struct，触发信号

**负责什么：** 把 TriggerOn 条件、造成它的 CastEvent、事件位置一起交给管道。

**从哪里读：** 构造器与 Kind / Cause / Position；击杀位置可保留敌人死亡时的快照。

**什么时候改：** 增加事件所需数据时。发信号不直接扣血，条件由触发器运行逻辑判断。

### StatusEffectData.cs

[StatusEffectData.cs](../Assets/Scripts/Core/StatusEffectData.cs) · 状态枚举与只读参数类

**负责什么：** 定义 Stun/Bleed/Slow 和概率、时间、周期、层数等已校验参数。

**从哪里读：** StatusEffectConfig.TryCreateData 创建它，StatusEffectRuntime 使用它。

**什么时候改：** 改状态参数结构时，与配置和运行逻辑同步；这是数据，不是每个目标的计时器。

### BlackHoleData.cs

[BlackHoleData.cs](../Assets/Scripts/Core/BlackHoleData.cs) · 只读参数类

**负责什么：** 保存黑洞持续时间与吸引速度；位置、范围和来源随 CastEvent 传入。

**从哪里读：** 构造器校验参数，BlackHoleBehaviour 读取。

**什么时候改：** 增加黑洞共享参数时。当前某个黑洞剩余多久不是放在此处。

## 战场和实验室接线

以下是与器官有关的跨模块文件。它们不是每种器官各一份，而是把同一套规则放进真实战场或预览。

### BattleOrganTargeting.cs

[BattleOrganTargeting.cs](../Assets/Scripts/Systems/BattleOrganTargeting.cs)

场景组件，给管道安装 TargetSelector / TargetValidator / TargetSnapshotProvider；只从本场景真实活敌人中索敌。查“不出手/选错目标”先看它和 AttackTargeting。

### BattleActuatorWorld.cs

[BattleActuatorWorld.cs](../Assets/Scripts/Systems/BattleActuatorWorld.cs)

普通类，实现 IEffectWorld。Sweep / SweepWall 接 Physics2D；BeamTargets 查激光目标并按身份去重；Damage 返回实际扣血；Heal、Slow、NotifyDebuff 接回血/减速/事件。查“穿墙/重复命中/吸血不准”看这里与执行类。

### BattleOrganCombat.cs

[BattleOrganCombat.cs](../Assets/Scripts/Systems/BattleOrganCombat.cs)

场景组件，订阅管道输出，ResolveDamage 结算真实敌人，管理 Runner、状态和黑洞；Advance / FixedUpdate 推进持续效果及触发计时；Clear 清场。这里接暴击、实际吸血、玩家受伤/回复、命中/击杀/减益/执行开始结束信号及报告。

### BattleOrganEffects.cs

[BattleOrganEffects.cs](../Assets/Scripts/Systems/BattleOrganEffects.cs)

场景组件，使用 LineRenderer 等显示旧爆炸/黑洞和新弹体/光束/区域的占位表现，并回收显示对象。改变颜色形状先看这里；伤害数字不在这里算。

### TorsoEquipmentRuntime.cs

[TorsoEquipmentRuntime.cs](../Assets/Scripts/Systems/TorsoEquipmentRuntime.cs)

运行时由主驱动创建的组件，实现 C 的 IStatModifierSource。Configure 绑定玩家，Apply 重算躯干；更新生命、受伤倍率、无敌、移动、转向及管道概率/频率。卸下时不是永久覆盖基础参数。

### HandPipelineLoadout.cs

[HandPipelineLoadout.cs](../Assets/Scripts/Systems/HandPipelineLoadout.cs)

普通静态适配。文件里同时有旧 HandPipelineLoadout 和现行 PipelineLoadout；后者把正式库存映射到头/手/腿，保留 InstanceId 并转换槽号。文件名里的 Hand 不表示现在只支持手部。

### OrganCombatReport.cs

[OrganCombatReport.cs](../Assets/Scripts/Systems/OrganCombatReport.cs)

运行时报告组件。Bind 订阅成功发动与连锁，记录本波实际伤害、最多 64 条历史与槽位闪亮状态；ResetWave 清波次数据。它不决定器官何时发动，也不计算基础伤害。

### RunEquipmentHud.cs

[RunEquipmentHud.cs](../Assets/Scripts/Systems/RunEquipmentHud.cs)

运行时创建的装备界面，显示三条六槽、躯干、自动攻击提示和伤害/触发记录。想改左上角排版或颜色看这里；装备权限仍在库存/流程层。

### SpringUpRunDriver.cs

[SpringUpRunDriver.cs](../Assets/Scripts/Systems/SpringUpRunDriver.cs)

主场景接线总入口。读取目录和库存、应用装备与躯干、配置各管道节奏、切换战斗/休息、清场/重开、同步相机视野。节奏来自 PlayerStatsBase，而不是每个执行器重复计时。

### RunLaboratoryEquipment.cs

[RunLaboratoryEquipment.cs](../Assets/Scripts/Systems/RunLaboratoryEquipment.cs)

正式库存与实验室界面的适配，装卸/替换调用 D 的 RunController；不是另一份独立实战库存。

### LaboratoryCombatDemo.cs

[LaboratoryCombatDemo.cs](../Assets/Scripts/UI/Laboratory/LaboratoryCombatDemo.cs)

实验室核心预览组件，创建独立目标和模拟 World，复用管道、执行器、状态、触发器与躯干。控制预览时间、重置、目标补充和条件模拟。查“实战有效、预览无效”时与 BattleActuatorWorld 对照。

### EffectPreviewGraphic.cs

[EffectPreviewGraphic.cs](../Assets/Scripts/UI/Laboratory/EffectPreviewGraphic.cs)

UI 绘制类，将效果的几何信息画在预览面板；不自行判断命中或扣血。

### TemporaryEquipment.cs

[TemporaryEquipment.cs](../Assets/Scripts/UI/Laboratory/TemporaryEquipment.cs)

独立预览的物品描述、装配接口与临时库存；正式场景通过 RunLaboratoryEquipment 使用 D 库存。修改卡片中参数文字可从 LaboratoryItem 的描述逻辑看起。

### LaboratoryDemoBootstrap.cs

[LaboratoryDemoBootstrap.cs](../Assets/Scripts/UI/Laboratory/LaboratoryDemoBootstrap.cs)

选择共享目录并初始化独立实验室样例；正式场景会 Bind 正式装备源。不要把独立样例生成当成正式掉落。

### LaboratoryPresenter.cs

[LaboratoryPresenter.cs](../Assets/Scripts/UI/Laboratory/LaboratoryPresenter.cs)

实验室的选择、安装/拆下按钮、卡片和刷新接线。器官正确但按钮无响应时从这里追。

### OrganCardView.cs

[OrganCardView.cs](../Assets/Scripts/UI/Laboratory/OrganCardView.cs)

单张物品卡片的展示与选择反馈，不保存器官运行行为。

**还会读到的 C / D 文件：** `Assets/Scripts/Battle/PlayerStatsBase.cs` 管基础属性，`PlayerRuntime.cs` 管实际生命与属性、回血和伤害事件，`PlayerMotor.cs` 执行转向移动，`EnemyRuntime.cs` 执行实际受伤，`EnemyAttackFlash.cs` 显示受伤闪色。`Assets/Scripts/Systems/Inventory.cs` / `RunController.cs` 管唯一库存与休息期权限。这些是器官接入的消费者，不是某一种器官的专用实现。

## 每个器官对应哪些文件

以下覆盖目录中的全部 **36 种战斗定义**。每一项都区分“参数资源”和“行为源码”。这里不重复整本数值表，具体当前默认值见 [调参手册第 8 节](开发改动总览与调参手册.md#8-新器官的具体文件与参数)。

### 从 Hand 迁入的六种执行器与多肢触手

#### 拳头（`fist`）

**目录入口：** [Fist.asset](../Assets/Data/Organs/Actuators/Fist.asset)。

**参数资源：** [FistBehaviour.asset](../Assets/Data/Organs/Actuators/FistBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs → ParameterisedBehaviour.TryCreateAttack → AttackResolver.Resolve → BattleOrganCombat.ResolveDamage。

**修改提示：** 基础单体直接伤害；没有 Fist.cs，也没有实体飞行弹体。改 damage 用本资源，改变出手判定看管道/索敌。

#### 钢管（`steel_pipe`）

**目录入口：** [SteelPipe.asset](../Assets/Data/Organs/Actuators/SteelPipe.asset)。

**参数资源：** [SteelPipeBehaviour.asset](../Assets/Data/Organs/Actuators/SteelPipeBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs + StatusEffectConfig.cs + StatusEffectRuntime.TryApply。

**修改提示：** 直接伤害并概率眩晕；伤害改 SteelPipeBehaviour.asset，概率/时长改 Data/Statuses/Stun.asset。眩晕最终通过 BattleOrganCombat 设置敌人移动倍率。

#### 多肢触手（`multi_tentacle`）

**目录入口：** [MultiTentacle.asset](../Assets/Data/Organs/Triggers/MultiTentacle.asset)。

**参数资源：** [MultiTentacleTrigger.asset](../Assets/Data/Organs/Triggers/MultiTentacleTrigger.asset)，类型 `OrganTriggerConfig.cs`。

**对应逻辑：** OrganTriggerConfig.cs → OrganTriggerRuntime.ProcessConfiguredTrigger → ActivateConfigured（SelfSlots）。

**修改提示：** 击杀触发器；追加扫描自身整条管道，不是新 KillCount 的“攒 10 杀 + 后续槽”。它自己没有直接伤害。修改规则看 MultiTentacleTrigger.asset 的 Kill Threshold / Trigger Target，以及 ActivateConfigured 与 CastContext。

#### 锈刀（`rust_knife`）

**目录入口：** [RustKnife.asset](../Assets/Data/Organs/Actuators/RustKnife.asset)。

**参数资源：** [RustKnifeBehaviour.asset](../Assets/Data/Organs/Actuators/RustKnifeBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs + StatusEffectRuntime.TryApply / Tick。

**修改提示：** 直接伤害并概率流血；流血参数改 Data/Statuses/Bleed.asset，持续伤害保留原器官与触发链来源。

#### 粘液腺（`slime_gland`）

**目录入口：** [SlimeGland.asset](../Assets/Data/Organs/Actuators/SlimeGland.asset)。

**参数资源：** [SlimeGlandBehaviour.asset](../Assets/Data/Organs/Actuators/SlimeGlandBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs + StatusEffectRuntime.TryApply / DecaySlow。

**修改提示：** 直接伤害并概率叠减速；层数、每层幅度、减层时间改 Data/Statuses/Slow.asset。不要把这个叠层减速与吸盘的独立减速句柄混为一套。

#### 坍缩体（`collapse_body`）

**目录入口：** [CollapseBody.asset](../Assets/Data/Organs/Actuators/CollapseBody.asset)。

**参数资源：** [CollapseBodyBehaviour.asset](../Assets/Data/Organs/Actuators/CollapseBodyBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs → BlackHoleData → BlackHoleBehaviour.PullPosition / Tick → BattleOrganCombat.Advance。

**修改提示：** 生成目标位置黑洞并吸引可位移敌人，本身不扣血；同一实例再次发动替换它自己的旧黑洞。半径、寿命和吸引速度都在 CollapseBodyBehaviour.asset。

#### TNT（`tnt`）

**目录入口：** [TNT.asset](../Assets/Data/Organs/Actuators/TNT.asset)。

**参数资源：** [TNTBehaviour.asset](../Assets/Data/Organs/Actuators/TNTBehaviour.asset)，类型 `DirectActuatorConfig.cs`。

**对应逻辑：** DirectActuatorConfig.cs → AttackResolver.Resolve → BattleOrganCombat.ResolveDamage。

**修改提示：** 以目标位置快照为中心直接范围伤害，每目标一次；不是导弹飞行后爆炸。调 damage / explosionRadius；范围表现由 BattleOrganEffects 显示。

### 新增十三种执行器

这 13 项的外层入口均是 OrganConfig。直线子弹参数类型为 StraightProjectileConfig，其余 12 项为 PatternActuatorConfig；以下执行类除直线子弹外均位于 [EffectExecutions.cs](../Assets/Scripts/Organs/EffectExecutions.cs)。所有执行最终经 World 接入实战/预览，不能在执行类里直接查找某个场景 Enemy 对象。

#### 1. 直线子弹（`straight_projectile`）

**目录入口：** [StraightProjectile.asset](../Assets/Data/Organs/Actuators/StraightProjectile.asset)。

**调参资源：** [StraightProjectileBehaviour.asset](../Assets/Data/Organs/Actuators/StraightProjectileBehaviour.asset)；类型 `StraightProjectileConfig.cs`。

**行为代码：** `StraightProjectileExecution.Tick（ActuatorRuntime.cs）`。

**常调字段：** `speed / hitRadius / lifetime / damage`。

**当前行为 / 修改提示：** 沿发射时方向前进，段扫掠检测首次命中；敌人受伤后消失，遇墙消失。转向不在本效果中实现。

#### 2. 环绕子弹（`orbit_projectile`）

**目录入口：** [Orbit.asset](../Assets/Data/Organs/Actuators/Orbit.asset)。

**调参资源：** [OrbitBehaviour.asset](../Assets/Data/Organs/Actuators/OrbitBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Orbit`。

**行为代码：** `MovingEffectExecution.Advance 的 Orbit 分支`。

**常调字段：** `orbitRadius / angularSpeed / hitRadius / lifetime / damage`。

**当前行为 / 修改提示：** 每次更新用当前角色位置作为圆心旋转；无需敌人目标即可生成，命中后消失。

#### 3. 螺旋子弹（`spiral_projectile`）

**目录入口：** [Spiral.asset](../Assets/Data/Organs/Actuators/Spiral.asset)。

**调参资源：** [SpiralBehaviour.asset](../Assets/Data/Organs/Actuators/SpiralBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Spiral`。

**行为代码：** `MovingEffectExecution.Advance 的 Spiral 分支`。

**常调字段：** `speed / orbitRadius / angularSpeed / lifetime / damage`。

**当前行为 / 修改提示：** 以起点和发射方向向前推进，叠加旋转偏移。改轨迹公式就在该分支。

#### 4. 激光（`laser`）

**目录入口：** [Laser.asset](../Assets/Data/Organs/Actuators/Laser.asset)。

**调参资源：** [LaserBehaviour.asset](../Assets/Data/Organs/Actuators/LaserBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Laser`。

**行为代码：** `BeamExecution.Advance`。

**常调字段：** `range / hitRadius / damage`。

**当前行为 / 修改提示：** 单次命中路径上的有效敌人，先由 SweepWall 截断光束；不是持续每帧扣血。宽度与 hitRadius 对应。

#### 5. 追踪子弹（`homing_projectile`）

**目录入口：** [Homing.asset](../Assets/Data/Organs/Actuators/Homing.asset)。

**调参资源：** [HomingBehaviour.asset](../Assets/Data/Organs/Actuators/HomingBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Homing`。

**行为代码：** `MovingEffectExecution.Advance 的 Homing 分支`。

**常调字段：** `speed / angularSpeed / lifetime / damage`。

**当前行为 / 修改提示：** 追踪发射时选择的 TargetId，按最大转向速度修正方向。目标失效后沿最后方向继续飞，不重新选敌。

#### 6. 毒雾（`poison_fog`）

**目录入口：** [Fog.asset](../Assets/Data/Organs/Actuators/Fog.asset)。

**调参资源：** [FogBehaviour.asset](../Assets/Data/Organs/Actuators/FogBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Fog`。

**行为代码：** `FogExecution.Advance → EffectExecution.Area`。

**常调字段：** `areaRadius / interval / lifetime / damage`。

**当前行为 / 修改提示：** 生成后留在原位置，定期对区域目标结算；不跟随角色。无需敌方目标即可发动。

#### 7. 吸血针（`vampire_needle`）

**目录入口：** [Vampire.asset](../Assets/Data/Organs/Actuators/Vampire.asset)。

**调参资源：** [VampireBehaviour.asset](../Assets/Data/Organs/Actuators/VampireBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Vampire`。

**行为代码：** `MovingEffectExecution.Advance 的命中分支`。

**常调字段：** `lifeSteal / damage / speed / hitRadius / lifetime`。

**当前行为 / 修改提示：** 直线弹命中后取实际直接伤害，调用 World.Heal(actual * LifeSteal)。过量伤害不算额外回血；实战回血最终进 PlayerRuntime。

#### 8. 三发子弹（`triple_projectile`）

**目录入口：** [Triple.asset](../Assets/Data/Organs/Actuators/Triple.asset)。

**调参资源：** [TripleBehaviour.asset](../Assets/Data/Organs/Actuators/TripleBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Triple`。

**行为代码：** `TripleProjectileExecution 构造器 / Advance；内部三个 StraightProjectileExecution`。

**常调字段：** `spreadAngle / damage / speed / hitRadius / lifetime`。

**当前行为 / 修改提示：** 按 -spreadAngle/2、0、+spreadAngle/2 发射；60° 是总夹角。每一发各自碰撞/结束，伤害参数是单发伤害。

#### 9. 章鱼吸盘（`octopus_sucker`）

**目录入口：** [Sucker.asset](../Assets/Data/Organs/Actuators/Sucker.asset)。

**调参资源：** [SuckerBehaviour.asset](../Assets/Data/Organs/Actuators/SuckerBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Sucker`。

**行为代码：** `SuckerExecution.Advance / Finish`。

**常调字段：** `slowMultiplier / interval / lifetime / damage / speed`。

**当前行为 / 修改提示：** 先飞行，命中后跟随目标位置并定期伤害；World.Slow 创建独立减速句柄，结束时 Dispose 移除自己的减速。slowMultiplier=.5 表示剩余速度 50%。

#### 10. 导弹（`missile`）

**目录入口：** [Missile.asset](../Assets/Data/Organs/Actuators/Missile.asset)。

**调参资源：** [MissileBehaviour.asset](../Assets/Data/Organs/Actuators/MissileBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Missile`。

**行为代码：** `MovingEffectExecution.Advance 的 Missile 命中分支`。

**常调字段：** `damage / explosionDamage / areaRadius / speed / lifetime`。

**当前行为 / 修改提示：** 直线飞行后直接伤害，再进行一次区域爆炸；主目标可同时承受两部分伤害。改 explosionDamage 不会改变直接 damage。

#### 11. 大激光（`charged_laser`）

**目录入口：** [ChargedLaser.asset](../Assets/Data/Organs/Actuators/ChargedLaser.asset)。

**调参资源：** [ChargedLaserBehaviour.asset](../Assets/Data/Organs/Actuators/ChargedLaserBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=ChargedLaser`。

**行为代码：** `BeamExecution.Charge / Advance`。

**常调字段：** `chargeTime / range / hitRadius / damage`。

**当前行为 / 修改提示：** 与普通激光共用类，但先等待蓄力；起点方向按发射上下文锁定，取消则不发射。更粗和更高伤害由本资源数值决定。

#### 12. 孢子（`spore`）

**目录入口：** [Spore.asset](../Assets/Data/Organs/Actuators/Spore.asset)。

**调参资源：** [SporeBehaviour.asset](../Assets/Data/Organs/Actuators/SporeBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Spore`。

**行为代码：** `MovingEffectExecution.Advance 的 Spore 分支`。

**常调字段：** `driftDuration / speed / lifetime / hitRadius / damage`。

**当前行为 / 修改提示：** 只在漂浮时段前进，之后原地继续等待碰撞，命中后消失。漂浮时间不能超过总寿命。

#### 13. 跟随召唤物（`summon_follower`）

**目录入口：** [Summon.asset](../Assets/Data/Organs/Actuators/Summon.asset)。

**调参资源：** [SummonBehaviour.asset](../Assets/Data/Organs/Actuators/SummonBehaviour.asset)；类型 `PatternActuatorConfig.cs · Pattern=Summon`。

**行为代码：** `SummonExecution.Advance / TakeDamage`。

**常调字段：** `health / healthDecay / followDistance / speed`。

**当前行为 / 修改提示：** 跟随角色并自然扣血，寿命主要由 health / healthDecay 决定；无需敌方目标。当前不主动攻击，敌人也未接专门选它为目标的逻辑。不能仅调 damage 就让它攻击。

### 其余九种触发器（共十份配置、九种条件）

全部用 [OrganTriggerConfig.cs](../Assets/Scripts/Organs/OrganTriggerConfig.cs) 定义参数，实际逻辑在 [OrganTriggerRuntime.cs](../Assets/Scripts/Organs/OrganTriggerRuntime.cs)。这九份配置默认由 ActivateConfigured 发动同管道后面的执行器；上面已列出的多肢触手使用整条管道；不会把这些执行器从普通自动循环中移除。每件触发器独立计数；同链保护由 CastContext 处理。

#### 击中概率

**资源链：** [Hit.asset](../Assets/Data/Organs/Triggers/Hit.asset) → [HitTrigger.asset](../Assets/Data/Organs/Triggers/HitTrigger.asset)。

**源码入口：** `ProcessConfiguredTrigger 的 Hit 概率分支`；`Condition=Hit`；调 `Probability（0～1）`。

**事件来源 / 规则：** BattleOrganCombat 在实际命中结算后发布 Hit。

#### 前级开始后计时

**资源链：** [Delay.asset](../Assets/Data/Organs/Triggers/Delay.asset) → [DelayTrigger.asset](../Assets/Data/Organs/Triggers/DelayTrigger.asset)。

**源码入口：** `ProcessConfiguredTrigger + IsPrevious + scheduledTriggers，AdvanceTriggers 到期执行`；`Condition=Started`；调 `Delay`。

**事件来源 / 规则：** 只认同管道最近前级执行器开始；默认 .5 秒是样板参数，不是全局攻击间隔。

#### 击杀计数

**资源链：** [KillCount.asset](../Assets/Data/Organs/Triggers/KillCount.asset) → [KillCountTrigger.asset](../Assets/Data/Organs/Triggers/KillCountTrigger.asset)。

**源码入口：** `ProcessConfiguredTrigger 中累加 Kills、达到门槛扣减`；`Condition=Kill`；调 `Kill Threshold`。

**事件来源 / 规则：** 默认每 10 杀触发并扣 10，余数保留；与旧多肢触手的每次击杀扫描全管道不同。

#### 受伤

**资源链：** [Hurt.asset](../Assets/Data/Organs/Triggers/Hurt.asset) → [HurtTrigger.asset](../Assets/Data/Organs/Triggers/HurtTrigger.asset)。

**源码入口：** `NotifyOwner → ProcessConfiguredTrigger`；`Condition=Hurt`；调 `主要是 Condition`。

**事件来源 / 规则：** BattleOrganCombat.OwnerHurt 连接 PlayerRuntime 实际受伤事件；不能用敌人的原始伤害请求代替。

#### 回复

**资源链：** [Heal.asset](../Assets/Data/Organs/Triggers/Heal.asset) → [HealTrigger.asset](../Assets/Data/Organs/Triggers/HealTrigger.asset)。

**源码入口：** `NotifyOwner → ProcessConfiguredTrigger`；`Condition=Heal`；调 `主要是 Condition`。

**事件来源 / 规则：** BattleOrganCombat.OwnerHealed 连接实际回复；生命已满而未发生回复不算。

#### 前级失效

**资源链：** [End.asset](../Assets/Data/Organs/Triggers/End.asset) → [EndTrigger.asset](../Assets/Data/Organs/Triggers/EndTrigger.asset)。

**源码入口：** `NotifyExecutionEnded + ProcessConfiguredTrigger + IsPrevious`；`Condition=Ended`；调 `主要是 Condition`。

**事件来源 / 规则：** 监听同管道最近前级自然结束；清场 Cancel 不触发。

#### 施加减益概率

**资源链：** [Debuff.asset](../Assets/Data/Organs/Triggers/Debuff.asset) → [DebuffTrigger.asset](../Assets/Data/Organs/Triggers/DebuffTrigger.asset)。

**源码入口：** `ProcessConfiguredTrigger 的 Debuff 概率分支`；`Condition=Debuff`；调 `Probability（0～1）`。

**事件来源 / 规则：** 旧状态在 BattleOrganCombat 发布，新吸盘通过 World.NotifyDebuff 发布；只改变视觉不能触发。

#### 暴击

**资源链：** [Critical.asset](../Assets/Data/Organs/Triggers/Critical.asset) → [CriticalTrigger.asset](../Assets/Data/Organs/Triggers/CriticalTrigger.asset)。

**源码入口：** `ProcessConfiguredTrigger 的条件匹配`；`Condition=Critical`；调 `主要是 Condition`。

**事件来源 / 规则：** BattleOrganCombat 结算暴击后发布；暴击概率本身在玩家/躯干属性侧，触发器不是增加暴击率的器官。

#### 静止充能

**资源链：** [Stationary.asset](../Assets/Data/Organs/Triggers/Stationary.asset) → [StationaryTrigger.asset](../Assets/Data/Organs/Triggers/StationaryTrigger.asset)。

**源码入口：** `AdvanceTriggers 检查实际位置/MovingProvider，累加 Charge`；`Condition=Stationary`；调 `Charge Rate / Charge Threshold`。

**事件来源 / 规则：** 默认每秒 1 点、满 10 点触发；移动清零，触发扣门槛。这是独立器官计数，取消玩家攻击充能后仍有效。

### 七种躯干

全部由 [TorsoOrganConfig.cs](../Assets/Scripts/Organs/TorsoOrganConfig.cs) 的 TorsoSettings / TorsoLoadout 表达，装备后由 [TorsoEquipmentRuntime.cs](../Assets/Scripts/Systems/TorsoEquipmentRuntime.cs) 应用。它们不是按管道发射的弹体，因此没有某个 OverdriveExecution 类。下面的数值是当前样板值，实际以你保存的 asset 为准。

#### 过载躯干

**资源链：** [Overdrive.asset](../Assets/Data/Organs/Torso/Overdrive.asset) → [OverdriveTorso.asset](../Assets/Data/Organs/Torso/OverdriveTorso.asset)。

**重点参数：** `damage=1.2、probability=1.2、frequency=1.2、maxHealth=.6`。

**实际生效位置：** Apply 更新管道概率/频率；CollectMultiplicativeModifiers 更新伤害、生命、节拍等属性。频率越大，等待时间越短。

#### 共生躯干

**资源链：** [Leech.asset](../Assets/Data/Organs/Torso/Leech.asset) → [LeechTorso.asset](../Assets/Data/Organs/Torso/LeechTorso.asset)。

**重点参数：** `damage=.8、lifeSteal=.05`。

**实际生效位置：** BattleOrganCombat.ApplyTorsoHealing 按实际伤害回血，和吸血针局部 LifeSteal 是两项可同时存在的效果。

#### 冲锋躯干

**资源链：** [Ram.asset](../Assets/Data/Organs/Torso/Ram.asset) → [RamTorso.asset](../Assets/Data/Organs/Torso/RamTorso.asset)。

**重点参数：** `frontDamage=1.6、frontHalfAngle=45、moveSpeed=2、turnSpeed=.6、directionalMovement=true`。

**实际生效位置：** TorsoLoadout.FrontMultiplier 判前方；BattleOrganCombat 使用结算时目标方向；PlayerMotor 执行定向转向移动。

#### 远视躯干

**资源链：** [WideView.asset](../Assets/Data/Organs/Torso/WideView.asset) → [WideViewTorso.asset](../Assets/Data/Organs/Torso/WideViewTorso.asset)。

**重点参数：** `view=1.2`。

**实际生效位置：** TorsoEquipmentRuntime 更新视野属性；SpringUpRunDriver.LateUpdate 把 Loadout.View 作用到 Camera.orthographicSize。

#### 聚焦躯干

**资源链：** [Focused.asset](../Assets/Data/Organs/Torso/Focused.asset) → [FocusedTorso.asset](../Assets/Data/Organs/Torso/FocusedTorso.asset)。

**重点参数：** `view=.8、damage=1.2`。

**实际生效位置：** 视野由 SpringUpRunDriver 消费，伤害由玩家属性和 BattleOrganCombat 结算。

#### 长庇护躯干

**资源链：** [LongGuard.asset](../Assets/Data/Organs/Torso/LongGuard.asset) → [LongGuardTorso.asset](../Assets/Data/Organs/Torso/LongGuardTorso.asset)。

**重点参数：** `invulnerability=1.5、incomingDamage=1.2`。

**实际生效位置：** Apply 设置 PlayerRuntime.InvulnerabilityMultiplier；伤害倍率经属性系统作用到玩家受伤。

#### 短庇护躯干

**资源链：** [ShortGuard.asset](../Assets/Data/Organs/Torso/ShortGuard.asset) → [ShortGuardTorso.asset](../Assets/Data/Organs/Torso/ShortGuardTorso.asset)。

**重点参数：** `invulnerability=.5、incomingDamage=.6`。

**实际生效位置：** 与长庇护共用相同代码，只是资源参数不同；不需要复制一个短庇护脚本。

## 独立演示脚本

### OrganDemoSetup.cs

[OrganDemoSetup.cs](../Assets/Scripts/Debug/OrganDemoSetup.cs) · MonoBehaviour，OrganDemo 的预设选择

**负责什么：** 按 Preset 安排七种旧器官配置和三个演示目标的位置、血量、节拍；Manual 保留手动设置。

**从哪里读：** Awake → ConfigurePreset → HandOrganDemo。

**什么时候改：** 增加旧器官演示预设时；不要用它改正式玩家开局装备。

### HandOrganDemo.cs

[HandOrganDemo.cs](../Assets/Scripts/Debug/HandOrganDemo.cs) · MonoBehaviour，旧器官演示接线

**负责什么：** 给演示管道配置槽位和目标，转发伤害、状态、范围/黑洞、击杀链，并显示调试信息。

**从哪里读：** TryStartDemo / SetLoadout / ApplyAttack / StopDemo；真实游戏对应 BattleOrganCombat 等适配。

**什么时候改：** 改 OrganDemo 的展示与目标组织时，不要把演示特有规则混入共享核心。

### DemoTarget.cs

[DemoTarget.cs](../Assets/Scripts/Debug/DemoTarget.cs) · MonoBehaviour，简化目标

**负责什么：** 提供独立演示目标身份、生命、受击与死亡通知。

**从哪里读：** InitializeForDemo 与攻击处理；由 HandOrganDemo、预览和检查工具使用。

**什么时候改：** 改测试目标时；正式敌人生命由 C 的 EnemyRuntime 管理。

### DemoCombatTarget.cs

[DemoCombatTarget.cs](../Assets/Scripts/Debug/DemoCombatTarget.cs) · MonoBehaviour，演示状态容器

**负责什么：** 给 DemoTarget 接 StatusEffectRuntime，处理状态持续伤害和演示移动等。

**从哪里读：** TryApplyAttack / Advance / ClearStatuses。

**什么时候改：** 只在独立目标的状态行为或测试展示需要调整时改。

### DemoExplosionView.cs

[DemoExplosionView.cs](../Assets/Scripts/Debug/DemoExplosionView.cs) · MonoBehaviour，演示爆炸显示

**负责什么：** 记录短时爆炸圆环并通过 OnGUI 画出来，不结算伤害。

**从哪里读：** Show / Clear / OnGUI。

**什么时候改：** 改 OrganDemo 的爆炸占位表现；主场景表现改 BattleOrganEffects。

### DemoBlackHoleField.cs

[DemoBlackHoleField.cs](../Assets/Scripts/Debug/DemoBlackHoleField.cs) · MonoBehaviour，演示黑洞适配

**负责什么：** 管理演示中的 BlackHoleBehaviour，推进寿命、拉动演示目标并显示。

**从哪里读：** Spawn / Advance / Clear / OnGUI。

**什么时候改：** 改独立演示黑洞接线；真正共享的吸引算法在 BlackHoleBehaviour。

## 编辑器与检查脚本

这些脚本只用于开发、菜单或验收，不会作为一件器官发动。检查文件名里的 Step/Hand 是开发历史命名，保留因为其逻辑仍被整合回归调用。

- [PatternActuatorConfigEditor.cs](../Assets/Editor/PatternActuatorConfigEditor.cs)：自定义 Inspector，根据 Pattern 显示相关参数。新增参数后要检查它，否则字段存在但界面可能不显示。

- [DirectActuatorConfigEditor.cs](../Assets/Editor/DirectActuatorConfigEditor.cs)：根据 Shape 显示单体/爆炸/黑洞参数；单体与爆炸可选状态文件；显示配置校验错误。

- [OrganTriggerConfigEditor.cs](../Assets/Editor/OrganTriggerConfigEditor.cs)：显示 Trigger Target 范围，并根据 Condition 显示概率、延迟、击杀或静止计数等字段。

- [StraightProjectileSampleMenu.cs](../Assets/Editor/StraightProjectileSampleMenu.cs)：名字沿用首个样板，实际提供全部执行器/触发器/躯干的休息期添加菜单。它不把样板自动加入正式奖励表。

- [OrganConfigurationValidator.cs](../Assets/Editor/OrganConfigurationValidator.cs)：菜单检查配置合法性、重复 ID、默认目录引用和行为歧义；入口 Validate / ValidateMenu。

- [SpringUpBattleSceneSync.cs](../Assets/Editor/SpringUpBattleSceneSync.cs)：跨模块工具，将 C 地图/相机/碰撞等同步到主 Demo；不是器官效果代码。

- [SpringUpDemoBuilder.cs](../Assets/Editor/SpringUpDemoBuilder.cs)：创建统一 Demo 的工具，已有主场景时拒绝覆盖；不是每次运行必点。

- [SpringUpGameView.cs](../Assets/Editor/SpringUpGameView.cs)：开发时的 Game View 显示设置工具。

- [SpringUpBuild.cs](../Assets/Editor/SpringUpBuild.cs)：Windows 主场景构建入口。

- [TemporaryEquipmentChecks.cs](../Assets/Editor/TemporaryEquipmentChecks.cs)：临时装配事务、替换和容量守恒检查。

- [LaboratorySettlementChecks.cs](../Assets/Editor/LaboratorySettlementChecks.cs)：实验室实际伤害、状态、生命周期和连锁结算检查。

- [LaboratoryViewChecks.cs](../Assets/Editor/LaboratoryViewChecks.cs)：实验室引用、卡片和界面内容检查。

- [LaboratoryPlayChecks.cs](../Assets/Editor/LaboratoryPlayChecks.cs)：真实 Play 下的预览更新、按钮、重启与订阅清理检查。

- [LaboratorySceneMenu.cs](../Assets/Editor/LaboratorySceneMenu.cs)：独立实验室的打开菜单和场景路径。

### Assets/Tests/Editor 中每个文件的用途

- [AttackTargetingChecks.cs](../Assets/Tests/Editor/AttackTargetingChecks.cs)：最近目标、范围边界、无效目标等索敌规则。

- [AutomaticAttackChecks.cs](../Assets/Tests/Editor/AutomaticAttackChecks.cs)：三条管道自动循环、无目标后恢复、停用/重置与忽略旧充能消耗。

- [BattleCombatChecks.cs](../Assets/Tests/Editor/BattleCombatChecks.cs)：A 器官与 C 真实敌人的伤害、状态、移动和事件接线。

- [BattleSceneSyncChecks.cs](../Assets/Tests/Editor/BattleSceneSyncChecks.cs)：C 战场同步后的场景接线、墙体、滑动、相机及木桩。

- [EnemyFlashChecks.cs](../Assets/Tests/Editor/EnemyFlashChecks.cs)：实际受伤的闪色与恢复。

- [HandBlackHoleChecks.cs](../Assets/Tests/Editor/HandBlackHoleChecks.cs)：黑洞半径、移动、寿命和替换清理。

- [HandCombinationChecks.cs](../Assets/Tests/Editor/HandCombinationChecks.cs)：旧器官组合次序、状态/范围/触手配合及清理。

- [HandExplosionChecks.cs](../Assets/Tests/Editor/HandExplosionChecks.cs)：TNT 范围、重复目标处理、击杀追加。

- [HandOrganChecks.cs](../Assets/Tests/Editor/HandOrganChecks.cs)：旧七器官定义、独立行为与目标接线。

- [HandStartupPlayChecks.cs](../Assets/Tests/Editor/HandStartupPlayChecks.cs)：演示组件在真实 Play 启动和重新启用时的行为。

- [HandStatusChecks.cs](../Assets/Tests/Editor/HandStatusChecks.cs)：眩晕、流血、逐层减速及计时边界。

- [IntegratedFlowChecks.cs](../Assets/Tests/Editor/IntegratedFlowChecks.cs)：主 Demo 的攻击、休息实验室、下一波、七波、失败和重开；会加速结束波次。

- [IntegratedVisualChecks.cs](../Assets/Tests/Editor/IntegratedVisualChecks.cs)：多分辨率战斗/实验室画面与记录面板截图。

- [OrganCombatReportChecks.cs](../Assets/Tests/Editor/OrganCombatReportChecks.cs)：实际伤害归因、历史容量、槽位反馈、重复绑定；提供完整批量和画面检查入口。

- [OrganConsolidationChecks.cs](../Assets/Tests/Editor/OrganConsolidationChecks.cs)：旧器官资源、状态关联、预设和手动模式的汇总回归。

- [OrganPipelineStep1Checks.cs](../Assets/Tests/Editor/OrganPipelineStep1Checks.cs)：基础槽位、游标、空槽与时间推进。

- [OrganPipelineStep2Checks.cs](../Assets/Tests/Editor/OrganPipelineStep2Checks.cs)：基础攻击数据与演示伤害接线回归。

- [OrganPipelineStep3Checks.cs](../Assets/Tests/Editor/OrganPipelineStep3Checks.cs)：击杀信号、追加执行与链保护。

- [OrganTestFactory.cs](../Assets/Tests/Editor/OrganTestFactory.cs)：测试工厂，创建临时器官/状态配置，避免污染正式 asset。

- [OrganTriggerChecks.cs](../Assets/Tests/Editor/OrganTriggerChecks.cs)：9 种条件、计数、计时、来源、连锁保护及真实事件接线。

- [PatternActuatorChecks.cs](../Assets/Tests/Editor/PatternActuatorChecks.cs)：其他 12 种执行器的行为、全部 13 种预览/实战接线和混合效果清理压力。

- [StraightProjectileChecks.cs](../Assets/Tests/Editor/StraightProjectileChecks.cs)：直线弹首次命中、寿命、取消、来源与墙体/复合碰撞；RunBatch 还串起下游回归。

- [TorsoOrganChecks.cs](../Assets/Tests/Editor/TorsoOrganChecks.cs)：7 种躯干、倍率叠加/卸下、前方判断、移动、生命和相机等接线。

- [UniversalEquipmentChecks.cs](../Assets/Tests/Editor/UniversalEquipmentChecks.cs)：普通器官跨部位、实例唯一性、库存/管道同步及阶段权限。

D 侧 `Assets/Scripts/Systems/Editor/ProgramDTests.cs` 与 `BattleRunLinkTests.cs` 分别验证库存流程和独立灰盒桥接。完整检查运行方式见 [工程配置操作单](../工程配置操作单.md)，本页只说明职责，不把每个 Test 都当作运行组件。

## 沿一次直线子弹追代码

假设休息期把一件直线子弹装到头部第 2 槽，并开始下一波：

1. **库存与装配**：RunController 记录该 InstanceId 在 head/2。RunLaboratoryEquipment 刷新 UI；PipelineLoadout 转为 Head、下标 1，建立对应 OrganInstance。
2. **读配置**：OrganCatalog 收录 StraightProjectile.asset；其 actuator 指向 StraightProjectileBehaviour.asset；StraightProjectileConfig 生成 OrganDefinition 的 Projectile 参数快照。
3. **等待节拍**：SpringUpRunDriver 给管道设置玩家的步进和周期，开波 ResetTiming。OrganPipelineController.Update 推进 Head 的 BodyRuntime，轮到有器官的槽位。
4. **选目标与发射**：BattleOrganTargeting 提供最近有效敌人的身份/位置，管道 ExecuteAttack 建立 ActuatorContext 并发出 ActuatorProduced。
5. **登记执行**：BattleOrganCombat.ExecuteActuator 调 ActuatorRunner.Start，统一工厂选择 StraightProjectileExecution。Runner 发布 Started，可供前级计时触发器监听。
6. **飞行和碰撞**：BattleOrganCombat 的 FixedUpdate 推进 Runner；弹体 Tick 计算下一位置，BattleActuatorWorld.Sweep 检查这段路首先碰到谁。
7. **伤害与事件**：命中敌人则 World 转入 ResolveDamage / EnemyRuntime.TakeAttack。实际损失生命用于统计和吸血；命中、暴击、击杀、状态等事件根据本次真实结果产生。
8. **结束与显示**：弹体结束后 Runner 发布 Ended；失效触发器可响应正常结束。BattleOrganEffects 更新/回收外观，OrganCombatReport 和 RunEquipmentHud 显示发动/伤害信息。

休息、死亡、重开会停止实战管道并 Clear 持续效果。取消不等于自然结束；不能为了显示一个特效就重新结算一次伤害。

### 工厂代码实际长什么样

下面直接摘自当前 EffectExecutions.cs。看这段就能知道没有 Laser.cs 的原因：普通激光与大激光都交给 BeamExecution。

```csharp
    public static class EffectExecutionFactory
    {
        public static IActuatorExecution Create(ActuatorContext context, IActuatorWorld world)
        {
            if (!(context.Projectile is PatternActuatorSettings settings)) return new StraightProjectileExecution(context, world);
            var effects = world as IEffectWorld ?? throw new ArgumentException("世界未提供执行器效果接口。");
            switch (settings.Pattern)
            {
                case ActuatorPattern.Triple: return new TripleProjectileExecution(context, effects);
                case ActuatorPattern.Laser: case ActuatorPattern.ChargedLaser: return new BeamExecution(context, effects);
                case ActuatorPattern.Fog: return new FogExecution(context, effects);
                case ActuatorPattern.Sucker: return new SuckerExecution(context, effects);
                case ActuatorPattern.Summon: return new SummonExecution(context, effects);
                default: return new MovingEffectExecution(context, effects);
            }
        }
    }
```

## 常见修改应该动哪里

**只想提高导弹爆炸伤害**：选 MissileBehaviour.asset 改 Explosion Damage。不用改 MovingEffectExecution，也不用复制 Missile.cs。

**想让追踪弹丢失目标后重新索敌**：改 MovingEffectExecution 的 Homing 分支，并决定新目标选择范围/规则；同时检查实战与预览的 Targets 接口，补失去目标后的行为测试。

**想让毒雾跟着角色移动**：这是规则变更，改 FogExecution 的位置更新；只调 speed 不能生效，因为现有毒雾不使用它移动。

**想让大激光连续伤害**：改 BeamExecution 的单次 fired 结算规则，新增伤害间隔配置并让 PatternActuatorConfigEditor 显示；保留复合碰撞去重和结束清理。只改 lifetime 不会自动变成连续激光。

**想让触发器只发动后面一个器官**：改 OrganTriggerRuntime.ActivateConfigured 中扫描后续槽位的规则；需要同时考虑 SelfSlots 和 FollowingSlots 两种范围，会影响全部 10 份触发配置，若只想改其中一种需显式区分条件/配置。

**想改钢管眩晕概率**：选 Stun.asset 的 Chance Percent；想改“再次眩晕如何刷新剩余时间”才改 StatusEffectRuntime。数据单位分别是 Inspector 百分数和运行时 0～1。

**想让召唤物主动攻击**：需要补 SummonExecution 或单独召唤攻击逻辑、目标选择、攻击节拍与伤害事件；要被敌人攻击还需 C 的目标系统接入。当前只有跟随、生命和自然扣血，调高 Damage 不会补出这些功能。

**想增加一个“毒雾伤害更高”的器官品种**：可复用 Fog Pattern，新建自己的行为参数和外层 OrganConfig，设置唯一 ID，并接 OrganCatalog 与库存元数据。若需要全新轨迹，则要增加 Pattern/参数、工厂分支、执行类、Inspector 显示和对应测试。

**想改所有攻击的速度**：区分“出手频率”和“弹体飞行速度”。前者调 PlayerStatsBase 的 Base Cooldown / Base Cycle Duration；后者调相应 Behaviour.asset 的 Speed。躯干 Frequency 会影响节拍。

## 查阅时容易混淆的名字

- `SpringUp.Organs.OrganDefinition / OrganInstance` 是战斗模型；`Tideflesh.Systems` 中的同名类型是库存数据。看清 namespace，适配层负责关联，不要随意合并。
- HandPipelineLoadout、StraightProjectileActuator、StraightProjectileSampleMenu 都沿用了早期名称；当前用途比名字更广。
- `OrganTriggerRuntime.cs` 与 `OrganPipelineController.cs` 属于同一个 partial 类，不会在 Inspector 出现两个组件。
- 场景里有 MonoBehaviour，不代表它的纯 C# helper 也要手动 Add Component。配置通过资源引用，运行对象由代码创建。
- 删除过的 PlayerChargeAttack、ChargeAttackHud、OrganPipelineDemo 不再是当前入口；旧历史记录出现这些名称时不要重新创建。

本文是当前实现的源码导航。具体平衡参数见 [调参手册](开发改动总览与调参手册.md)；规则仍待确认或尚未实现的项目见 [试玩验收清单](待确认与试玩验收清单.md)。本页已同步 2026-10-17 的资源整理；迁移与持续效果详解见 [器官资源整理与持续效果说明](器官资源整理与持续效果说明.md)。
