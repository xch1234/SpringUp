# 潮涌之躯（SpringUp）

> **这份文档的用途**：给人和 AI 一份「去哪儿找信息」的索引。
> 不重复设计内容——玩法规则、接口字段、数值一律以被指向的文档为准。
>
> 仓库：<https://github.com/xch1234/SpringUp>

---

## 一、项目一句话

海洋克苏鲁题材的 2D 类幸存者：打一波怪（60 秒）→ 回实验室把敌人器官装到自己身上 → 打下一波，共 7 波。
**重心在「组装」不在「割草」**——战斗是给构筑做验证的。

| 项 | 值 |
| --- | --- |
| 引擎 | Unity **6000.5.11f1**，2D (URP) |
| 输入 | **只启用新 Input System**（`ProjectSettings.asset` 的 `activeInputHandler: 1`）。旧的 `Input.GetAxis` / `Input.GetKey` 运行时会抛异常，**不要用** |
| 规模 | 1 张地图、7 波 + 1 Boss、**43 个器官**（走槽 39 + 躯干 4）、1 个角色 |
| 团队 | 4 程序 + 2 美术，策划兼设计支撑 |

---

## 二、读文档的顺序（**先读这里，别直接翻代码**）

| 顺序 | 文档 | 里面是什么 | 谁该读 |
| --- | --- | --- | --- |
| 1 | [策划案.md](策划案.md) | **设计事实来源**。核心循环、器官结算模型、触发条件、经济、题材、边界与异常规则 | 所有人 |
| 2 | [接口草案.md](接口草案.md) | **模块间的契约**：§1 `Event` 结构、§2 器官配置格式、§3 状态格式、§4 目录规范、**§5 玩家属性接口（已冻结）**、**§6 敌人接口（已冻结）** | 程序 A / C / D |
| 3 | [任务拆解.md](任务拆解.md) | 边界与所有权、验收标准、P0 五项接口、里程碑 | 所有人 |
| 4 | [器官模块-代码架构.md](器官模块-代码架构.md) | 器官系统的**代码架构提案**，待程序 A 认可后才成为实现约定 | 程序 A |

> ⚠️ **`任务拆解.md` 里有旧方案**（4 部位 / 4 类器官 / 40 个器官）。
> 现行方案是 **3 部位（头/手/腿）× 6 槽 = 18 槽 + 躯干特殊器官、总池 43**。
> 两处冲突时**以 `策划案.md` 为准**，`任务拆解.md` 待同步。

---

## 三、模块地图（做什么、代码在哪儿、契约在哪份文档）

现行代码统一计划放在 `Assets/Scripts/Battle/`（见第五节「目录约定冲突」）。

| 角色 | 做什么 | 代码位置 | 契约文档 |
| --- | --- | --- | --- |
| **程序 A** 战斗内核 | `Event` 管道执行器、触发器 / 执行器骨架、伤害与状态结算、触发链与防环 | `Assets/Scripts/` 下（**尚未开工**） | 接口草案 §1 §2 §3；策划案「结算模型」 |
| **程序 B** 实验室与战斗 UI | 18 槽装配界面、器官说明卡、**链路预览**、**战斗内触发可视化** | `Assets/Scripts/UI/` | 接口草案 §5（读属性做预览） |
| **程序 C** 玩家 / 敌人 / 战场 | 玩家移动、镜头、敌人数值与 AI、刷怪、波次计时、灰盒关卡 | **`Assets/Scripts/Battle/` ＋ `Assets/Scripts/Editor/`（已实现，见第四节）** | **接口草案 §5 §6（已冻结）** |
| **程序 D** 数据 / 流程 / 经济 | 器官数据读取与校验、掉落、背包（上限 5）、商店、货币、躯干升级树、7 波流程状态机、打包 | `Assets/Scripts/Systems/`、`Assets/Data/` | 接口草案 §2 |
| **美术 A** 角色与器官 | 玩家头/手/腿附着点、**43 个器官图标与外观**（必须走模板：图标 = 部位色 × 类型形）、3–4 种普通敌人、1 个 Boss | `Assets/Art/Characters/`、`Assets/Art/Enemies/` | 策划案「表现模板」 |
| **美术 B** 场景与界面 | 1 张深海地图、UI 美术、特效模板（**按载荷类型复用，不做一对一**）、宣传物料 | `Assets/Art/Map/`、`Assets/Art/UI/`、`Assets/Art/VFX/` | 策划案「表现模板」 |
| **策划 / 设计支撑** | 43 器官数值表、掉落加权、波次配置、经济曲线、首次体验引导 | `Assets/Data/`（数据）＋ 根目录文档 | 策划案「待定问题」 |

**边界纪律**（`任务拆解.md` §七）：一个模块只有一个负责人；改别人模块的代码先打招呼；接口冻结后要改必须公告 + 通知依赖方。

---

## 四、程序 C 现状（该模块已产出可运行灰盒）

> **重要**：这部分代码目前**只在本地工作区，尚未推送到本仓库**。
> 下列路径在推送后才会出现在 GitHub 上。

