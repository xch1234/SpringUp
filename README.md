# 潮涌之躯（SpringUp）

海洋克苏鲁题材的 Unity 2D URP 器官组合游戏。目标循环是战斗 60 秒 → 获得器官 → 实验室装配 → 下一波，共 7 波，第 7 波为 Boss。现行设计为头、手、腿三条管道，每条 6 槽，另有躯干特殊器官；目标器官池为 43 个。

本文是合并后的信息索引，更新于 2026-10-12。ABCD 已接通器官真实扣血、波次结算、实验室装配和下一波的初步闭环，原有三个演示入口保留。当前第六波以 TNT 临时代替未实现的蜘蛛巢奖励，第七波为计时通关，不代表 Boss 已实现。实施与验收见 [ABCD整合记录](ABCD整合记录.md)。

## 打开工程与入口

使用 **Unity 6000.5.11f1**，等待导入和编译完成。工程只启用新 Input System；不要使用旧的 `Input.GetAxis` / `Input.GetKey`。

| 模块 | 场景 | 操作与当前范围 |
| --- | --- | --- |
| ABCD：统一流程 | `Assets/Scenes/SpringUpDemo.unity` | 默认入口；移动靠近敌人后自动发动器官；每波 60 秒，结算后装卸、替换、出售、丢弃，再点击下一波；支持失败、七波通关和重开 |
| A：器官演示 | `Assets/Scenes/OrganDemo.unity` | 选择 OrganDemo 物体，在 Organ Demo Setup 的 Preset 中选模式后 Play；演示七种手部器官、状态、TNT、黑洞与击杀触发 |
| B：实验室 | `Assets/Scenes/LaboratoryDemo.unity` | 左侧安装、替换、拆下；右侧读取七种真实手部配置，对两个固定目标预览伤害、状态、黑洞寿命和击杀追加；死亡延迟补充 |
| C：战场灰盒 | `Assets/Scenes/Battle_Greybox.unity` | WASD / 方向键移动，Shift 疾跑；编辑器中可用 F 对周围敌人造成开发测试伤害 |

Build Settings 首个启用场景为 SpringUpDemo。战斗与实验室预览共用 A 的器官/状态资源；D 的 JSON 负责库存元数据、价格与波次，加载后战斗字段由 A 的定义覆盖。修改战斗数值请改 `Assets/Data/Organs/Hand/` 与 `Assets/Data/Statuses/`。完整 Event → Event、Boss、蜘蛛巢、商店及躯干升级仍未实现。

统一游玩界面左上角显示当前装备：头、手、腿各 6 槽，躯干 2 槽，包含槽位编号、器官名称和空槽。战斗期间只读；每波结束后的休息期在实验室安装、替换或拆卸，下一波自动显示更新后的装备。

角色采用自动攻击：在 3 个世界单位内选择最近的存活敌人，按已装备器官的槽位顺序发动；默认每轮 2 秒，同轮相邻已装备槽位间隔 0.15 秒，无目标则跳过，不需要按攻击键。敌人实际受伤时闪白约 0.16 秒并恢复原色；受伤闪色优先于敌人开火时的闪黄。

## 文档阅读顺序

| 文档 | 用途 |
| --- | --- |
| [策划案](策划案.md) | 玩法与内容设计依据；部分待定项尚需同步 |
| [接口草案](接口草案.md) | 玩家与敌人接口、器官接入约定；后续章节记录 A 当前实现子集 |
| [程序 A 器官系统使用说明](程序A-器官系统使用说明.md) | 演示预设、配置修改、检查入口及 A 的未完成事项 |
| [程序 B 实验室展示框架](程序B-实验室展示框架.md) / [交付清单](程序B-交付清单.md) | 装配操作、样例数据、战斗演示与验收边界 |
| [程序 C 验证清单](程序C-验证清单.md) / [缺口盘点](程序C-缺口盘点.md) | 战场验收与接口消费方；缺口盘点中的 A 进度是当时快照，应结合 A 最新说明阅读 |
| [工程配置操作单](工程配置操作单.md) | C 的工程设置与操作记录 |
| [器官模块代码架构](器官模块-代码架构.md) | 架构提案，不等于全部已实现 |
| [程序 A 历史记录](Docs/Archive/程序A-历史开发记录.md) | 旧开发记录与计划，仅供追溯，旧路径和默认数值不是当前使用方法 |

旧索引引用的 `任务拆解.md` 未随这些分支提交。工作区外层的 `下一阶段任务分工.md` 也尚未纳入仓库，不能假定新克隆包含这两份文件。旧版“四部位、四类器官、40 个器官”的方案不作为当前开工依据。

## 模块地图与实际目录

