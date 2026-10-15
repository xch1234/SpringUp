# 潮涌之躯（SpringUp）

海洋克苏鲁题材的 Unity 2D URP 器官组合游戏。目标循环是战斗 60 秒 → 获得器官 → 实验室装配 → 下一波，共 7 波，第 7 波为 Boss。现行设计为头、手、腿三条管道，每条 6 槽，另有躯干特殊器官；目标器官池为 43 个。

当前正式入口为 **Assets/Scenes/Flow/Bootstrap.unity**，使用 Unity **6000.5.11f1** 与新 Input System。

## 游玩入口与操作

- Bootstrap 加载主菜单，再进入战斗和休整；共七波，第七波仍为计时通关，Boss 尚未完成。
- WASD / 方向键移动，Shift 疾跑。Esc 菜单仅有继续、设置和返回主菜单；放弃本局需要确认。
- B 或 Tab 切换背包，Esc 关闭后直接返回游戏。战斗背包只读，休整允许安装、替换、拆卸、丢弃。打开背包暂停世界；其他模态界面打开时不可叠加背包。
- 休整地图青绿色占位商人为商店入口，靠近两单位内显示 E：商店。购买沿用测试锈刀、骨髓与容量规则；只出售未装备物品，选择后显示售价（商店价整除二），确认出售。装备需先在背包拆卸，背包不再出售。
- 默认 F 攻击，点按启动一轮，长按在整轮完成、最后出手间隔结束、充能足够且目标有效后连续发动。松开仍完成当前轮。间隔复用角色/器官 StepInterval（含最小间隔），快速点按不能绕过。
- 每轮预扣全部攻击器官费用，逐槽位重新选目标；失去目标或中止时退还未执行槽位的费用。每波充满，Bootstrap 战斗及休整均可恢复充能并攻击（休整木桩不发奖励）。
- 设置支持普通键盘键、鼠标左键、鼠标右键，并可恢复 F。点击更改后先释放启动输入，再选择；Esc 只取消捕获。移动、疾跑、修饰键和界面键（包括 B/Tab/E）保留。旧键盘配置兼容，不合法或缺失鼠标时恢复 F 并提示。
- 模态界面、死亡、切场景会取消未执行攻击；恢复、跨波次、改键及失焦后须先释放攻击键。点击可交互 UI 后持续按住也不会转成攻击，纯展示 HUD 不屏蔽攻击。

窗口继续使用窗口栈和 PauseService 令牌，多层关闭不会提前恢复世界。休整商人已保存在 Rest 场景，FlowContent 提供缺失时的创建逻辑，重建场景同样生效；正式 NPC 可调用 FlowApplication.TryOpenShop 复用阶段、加载、模态、距离校验。

`SpringUpDemo.unity` 保留旧整合演示入口，共用主动攻击组件；`LaboratoryDemo.unity` 与 `OrganDemo.unity` 保留独立自动预览；`Battle_Greybox.unity` 为开发灰盒。旧演示 UI 不代表 Bootstrap 的正式操作路径。

战斗数值在 `Assets/Data/Organs/Hand/` 与 `Assets/Data/Statuses/`，库存元数据、价格和波次在 D 的 JSON。现有器官内容和未完成事项见 [ABCD整合记录](ABCD整合记录.md)。

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

本次 Bootstrap 输入、背包与商店改动的验证结果见 [验证记录](BootstrapInputShopValidation.md)。