| 文件 | 职责 |
| --- | --- |
| `Assets/Scripts/Battle/PlayerStatsBase.cs` | 属性表：接口草案 §5.2 十四字段 |
| `Assets/Scripts/Battle/PlayerRuntime.cs` | 运行时状态：§5.3 `currentHealth` / `partRate`，并结算玩家受伤 |
| `Assets/Scripts/Battle/PlayerMotor.cs` | WASD 移动 + 疾跑（读新 Input System） |
| `Assets/Scripts/Battle/CameraFollow.cs` | 俯视跟随相机（硬跟随，未用 Cinemachine） |
| `Assets/Scripts/Battle/EnemyEvents.cs` | 敌人四条对外信号：§6.1 `OnDamaged` / `OnKilled` / `OnStatusApplied` / `OnPlayerHurt` |
| `Assets/Scripts/Battle/EnemyData.cs` | 敌人数据：§6.2 十二字段 |
| `Assets/Scripts/Battle/EnemyArchetypeConfig.cs` | **六种敌人的数值与行为配置集中在此表**（改数值改这里） |
| `Assets/Scripts/Editor/BattleGreyboxBuilder.cs` | 用代码生成灰盒场景（菜单：`Tools → 程序C`） |
| `Assets/Scripts/Editor/EnemyPrefabBuilder.cs` | 按配置表生成敌人 prefab（菜单：`Tools → 程序C`） |
| `Assets/Scripts/Battle/`（其余） | 对象池、弹体、刷怪、波次计时、医疗兵 / 兵工厂等行为组件、灰盒诊断 HUD |

**测试入口**：打开 `Assets/Scenes/Battle_Greybox.unity` 进播放模式。WASD 移动，`F` 键对周围敌人造成伤害（开发工具，正式版不编译进去）。

### 六种敌人（程序 C）

| 类型 | 行为 | 关键数值（基准：移速 2.5 / 血 20 / 攻 5） |
| --- | --- | --- |
| `Normal` | 追击 + 接触伤害 | 基准值 |
| `Fast` | 同上，更快更脆 | 移速 ×1.3、血 ×0.7 |
| `RangedKeeper` | **保持距离** + 射击，**不**接触伤害 | 血 ×1.0 |
| `RangedChaser` | 追击 + 接触伤害 + 射击 | 血 ×1.2 |
| `Arsenal` | 静止 + 持续产生 `Normal` | 血 ×2.0，每 3 秒产 1 个 |
| `Medic` | **保持距离** + 间歇治疗周围敌人 | 血 ×0.8，3 秒 / 5 米 / 回 20% |

> 数值是灰盒占位，**以 `EnemyArchetypeConfig.cs` 为唯一来源**，不要改 prefab 里的副本。

---

## 五、目录约定冲突（**开始协作前必须解决**）

本仓库的 `Assets/Scripts/` 建了六个空目录，但程序 C 的代码放在了 `Assets/Scripts/Battle/`：

| 本仓库现有（空） | 程序 C 实际使用 |
| --- | --- |
| `Assets/Scripts/Core/` | — |
| `Assets/Scripts/Enemies/` | `Assets/Scripts/Battle/` |
| `Assets/Scripts/Orangs/`（**注意拼写**） | `Assets/Scripts/Battle/` |
| `Assets/Scripts/Player/` | `Assets/Scripts/Battle/` |
| `Assets/Scripts/Systems/` | — |
| `Assets/Scripts/UI/` | — |

**两条路选一条并写进 `接口草案.md` §4：**

1. **沿用现有六个目录**——把程序 C 的代码拆进 `Player/` `Enemies/`；需要一次搬迁 + 改 `.meta`。
2. **改用 `Battle/` 单目录**——删掉那六个空目录（或留给对应模块）；好处是程序 C 的边界与 `任务拆解.md` 的模块划分一一对应。

**顺带修正**：`Orangs` 应为 `Organs`（器官）。拼写错误一旦被代码引用就很难改。

---

## 六、仓库与协作约定

- **分支**：主分支 `main`；功能分支按模块命名（例如 `battle/greybox`）。
- **提交粒度**：场景、Prefab、脚本、数据分小批提交。
- **`.meta` 必须和资源一起提交**，不要单独删除或忽略。
- **不要提交** `Library/` `Temp/` `Logs/` `UserSettings/` `obj/`（`.gitignore` 已配）。
- **行尾**：`.gitattributes` 已把 Unity 的 YAML 资产统一为 LF，避免整文件假 diff。

---

## 七、文档同步提醒（未决项）

以下差异需要策划与程序 A 确认后写回文档，**不要按旧条目开工**：

- `任务拆解.md` 仍是旧方案（4 部位 / 4 类器官 / 40 器官 / 旧分工）。
- `接口草案.md` §1 里 **`radius` 的「(类型, 距离)」格式**与 **`pierce` 的数值含义**未定——
  这两条不定，**程序 A 无法开工**（要构造 `Event`）。
- `接口草案.md` §3 的**状态配置表是空的**（叠加一/二/三层的影响未填）。
- `策划案.md` 待定问题 **2 已定**（60 秒到点清除场上敌人并结算；**是「清除」不是「消灭」、不产生掉落**），
  但文档里尚未划掉。
- `策划案.md` 待定问题 **7 体力系统留不留**未定 → 阻塞「冲刺 / 体力」实现。
- 器官池表标注「**本表待重排**」——43 池的逐格分配未产出。