| 负责人 | 职责与当前状态 | 代码位置 |
| --- | --- | --- |
| 程序 A | 三条六槽管道、七种手部独立效果、状态、范围攻击、黑洞、击杀链与防环；完整 Event → Event 尚未实现 | `Assets/Scripts/Core/`、`Organs/`、`Debug/` |
| 程序 B | 实验室装卸替换、正式库存、出售丢弃、容量门槛与下一波；七种共享配置的结算预览 | `Assets/Scripts/UI/Laboratory/` |
| 程序 C | 玩家属性/移动、镜头、敌人 AI、弹体、对象池、刷怪、波次与灰盒 | `Assets/Scripts/Battle/`；工具在 `Assets/Scripts/Editor/` |
| 程序 D | 正式数据读取、掉落、库存、经济、统一波次流程与 Windows 构建已接入 | `Assets/Scripts/Systems/`、`Assets/Data/` |
| 美术 A | 角色、器官图标与附着表现、敌人/Boss | 按角色与敌人资源分类协作 |
| 美术 B | 深海地图、界面、可复用特效与反馈 | 与 B/C 对齐场景和资源规格 |
| 策划 / 设计支撑 | 器官判据、数据、掉落/经济、教学与规则确认 | 策划案及配置资料 |

保留 C 的 Battle 目录，不按旧目录草案拆入 Player / Enemies；也不把 A/B 全部搬进 Battle。`Orangs` 已由 A 整理为 `Organs`，不要恢复旧拼写。接口草案中的早期目录提案仍需同步。

- `Assets/Data/Organs/Hand/`：唯一一套七种手部器官配置。
- `Assets/Data/Statuses/`：独立眩晕、流血、减速配置。
- `Assets/Scenes/Tests/`：A 归档的七个旧场景。
- `Assets/Resources/Enemies/`：C 的敌人 Prefab；数值与行为配置以 `EnemyArchetypeConfig.cs` 为来源。
- `Assets/Tests/Editor/`：A 的检查；`Assets/Editor/`：B 的检查与场景打开工具。

## 程序 C 的战场约定

- 玩家属性和运行时由 `PlayerStatsBase` / `PlayerRuntime` 管理；移动读取新 Input System，镜头由 `CameraFollow` 管理。
- 敌人由 `EnemyData` / `EnemyRuntime` 管理，信号集中在 `EnemyEvents`。
- 六种敌人包括 Normal、Fast、RangedKeeper、RangedChaser、Arsenal、Medic；配置表在 `Assets/Scripts/Battle/EnemyArchetypeConfig.cs`，不要只修改 Prefab 中的数值副本。
- **60 秒到点清除残余敌人，不视为击杀，不发击杀信号、不产生清场掉落。** 此项按 C 已落实规则处理，不再作为未定波次结束条件。
- `WaveDirector` 提供波次开始/结束接口；统一场景由 `SpringUpRunDriver` 控制开波，实验室出战校验成功后才开始下一波。
- `BattleOrganCombat` 接 A 的攻击与状态计时，调用 C 的真实扣血和移动倍率；黑洞使用真实敌人的 Rigidbody2D/位置，并尊重 CanBeDisplaced。
- 玩家受伤触发应消费 `PlayerRuntime.PlayerDamaged` 的实际结算结果；`EnemyEvents.PlayerHurt` 是敌人发出的原始伤害请求，不能重复扣血。
- 疾跑已实现；冲刺与体力仍需策划明确，不能把字段存在视为完整机制已完成。

## 合并验证与已知缺口

本地合并基线：A `12af7ea`、B `71a4905`、battle/greybox `ba60c97`、docs/module-map `a9d841f`。

合并前已在相同源码的隔离快照中使用 Unity 6000.5.11f1、本地 uGUI/Input System 与所需内建模块检查：脚本编译成功，A 前 47 组检查通过，B 的 LaboratoryViewChecks（含装备事务检查）通过。这不等于完整包环境、URP 画面、真实 Play 闭环或正式打包通过。

本轮 B 接入后，在 Unity 6000.5.11f1 隔离快照中，A 完整 51 组已通过。OrganConsolidationChecks.CheckAssets 的场景断言已缩小为 A 的 OrganDemo 主场景，保留 B/C 场景。B 的新结算、UI 与 Play 验证及边界见[交付清单](程序B-交付清单.md)。隔离依赖验证不等同于完整项目打包。

上述为历史合并验证；2026-10-12 已完成完整依赖 Play 验证、真实战斗检查、D 的 19 项测试、三分辨率画面与 Windows 构建，详见整合记录。后续重点：

1. 人工完整七波生存试玩与数值平衡；自动加速验收不替代平衡试玩。
2. 实现蜘蛛巢与 Boss，取消第六波 TNT 替代和第七波计时胜利。
3. 补完整 Event → Event、其他器官、商店、躯干升级及正式美术反馈。

C 的 `Assets/Art/Textures/white_16.png` 在原提交中是普通 PNG，但仓库规则要求 PNG 走 LFS，Git 会提示“应为 pointer”。这是继承的资源规范问题，需后续统一 LFS 或明确例外。

## 协作约定

接口变更先通知依赖方并更新接口文档；每个模块明确负责人。场景、Prefab、脚本和数据分小批提交，资源与 `.meta` 一起保留，移动资源时不要重新生成 GUID。

`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`obj/` 和 `.utmp/` 是缓存或本机数据，不提交；`Builds/` 为本机构建产物。本轮整合在 `main` 汇总交付，当前可玩版本及未完成内容见整合记录。
