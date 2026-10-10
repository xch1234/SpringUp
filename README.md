# 潮涌之躯（SpringUp）

海洋克苏鲁题材的 Unity 2D URP 器官组合游戏。目标循环是战斗 60 秒 → 获得器官 → 实验室装配 → 下一波，共 7 波，第 7 波为 Boss。现行设计为头、手、腿三条管道，每条 6 槽，另有躯干特殊器官；目标器官池为 43 个。

本文是合并后的信息索引，更新于 2026-10-10。当前集成了程序 A、B、C 的阶段成果，三个演示入口均保留，完整 M1 闭环尚待联调。涉及战场规则和目录的冲突，以程序 C 较新的 battle/greybox 实现与工作记录为准；旧 module-map 中“A 尚未开工”“C 尚未推送”等描述已失效。

## 打开工程与三个入口

使用 **Unity 6000.5.11f1**，等待导入和编译完成。工程只启用新 Input System；不要使用旧的 `Input.GetAxis` / `Input.GetKey`。

| 模块 | 场景 | 操作与当前范围 |
| --- | --- | --- |
| A：器官演示 | `Assets/Scenes/OrganDemo.unity` | 选择 OrganDemo 物体，在 Organ Demo Setup 的 Preset 中选模式后 Play；演示七种手部器官、状态、TNT、黑洞与击杀触发 |
| B：实验室 | `Assets/Scenes/LaboratoryDemo.unity` | 左侧安装、替换、拆下；右侧真实扣血和击杀追加演示，目前只支持拳头与多肢触手 |
| C：战场灰盒 | `Assets/Scenes/Battle_Greybox.unity` | WASD / 方向键移动，Shift 疾跑；编辑器中可用 F 对周围敌人造成开发测试伤害 |

当前 Build Settings 仍以 OrganDemo 为入口，不代表正式游戏流程。A 的测试敌人、B 的实验室演示与 C 的正式战场接口尚未完整接通。

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
| 程序 B | 实验室装卸替换、实例与物品守恒、拳头/触手战斗演示；正式库存、出售、链路查询及下一波待接 | `Assets/Scripts/UI/Laboratory/` |
| 程序 C | 玩家属性/移动、镜头、敌人 AI、弹体、对象池、刷怪、波次与灰盒 | `Assets/Scripts/Battle/`；工具在 `Assets/Scripts/Editor/` |
| 程序 D | 正式数据读取、掉落、库存、经济、流程与打包，待接入 | 预留 `Assets/Scripts/Systems/`、`Assets/Data/` |
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
- `WaveDirector` 提供波次开始/结束接口。当前 `intervalBetweenWaves = 0` 时会等待外部开启下一波；实验室尚未连接这一流程。
- `EnemyRuntime.ApplyStatus` 当前只发状态通知，实际状态计时、流血、减速/眩晕仍需接 A。黑洞需接 C 的位移与物理系统，不能只搬演示目标的移动代码。
- 玩家受伤触发应消费 `PlayerRuntime.PlayerDamaged` 的实际结算结果；`EnemyEvents.PlayerHurt` 是敌人发出的原始伤害请求，不能重复扣血。
- 疾跑已实现；冲刺与体力仍需策划明确，不能把字段存在视为完整机制已完成。

## 合并验证与已知缺口

本地合并基线：A `12af7ea`、B `71a4905`、battle/greybox `ba60c97`、docs/module-map `a9d841f`。

合并前已在相同源码的隔离快照中使用 Unity 6000.5.11f1、本地 uGUI/Input System 与所需内建模块检查：脚本编译成功，A 前 47 组检查通过，B 的 LaboratoryViewChecks（含装备事务检查）通过。这不等于完整包环境、URP 画面、真实 Play 闭环或正式打包通过。

A 完整检查入口为 `Tools > SpringUp > Run All Organ Checks`，单模块原有 51 组。**合并后 `OrganConsolidationChecks.CheckAssets` 仍要求 Scenes 根目录只有一个场景，会因 B/C 场景存在而失败**；应缩小该断言的模块范围，不能删掉 B/C 场景来通过检查。其余新增整合组尚未在此次合并快照中验证。

后续联调优先级：

1. A+C 打通器官攻击、真实敌人、原攻击上下文与击杀通知；范围命中和延迟流血保持同链防重复。
2. 接状态、黑洞外力与池化清理，统一属性/部位节拍；C 的 partRate 目前默认 0，接线前需明确初始化及含义。
3. A+B+D 统一配置查询和库存实例映射，扩展 B 当前仅两种器官的单目标演示接法。
4. D 串起战斗结束 → 奖励/库存 → 实验室 → 出战校验 → 下一波；B 补出售、链路预览及携带限制。
5. 补 Event → Event 修改机制、正式构建入口与联合回归，完成首波闭环验收。

C 的 `Assets/Art/Textures/white_16.png` 在原提交中是普通 PNG，但仓库规则要求 PNG 走 LFS，Git 会提示“应为 pointer”。这是继承的资源规范问题，需后续统一 LFS 或明确例外。

## 协作约定

接口变更先通知依赖方并更新接口文档；每个模块明确负责人。场景、Prefab、脚本和数据分小批提交，资源与 `.meta` 一起保留，移动资源时不要重新生成 GUID。

`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`obj/` 和 `.utmp/` 是缓存或本机数据，不提交。当前 `main_temp` 是本地集成分支，合并成果不代表正式主分支已发布。
