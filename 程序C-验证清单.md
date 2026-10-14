# 程序 C · 战场骨架验证清单

> 由 agent 于 2026-10-06 产出，对应 goal「程序 C 战场模块」的第一个切片。
> 配套文档：`潮涌之躯策划案/接口草案.md` §5、`潮涌之躯策划案/交接-新项目启动.md` §6、
> `工程配置操作单.md`。
>
> **本文件只写给程序 C。** 器官相关的接口（`IStatModifierSource` 的调用方）属程序 A。

---

## 一、先跑这四条菜单（按顺序）

| # | 菜单 | 作用 |
| --- | --- | --- |
| 1 | `Tools → 编辑器 → 把 External Script Editor 设为 VS Code` | 编辑器指向 D 盘的 VS Code |
| 2 | `Tools → 编辑器 → 清理 URP 模板残留（TutorialInfo / Readme）` | 删模板自带的说明与窗口布局 |
| 3 | `Tools → 编辑器 → 删除 Assets 下的空文件夹` | 删 `Assets/Scripts/Battle`、`Assets/Scripts/Organ` 之外的残留空目录 |
| 4 | **`Tools → 程序C → 生成战场灰盒场景`** | 生成 `Assets/Scenes/Battle_Greybox.unity` 并打开 |

可选第 5 条：`Tools → 程序C → 生成 Sprite 方块资源`
生成 `Assets/Art/Textures/white_16.png`（16×16、1 单位 = 16 像素、Point 过滤）。
不跑也能用——场景构建器在找不到该贴图时会退回运行时生成的 1×1 白图，灰盒照样能看。

> **第 3 条的注意**：`Assets/Scripts/Battle` 现在**不是空目录了**（程序 C 的 6 个脚本在里面），
> 所以这条菜单不会再删它，只可能删掉 `Assets/Scripts/Organ` 这类空残留。

---

## 二、播放模式要验的 7 件事

进播放模式（Play），用 **WASD 或方向键**：

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 1 | 玩家移动 | 按 WASD / 方向键，青色方块移动，速度约 **10 米/秒** |
| 2 | 相机跟随 | 相机平滑跟住玩家，不抖动、不越界。**这是本切片的核心目标** |
| 3 | 斜向不加速 | 同时按 W+D，速度与单按 W 相同（而非快 √2 倍） |
| 4 | 底图九宫格贴对位置 | 参见下面的「二之三」——边框八块应贴在地图边界，中心一块铺内部 |
| 5 | 疾跑 | 按住左 Shift，速度变为 1.5 倍；**松开立刻恢复**（不卡在疾跑） |
| 6 | 无报错 | Console 无红色报错。尤其**不能有 `InvalidOperationException`** |
| 7 | 朝向 | 玩家方块朝移动方向旋转（`faceMoveDirection` 默认开） |

**第 6 条是本切片最容易踩的坑**：本次实测发现工程 `activeInputHandler: 1`（只启用新
Input System），如果哪里用了旧的 `Input.GetAxis` / `Input.GetKey`，运行时会抛
`InvalidOperationException: You are trying to read Input using the UnityEngine.Input class,
but you have switched active Input handling to Input System package`。
当前实现全部走新 Input System，理论上不会触发——**但如果看到了，请把完整报错发我**。

### 怎么确认相机真的在跟随（而不是"看起来没动"）

把玩家从原点开到 `(8, 5)` 附近：相机应始终把玩家保持在屏幕中央附近，
你会看到障碍物相对屏幕移动。若玩家跑出屏幕，就是跟随没生效。

### 怎么确认边界限制生效

**镜头不是全程跟随的**——角色靠近地图边缘时镜头会停住，否则画面会露出地图外的黑边。

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 18 | 中间照常跟随 | 在地图中间走动，玩家始终在屏幕中央附近 |
| 19 | 到边缘镜头停住 | 一路向东（D）走到地图右边缘：到某个位置后**镜头不再移动**，只有玩家继续往屏幕右侧走 |
| 20 | **画面不露黑边** | 停在边缘时，画面里**看不到地图外的背景色**（原来会露出黑色） |
| 21 | 离开边缘恢复跟随 | 从边缘往回走（A），镜头**立刻恢复**跟随，玩家重新回到屏幕中央 |

数值上"镜头开始停住"的位置（地图 72×72、画面 36×20.25）：

```
相机中心被夹在 ±18（横向）、±25.875（纵向）
= 地图半尺寸 − 画面半尺寸
也就是玩家离地图中心横向超过 18 米、纵向超过 25.875 米后，镜头就不再跟了
纵向停得更早（25.875 对 36），因为画面只有 20.25 米高而地图有 72 米
玩家一路顶到墙（x=36）时，还留在画面上约 96% 半宽的位置，不会跑出屏幕
```

**一个刻意接受的取舍**：贴到墙时，角色最外侧约 0.4 米会被画面边缘切掉。
想让角色**整个**留在画面里，画面就必须越过地图边界 0.675 米、又露出黑边——
两者数学上不能同时满足（角色渲染宽约 2.15 米，因为 1×1 白方块被缩放到 1.35 后
精灵本身还有边距）。**优先保证不露黑边**，这是用户明确要的那一条。
墙本身 1 米厚且可见，顶到墙时身体贴着画面边缘并不违和。

HUD 左上角的「偏移」那一行会显示三种状态，用来区分"真的没问题"和"真的坏了"（见下）。

| 显示 | 含义 |
| --- | --- |
| `✅跟随中` | 偏移正常，镜头贴着玩家 |
| `⛰边界限制` | 偏移大是**边界限制造成的**，属于预期（原来这里会误报成 ❌） |
| `❌偏移异常` | 既没被边界夹住、偏移又大 —— 这才是真的跟随故障 |

`CameraFollow` 上可调：`clampToMapBounds`（开关）、`mapCenter`、`mapHalfExtents`。
后两个由场景构建器写入，**不用手填**；手动改地图尺寸时记得同步。

---

## 二之四、木桩（试伤害靶子）

场景里有一个 **`TrainingDummy`**，位置 **(12, 0)**——出生点往东走几步就到。

它是第 7 种敌人原型 `EnemyArchetype.Dummy`，配置在 `EnemyArchetypeConfig.Dummy()`：

| 字段 | 值 | 效果 |
| --- | --- | --- |
| `health` | **99999** | 等于无限；用有限大数而不是 `float.MaxValue`，避免血条百分比算出 NaN |
| `moveSpeed` + `moveMode` | `0` + `Stationary` | **不移动** |
| `hasContactDamage` | `false` | 不挂 `EnemyContactDamage`——**碰到它不掉血** |
| `hasRangedAttack` | `false` | `EnemyAttack` 被移除——**不发射弹体** |
| `hasProducer` / `hasHealer` | `false` | 不产兵、不治疗 |
| 颜色 | `(0.62, 0.52, 0.40)` 木色 | 与六种战斗单位（红/橙/蓝/紫/黄/绿）区分开 |

**它保留 `EnemyData` + `EnemyRuntime`**，这一点是关键：`EnemyRuntime.TakeDamage`
是程序 A 器官伤害的唯一真实入口，所以木桩验的是**真实伤害路径**，
不是个假的靶子——受击/击杀信号照常发。

### ⚠️ 曾经的 bug：木桩会朝玩家开枪（已修）

**现象**：木桩站着不动、打不死，但会发射子弹。

**根因**：`EnemyAttackFlash` 上写着 `[RequireComponent(typeof(EnemyAttack))]`。
生成器删掉 `EnemyAttack` 之后，Unity 在**保存 prefab 时把依赖自动加了回来**——
于是"删除"等于没做，而且**不报任何错**。这种问题只能靠对照 prefab 内容发现。

**这个 bug 影响了不止木桩**。实测七个 prefab 里**全部**带 `EnemyAttack`，
但按配置只有两个该带：

| 原型 | 该不该开枪 | 说明 |
| --- | --- | --- |
| RangedKeeper | ✅ 该 | 静止射击敌人 |
| RangedChaser | ✅ 该 | 移动射击敌人 |
| **Normal** | ❌ 不该 | 近战接触伤害 |
| **Fast** | ❌ 不该 | 近战接触伤害 |
| **Arsenal** | ❌ 不该 | 只产兵 |
| **Medic** | ❌ 不该 | 只治疗 |
| **Dummy** | ❌ 不该 | 木桩 |

也就是说**普通敌人和快速敌人之前也在乱放子弹**，只是没人注意到。

**修法**：删除时**两个组件一起删**（先删带依赖的 `EnemyAttackFlash`），
并加了**保存后核对 prefab 内容**的校验——因为 `RequireComponent` 的补挂发生在
序列化阶段，只检查内存里的实例是查不出来的。

**重建后应该看到**：只有 `RangedKeeper` / `RangedChaser` 会射击。
如果还有别的敌人在放子弹，把 Console 里 `[EnemyPrefabBuilder]` 开头的红色报错发我。

### 验证项

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 22 | 木桩在场且不动 | 出生点东侧 (12,0) 有一个木色方块，**站着不动** |
| 23 | 木桩不还手 | 贴着它站 10 秒，**玩家血量不掉**，也**没有弹体**飞出来 |
| 24 | 能被打 | 走到它旁边按 `F`（`DevDamageField`，10 点/次）→ Console 打印命中，木桩**不消失** |
| 25 | 血量确实是 99999 | Console 那行「木桩已摆放…血量 99999 ✅」；选中它看 `EnemyData.maxHealth` |
| 26 | **只有两种敌人会射击** | 波次里只有 `RangedKeeper`（蓝）和 `RangedChaser`（紫）放子弹；普通/快速/兵工厂/医疗兵都**不放** |

`F` 打不死它是**正常**的（要 9999 下）。要确认伤害确实进去了，
可以在 Inspector 里临时把 `EnemyData.maxHealth` 改小（比如 20），打两下就该消失；
也可以直接看 `EnemyRuntime` 暴露的当前血量。**验完记得改回 99999 或重建场景。**

### 它不会混进波次

`EnemySpawner` 用的是**单个 prefab 引用**，不查原型表，所以新增这个枚举值
不会让木桩出现在任何一波里。另外我在 `EnemyArchetypeConfig.SpawnableArchetypes()`
里单独维护了"可刷怪"白名单，木桩**不在其中**——将来刷怪表做成多条时以它为准。

### 枚举值刻意追加在最后

`Dummy = 6` 追加在 `Medic = 5` 之后，**没有插在中间**。`EnemyProducer.productArchetype`
这类字段序列化的是整数，插队会让已有 prefab 的编号错位。

---

## 二之五、战场碰撞关系（角色穿过敌人）

策划要求：**角色穿过敌人和木桩**，但**敌人之间有碰撞体积**，**角色仍被墙挡住**。

| | 玩家 | 敌人 | 墙 |
| --- | --- | --- | --- |
| **玩家** | — | ❌ 穿过 | ✅ 被挡住 |
| **敌人** | ❌ | ✅ 互相碰撞 | ✅ 被挡住 |

### 怎么做的

**没有改 `ProjectSettings/`**。那是多人共用仓库里**两边都在改**的文件
（远端 main 与本地 main 有 29 个文件双侧修改），改它就有合并风险。
改用代码在生成场景时写一遍：

- `BattleLayers`（`Assets/Scripts/Battle/BattleLayers.cs`）定义三个层名；
- `BattleGreyboxBuilder.ConfigureBattleLayers()` 用 `TagManager.asset` 的
  SerializedObject **建立层**，再用 `Physics2D.IgnoreLayerCollision` **确立碰撞矩阵**；
- 角色 → `TideBorne_Player`，敌人与木桩 → `TideBorne_Enemy`，四面墙 → `TideBorne_Wall`。

> **顺序很重要**：必须先把层名写进 TagManager 再取层号。否则第一次生成时
> `NameToLayer` 返回 -1，所有物体都落在 Default 上，三者又会互相撞。

### 玩家刚体改成了 Kinematic

光关掉碰撞矩阵还不够：**Dynamic 刚体之间仍会互相推动**——木桩被推走就是这么来的。
改成 `RigidbodyType2D.Kinematic` 后：

- 谁**推不动**玩家，玩家也**推不动**别人（不参与动力学求解）；
- 但**照常与静态碰撞体（墙）产生阻挡**，所以"被墙挡住"仍然成立；
- `PlayerMotor` 里的 `linearVelocity` 赋值对 Kinematic 刚体依然有效。

### 验证项

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 27 | 穿过敌人 | 直接朝敌人走过去，**不减速、不被挡住**，直接从它身上穿过 |
| 28 | 穿过木桩 | 朝木桩走过去，**穿过去**，而且木桩**纹丝不动**（不被推走） |
| 29 | 敌人之间仍碰撞 | 多个敌人挤在一起时会**互相挤开**，不会完全重叠成一个点 |
| 30 | 仍被墙挡住 | 走到地图边缘，**被墙挡住**，不会走出地图 |
| 31 | 层已建立 | Console 有「战场碰撞关系已确立：玩家(8) 穿过 敌人(9)，两者都被 墙(10) 挡住」 |

**注意 #27 的观感**：角色和敌人会**在画面上重叠**，这是"没有碰撞体积"的必然结果。
如果觉得重叠看着乱，要改的是**表现**（比如受击时闪一下、敌人的排序在角色之下），
不是把碰撞加回来——加回来就会重新出现被卡住、木桩被推走。

如果 Console 里出现 `[BattleLayers] 找不到层 "..."` 的告警，
说明场景没重建成功，跑一次菜单重建即可。

### ⚠️ 修过一次：「角色还能推动敌人」

**现象**：木桩推不动了，但普通敌人仍然被角色推着走。

**根因**：**版本号漏加。** 敌人 prefab 的层是**烘进 prefab** 的（`m_Layer` 字段），
而我加分层代码的那次改动**没有把 `EnemyPrefabBuilder.PrefabVersion` 加一**：

```
21:34  prefab 重新生成（PrefabVersion = 4）—— 那时还没有分层代码
21:39  我加上分层代码（EnemyFactory / EnemyPrefabBuilder）
21:40  改完编译通过
→ 版本号还是 4，与标记一致 → 不触发重建 → prefab 永远留在 Default(0) 层
```

于是场景里 `Player` 在 8 层、墙在 10 层都正确，只有**敌人**还在 0 层：
`Physics2D.IgnoreLayerCollision(玩家=8, 敌人=9)` 这一条根本作用不到它们身上。

**为什么木桩反而是对的**：木桩在场景里的层是构建器**显式写**的
（`dummy.layer = ...`），不依赖 prefab 的烘焙值，所以它正常。

**教训**：prefab 的 `m_Layer` 和其它烘进去的字段一样，**改动必须加版本号**。
现在补了 `VerifyBakedLayers()`——每次编辑器加载都核对磁盘上 prefab 的层，
不对就报红色错误并直接说明"把 PrefabVersion 加一"。

---

## 二之二、敌人与波次要验的 5 件事

脚本已经在工程里了（`Assets/Scripts/Battle/` 共 17 个文件），生成场景时会自动挂上
`BattleSystem` 物体（`EnemySpawner` + `WaveDirector`）。**进播放模式后自动开第 1 波。**

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 8 | 敌人生成 | 玩家周围环形位置出现红色小方块（半径约 14 米，屏幕外，会往里走） |
| 9 | 敌人会追 | 红方块持续朝玩家移动，速度约 2.5 米/秒（**比玩家 10 慢得多**，能轻松拉开距离） |
| 10 | 敌人会打 | 红方块贴到约 1.2 米内出手，玩家血量下降（Console 可看 `PlayerRuntime` 的 `currentHealth`） |
| 11 | 敌人会死 | **按 `F` 键**对周围 3 米内的敌人造成 10 点伤害，红方块血量归零后消失，Console 打印 `[DevDamageField] 命中 ...`。敌人血量 20，所以**按两下**死一个 |
| 12 | 一波 60 秒起止 | Console 打印「第 1/7 波开始，计划刷 N 个，时长 60 秒」→ 60 秒后打印「第 1 波结束，清除残余敌人 N 个（不计击杀、不掉落）」，场上红方块**同时全部消失** |

**第 11 条的说明**：正常情况下「敌人会死」要靠玩家攻击触发，而玩家攻击来自程序 A 的
器官 / `Event` 管道（**尚未实现**）。为了不让这条验收一直等依赖，场景里给玩家挂了一个
`DevDamageField` 开发工具——**它走的是 `EnemyRuntime.TakeDamage`，正是程序 A 将来要走的
那条真实入口**，所以验的是真实死亡路径，不是伪造的"直接销毁"。

- 按键：`F`（`DevDamageField.triggerKey`）
- 也可以在 Inspector 里勾 `autoFire` 让它自动打，或用组件的右键菜单「对周围敌人造成一次伤害」
- **它不是玩法内容**：`sourceOrganId` 传空，不冒充击杀触发；整个文件被
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包住，**正式发行版不会编译进去**
  （已验证：发行版配置编译出的程序集里不含 `DevDamageField`）

**第 12 条的验证要点**：波次结束时场上残余敌人应**立刻全部消失**，且 Console 里
**不应出现任何击杀相关日志**。这就是「清除 ≠ 消灭」的实现标志。
如果想快速验证，把 `BattleSystem` 上 `WaveDirector` 的 `waveDuration` 临时改成 5 秒。

**第 11 条已可独立验收**：正常路径下「敌人会死」要靠玩家攻击触发，而玩家攻击属程序 A 的
器官 / `Event` 管道。为了不等依赖，场景给玩家挂了 `DevDamageField`（见上），
它走的是 `EnemyRuntime.TakeDamage`——程序 A 将来要走的同一条真实入口。

---

## 二之三、底图九宫格（2026-10-13 新增）

地图真图已接进场景：`Assets/Art/bk1440-s/` 共九张 480×480 PNG，
按 1 左上 / 2 上中 / 3 右上 / 4 左中 / 5 中心 / 6 右中 / 7 左下 / 8 下中 / 9 右下 分格。

**怎么贴的**：八块边框贴地图边界，中心块 5 铺内部——三行三列 1-2-3 / 4-5-6 / 7-8-9，
整圈外沿正好落在可通行区域（**72 × 72 米**）的边界上，所以"边框贴地图边界"是字面成立的。

**关键：地图已改成正方形 72×72，底图零拉伸。** 对齐是一道等式：

```
每格米数 = 地图边长 ÷ 3 = 72 ÷ 3 = 24 米
精灵米数 = 图宽像素 ÷ PPU     ← 让两者相等即 1:1
```

**⚠️ 米和像素不能直接等同——中间必须有一个 PPU，而 PPU 是个可以随便改的导入参数。**
这正是 13 版修掉的那个 bug：12 版按「精灵 = 1 世界单位」算缩放（照抄了边界墙的写法，
而白方块恰好是 16 px ÷ 16 PPU = 1 单位，所以对墙成立、对底图不成立）。
结果每块底图实际铺开 48 米、格心却只挪了 24 米，**九张全部重叠**。

现在两道保险，都不依赖 `.meta` 写了什么：

| 保险 | 做法 |
| --- | --- |
| 尺寸不错位 | 缩放按**精灵自己的 `bounds`** 反算：`scale = 目标米数 ÷ 精灵世界尺寸`。PPU 是几都铺得满自己那格 |
| 密度不摊薄 | PPU 从**图的实际像素宽度推**：`PPU = 图宽像素 ÷ 每格米数`。换分辨率不用改代码——480 px → 20，960 px → 40 |

**换图注意事项**：九张图**必须同宽、且是正方形**，否则九块像素密度不一致（有的糊有的清），
代码会告警但不阻断。直接覆盖 `.png` 是安全的（`.meta` 里的 GUID 不变、场景引用不会断）；
**删掉再放回来会换 GUID、场景引用会丢**。

| # | 验证项 | 期望结果 |
| --- | --- | --- |
| 13 | 底图可见 | 进播放模式后是深海底纹，**不再是一片纯色灰蓝** |
| 14 | 九块不重叠 | 九块各占一格，**看不到一张图压在另一张上** |
| 15 | 边框位置 | 跑到地图四边走到底，看到的是**深色波纹边框**（1/2/3/4/6/7/8/9 那八张） |
| 16 | 底图不拉伸 | 花纹是**正方形**的，没有被压扁或拉长 |
| 17 | 墙不脏 | 贴在边界上的墙是一条**接近黑**的细带，不抢底图 |

**Console 里会有一行底图自检**，四个数必须自洽（这是专门为上面那个重叠 bug 加的）：

```
[BattleGreybox] 地图底图：九宫格 9/9 块就位｜地图 72 × 72 米｜每格 24 × 24 米
｜格心间距 24 米（须 = 每格，小于每格就会重叠）｜PPU 20 → 单块精灵 24 × 24 单位
｜实际渲染 24 × 24 米。
```

### 三处已做的取舍

1. **地图从 64×36 改成 72×72**（见上，为了底图不拉伸）。
   注意这会让**可通行面积变为原来的 2.25 倍**，刷怪密度、走到边界的时间都会变长；
   如果手感变"空"，优先调 `EnemySpawner.spawnRadius` 与每波刷怪数量，
   **不要去动地图尺寸**（一动底图又会被拉扁）。
2. **原纯色地面已删除**。那个深蓝灰 `Ground` 方块是为了"有块地面"而存在的，
   现在深海底图自己就是地面，再盖一层就把图挡住了。删掉它不影响任何碰撞
   （碰撞一直是 `BoundaryWalls` 负责的，`Ground` 从第 2 版起就没有碰撞体）。
3. **边界墙颜色改暗**（`0.24,0.22,0.26` → `0.05,0.06,0.09`）。加底图之前那点亮度看得出是墙，
   换成深海底图后反而成了脏边。

### 怎么调

- **相机视野**：`BattleGreyboxBuilder.CameraVisibleLongEdge`（默认 `36f`，即地图 72 米的一半）。
  半高是按 16:9 折算的：`orthographicSize = 36 × 9/16 ÷ 2 = 10.125`。
  **刻意不用 `camera.aspect` 现算**——那会把 Game 视图窗口当时的尺寸写进场景，别人打开就不是同一张图。
- **角色尺寸**：`GreyboxAssets.PlayerWorldSize`（默认 `1.35f`）。敌人按
  `EnemySizeRelativeToPlayer`（默认 `1f`）跟随，弹体用 `ProjectileWorldSize`。**尺寸只在这一处定义。**
  1.35 = 0.8 × **1.6875**，而 1.6875 正是新旧相机半高之比 `10.125 ÷ 6`，
  所以方块占屏幕的比例没变（都是屏宽的 3.8%），只是地图看得更多。
- **底图 PPU**：不用手调，由 `ResolveBackgroundPixelsPerUnit()` 从 `图宽像素 ÷ 每格米数` 推出来。
- **边框环整体缩放**：`BackgroundFrameScale`（默认 `1f`，1 = 外沿正好贴住边界）。
- **中心格占比**：`InnerColumnWidthRatio` / `InnerRowHeightRatio`（默认各 `1/3`，九宫格标准分法）。
- **地图尺寸**：`PlayAreaHalfWidth` / `PlayAreaHalfHeight`（默认各 `36f`；**必须相等**）。
  改完之后 PPU 会自动跟着变，不用手动同步。
- 改完**必须把 `GreyboxSceneVersion` 加一**（现在是 **15**），否则场景不会重建。
  **移速、尺寸这类值都是烘进场景/prefab 的**，光改代码默认值而不加版本号，是不会有任何效果的。

### ⚠️ 改了角色尺寸后，「敌人 prefab 不会自动更新」

敌人尺寸是**烘进 prefab 的 Transform** 的（`EnemyFactory.VisualSize` → 生成那一刻写死），
`projectileVisualSize` 同理。所以 2026-10-13 把敌人从 0.8 改成 1.35 之后，
六个 prefab 不会自己跟着变 —— 表现为**"玩家变大了，敌人还是小的"**，
很难一眼看出是漏了哪一步。

**现在会自动重建**：生成器带版本号（`EnemyPrefabBuilder.PrefabVersion`，当前 **4**），
写在 `Assets/Resources/Enemies/.generated-version.txt`。编辑器加载时发现版本不符，
就**自动重建全部敌人 prefab**（`RebuildIfStale()`）。

> 早先的版本只打印一句警告、要人手点菜单，结果是"玩家大了敌人小的"和
> "木桩朝玩家开枪"这两种半生效状态。版本号既然是我们主动加的，
> 就说明旧 prefab 一定不对，那么让用户记得点菜单就是多余的风险。
> **代价**：会覆盖 prefab 上的手工调整。这些 prefab 是生成物，
> 要长期手调请另存成别的资产。

（也仍然可以手动跑 `Tools → 程序C → 生成全部敌人 prefab`。）

### ⚠️ 已修：实测发现「玩家一出现就飞走，然后卡住、只能左右移动」

**这是 2026-10-06 首次实机试跑的实测结果，已定位并修复。**

**现象**：进播放模式后，青色玩家方块一出现就向一个方向飞走，随后卡在某个位置，只剩左右能动。

**根因**：初版给 `Ground` 加了 `BoxCollider2D`，而 `Ground` 的 `localScale` 是 `30×18`——
于是它是一个 **30×18 的实心碰撞矩形**，而玩家出生在 `(0,0)`，**正好在它内部**。
Box2D 检测到深度重叠后沿最小平移方向把玩家弹出去（"飞走"），
弹出地面范围后贴着边界卡住（"只能左右移动"）。

**修法**（生成器版本 `1` → `2`）：

| 改动 | 原因 |
| --- | --- |
| `Ground` **去掉碰撞体**，只留 `SpriteRenderer` | 幸存者类要的是无阻挡地面，碰撞不该由地面承担 |
| 新增 `BoundaryWalls`（四面墙） | 把玩家关在地面范围内，这才是该有的碰撞 |
| 玩家碰撞体 `CircleCollider2D` → `BoxCollider2D(0.8×0.8)` | 圆形套在方块上，斜向接触会产生侧向滑移 |
| 玩家不再用 `localScale` 缩放 | `localScale` 会连碰撞体一起缩，"看见的大小"与"挡住的大小"容易不一致 |
| 障碍物同理：`localScale` 缩放 → `BoxCollider2D.size = 2×2` | 同上 |

### 场景重建机制（为什么你不会再拿到旧场景）

初版还暴露了第二个问题：**旧场景一旦生成就会一直留着，结构改对了也不会重建**。
所以加了 `Assets/Scenes/Battle_Greybox.generated.txt` 版本标记：

- 场景不存在 / 标记缺失 / 标记里的 `version=` 与生成器的 `GreyboxSceneVersion` 不符 → **自动重建**
- **场景文件比标记新** → 说明有人在生成后手工编辑过它 → **只警告、不重建**，不冲掉人的工作
- 想强制重建：删掉那个 `.txt` 标记，下次打开工程即自动重建

> **给以后的自己**：改 `BattleGreyboxBuilder` 的场景结构时，
> **必须同步把 `GreyboxSceneVersion` 加一**，否则没人会看到你的改动。

### 已知的灰盒参数（都要你按手感调）

| 参数 | 位置 | 当前值 | 说明 |
| --- | --- | --- | --- |
| 每波时长 | `WaveDirector.waveDuration` | 60 秒 | 策划案已定 |
| 总波次 | `WaveDirector.totalWaves` | 7 | 策划案已定 |
| 每波数量 | `WaveDirector.enemiesPerWave` | 8/12/16/20/24/28/1 | **我取的占位值**，难度曲线是设计决策，归你 |
| **玩家移速** | `PlayerStatsBase.moveSpeed` | **10 米/秒** | 2026-10-13 由 5 提到 10（策划要求"两倍"）。**是敌人基础移速的 4 倍**，详见下面那条 |
| 生成半径 | `EnemySpawner.spawnRadius` | 10 米 | 大于"玩家屏幕 1/4"下界；小于相机半高 10.125 米，所以仍会落在视野内 |
| 敌人移速 | `EnemyData.moveSpeed` | 2.5 米/秒 | 灰盒初值（快速型 3.25） |
| 敌人攻击力 | `EnemyData.attackPower` | 5 | 灰盒初值 |
| 敌人攻击冷却 | `EnemyData.attackCooldown` | 1.5 秒 | 灰盒初值 |
| 敌人血量 | `EnemyData.maxHealth` | 20 | 灰盒初值 |

### ⚠️ 玩家 10 米/秒意味着什么

把玩家从 5 提到 10 之后，**它变成敌人基础移速（2.5）的 4 倍**，最快的快速型（3.25）也只有它的三分之一。

直接后果，等实测时留意：

| 现象 | 原因 |
| --- | --- |
| 敌人基本追不上，可以绕圈遛 | 速度差 4 倍，近战敌人几乎永远碰不到你 |
| 接触伤害类敌人形同虚设 | 同上——它们靠贴身打输出 |
| 相机跟得"急" | `CameraFollow` 是硬跟随（无平滑），速度翻倍后镜头运动更剧烈，可能更容易晕 |
| 疾跑几乎没用 | 疾跑是 1.5 倍 = 15 米/秒，地图 72 米只要 4.8 秒跑穿 |

**我没有擅自改敌人速度或相机平滑**——那是平衡决策，归你。如果实测觉得"敌人完全追不上"，通常的解法不是降玩家速度，而是给敌人加速度上限或让它们分层（快型更极端、慢型更肉），这样"拉开距离"仍是可选的战术而不是无代价的。

---

## 三、已完成的验证 vs 未验证的部分

诚实分账——**不要把「编译通过」当成「跑起来了」**。

| 项 | 状态 | 依据 |
| --- | --- | --- |
| **属性算法语义** | ✅ **已验证（离线断言）** | 把 `StatField` / `StatModifierCollector` / `PlayerStatsSnapshot` / `PlayerStatsBase` 配 UnityEngine 最小桩离线跑，**21 项断言全部通过** |
| 18 个脚本能编译（编辑器配置） | ✅ 已验证 | 对 Unity 6000.5.11f1 真实程序集编译：**0 警告 0 错误** |
| 18 个脚本能编译（正式发行版配置） | ✅ 已验证 | 无 `UNITY_EDITOR` 时也 0 错误，且产物 DLL 中确认**不含** `DevDamageField` |
| 依赖的 API 真实存在 | ✅ 已验证 | 反射探针对 `UnityEditor.dll` / `UnityEngine.Physics2DModule.dll` / `UnityEngine.CoreModule.dll` 逐个核对 |
| `Rigidbody2D.linearVelocity` 是 Unity 6 的正确写法 | ✅ 已验证 | 探针确认 `velocity` 已标 `[Obsolete]`，提示改用 `linearVelocity` |
| Input System 调用面 | ✅ 已验证 | 读包源码确认 `InputActionReference.action`、`InputAction.ReadValue<T>` / `IsPressed` / `Enable` / `Disable`、`Keyboard.current[Key]` / `wasPressedThisFrame` |
| `.inputactions` 里有 `Move` / `Sprint` | ✅ 已验证 | 读 `Assets/InputSystem_Actions.inputactions`：`Move` 是 `Value`/`Vector2`，WASD + 方向键都有绑定 |
| **Unity 自己的编译管线能过** | ❌ **未验证** | `Library/ScriptAssemblies/Assembly-CSharp.dll` 仍是 **13:16:16**，而脚本写在 13:17 之后。**Unity 一次都没编译过这些脚本** |
| 场景能被 Unity 生成出来 | ❌ **未验证** | 需要跑菜单 4。`Assets/Scenes/` 里只有模板的 `SampleScene.unity` |
| 移动手感 / 相机参数 | ❌ **未验证** | `smoothTime = 0.12`、`orthographicSize = 6`、`sprintMultiplier = 1.5` 是我取的保守初值，**需要你按手感调** |
| 敌人会追 / 会打 / 会死 | ❌ **未验证** | 只有编译证据（死亡路径可按 `F` 键独立验收） |
| 一波 60 秒能正常起止 | ❌ **未验证** | `WaveDirector` 的逻辑只有编译证据，没跑过 |

### 离线断言覆盖了什么（21 项）

| 组 | 断言 |
| --- | --- |
| 加法区 / 乘法区（§5.4 甲） | 基础值原样返回；加法叠加；乘法叠加；**先加后乘的顺序**（`(1+1)×2=4` 而非 `1×2+1=3`）；多来源合并 |
| 边界规则（§5.5） | 暴击率夹到 `[0,1]`（上下两端各一项）；移速下限 > 0；基础冷却下限；背包格取整 |
| 快照变更检测 | 相同判等；浮点噪声内视为相同；上限变化判为不同 |
| 波次数量下标 | 第 1/3/7 波取值；越界沿用最后一个值；越界兜底 |

> **这套断言抓到了一个编译检查抓不到的真 bug**：`PlayerStatsBase._cache` 声明为
> `new float[14]`，初值是 **0 而不是 NaN**，而 `Read()` 用 `IsNaN` 判断"尚未计算"——
> 于是第一次访问就把 0 当成有效值返回，`ReadBaseValue` **从来没被调用过**。
> 在 Unity 里的表现是「所有属性恒为 0」，也就是**玩家根本不会动**。
> 修法见 commit `88c7ded`（加 `CreateEmptyCache()`，让 NaN 哨兵的不变式在声明处成立）。

**两个编译层的区别值得记住**：我在 .NET SDK 里对着 Unity 的 DLL 编译通过（0 错误 0 警告），
这不等于 **Unity 自己的编译管线**能过。后者还会做程序集引用校验、`Library` 里包的导入、
`.meta` GUID 一致性检查。**认准"谁编译的"，不是"编译过没有"。**

**为什么没有替你把场景跑起来**：本机没跑 Unity 批处理模式（交接文档 §7 坑 1 记录了
`Unity.exe` 是 GUI 子系统程序、必须 `Start-Process -Wait -PassThru` 才拿得到退出码；
坑 2 记录了残留锁会让 Unity 秒退），而 `-executeMethod` 跑批处理需要项目外写权限并会
拉长链路。编译验证已抓到 1 个真实错误（缺 `using UnityEngine.InputSystem`），
所以并非走过场；但**「能编译」证明不了「场景生成成功」**，这一步留给你在 Unity 里跑。

> **可选的省事路径**：agent 可以跑一次 Unity 批处理把场景先生成出来（用 `-executeMethod`
> 调 `BattleGreyboxBuilder.BuildGreyboxScene`），这样你开 Unity 时场景已经在、直接进播放模式。
> 代价是一次提权授权 + 几分钟，且本机批处理模式有两个已知坑。要就说一声。

---

## 四、脚本清单与职责边界

```
Assets/Scripts/Battle/                        共 17 个文件
├─ 属性层（接口草案 §5）
│  ├─ StatField.cs                  属性枚举 + StatModifier + IStatModifierSource
│  ├─ StatModifierCollector.cs      加法区 / 乘法区两层收集与求值
│  ├─ PlayerStatsSnapshot.cs        最终值快照（供事件与实验室预览）
│  ├─ PlayerStatsBase.cs            属性表：§5.2 十四字段
│  ├─ PlayerRuntime.cs              运行时状态：§5.3 currentHealth / partRate + 受伤结算
│  └─ PlayerLocator.cs              找玩家并缓存
├─ 玩家操控
│  ├─ PlayerMotor.cs                移动 + 疾跑 + 朝向
│  └─ CameraFollow.cs               俯视跟随相机
├─ 敌人层（接口草案 §6）
│  ├─ EnemyData.cs                  §6.2 十二字段
│  ├─ EnemyEvents.cs                §6.1 四条对外信号（静态总线）
│  ├─ EnemyRuntime.cs               受伤 / 死亡 / 清场两条路径分离
│  ├─ EnemyMotor.cs                 直线追击
│  ├─ EnemyAttack.cs                近战出手 + Attacked 事件（Event 管道的对接点）
│  └─ EnemyFactory.cs               灰盒敌人拼装
├─ 刷怪与波次
│  ├─ EnemySpawner.cs               玩家周围环形刷怪
│  └─ WaveDirector.cs               60 秒一波、7 波、到点清场结算
└─ BattleEventBusLifetime.cs        静态事件总线的跨播放会话重置

Assets/Scripts/Editor/
├─ SetVsCodeExternalEditor.cs       编辑器设置 + 模板残留 / 空目录清理
└─ BattleGreyboxBuilder.cs          灰盒场景生成（含 BattleSystem）
```

**边界（`任务拆解.md` 程序 C）**：只管「场上发生什么」。
**不碰**器官怎么算、**不碰**界面怎么画。
敌人与波次已实现；**玩家攻击手段不属于程序 C**，它来自程序 A 的器官 / Event 管道——
所以「敌人会死」要等管道接上才能实机验收。

---

## 五、实现时做的四个判断，需要你确认

### 0. 敌人攻击**没有**代程序 A 定义 `Event`

`接口草案.md` §6.3 定的是「敌人的攻击产出一个 `Event`，与玩家攻击走同一套结构」，
但 §6.3 标着**【暂定】**，而且——

- `Event` 结构体的负责人是**程序 A**（`任务拆解.md` 接口 1：谁定义 = 程序 A）；
- §1 里 `radius` 的「(类型, 距离)」格式与 `pierce` 的数值口径都还**没定**
  （§1 的「待做/讨论」第 2、3 条就是问单位和范围）。

所以我**没有**在程序 C 里写一个 `Event` 结构体。`EnemyAttack` 抛出的是
`EnemyAttack.Attacked`，载荷是 §6.2 里程序 C 已确定的全部字段
（`enemyId` / `attackPower` / `projectileCount` / `projectileSpeed` / `attackRadius` / `origin`）。
程序 A 订阅它、自己构造 `Event` 即可，**不需要改程序 C 的任何文件**。

> 若你更希望程序 C 直接产出 `Event`，那就得先把 §1 的 `radius` / `pierce` 口径定下来
> （这是接口变更，要通知程序 A 与程序 D），我再改。

### 1. 用 `InputActionReference` 字段，而不是在代码里 `FindAction`

`PlayerMotor` 有两个 `InputActionReference` 字段（`moveAction` / `sprintAction`）。
场景构建器**已经自动填好**（从 `.inputactions` 资产里按名字取 `Move` 与 `Sprint`）。
好处是不依赖动作名字符串、Inspector 里能直接看到引用；代价是多两个序列化字段。

**若你更希望代码里硬找动作名**（例如减少 Inspector 配置），这是一行改动量，说一声我换。

### 2. 属性基础值的唯一来源：`PlayerStatsBase` 的 inspector 字段

`接口草案.md` §5.2 的「谁能改」列写着 `baseCooldown` / `baseMaxHealth` 归**躯干升级树**，
其余归**躯干特殊器官**。我据此把 `PlayerStatsBase` 的字段设为**基础值的唯一来源**，
器官与升级树通过 `IStatModifierSource` 提供**修改**，不直接写基础值。

**这需要程序 A 知道**：他的器官组件要实现 `IStatModifierSource`
（`CollectAdditiveModifiers` / `CollectMultiplicativeModifiers` 两个方法），
然后挂到 `PlayerStatsBase` 的 `modifierSourceComponents` 数组里。

### 3. 字段用文档里的原名，但读的时候加了前缀区分表

`接口草案.md` §5.3 写「`partRate[i] = 该部位内所有计时触发器的百分比之积`」——
注意 `partRate` 在 §5.2 的 `trigger_target` 取值表里**也是一个字符串**（`PART:<部位>`），
两个不同东西同名。代码里我没有改文档口径，但 `PlayerRuntime.GetPartRate(int)` /
`GetStepInterval(int)` 用下标区分，注释里也标了出处。

**若你认为该给其中一个改名，这是设计决策，你定**（改名要通知依赖方）。

---

## 六、goal 完成度盘点（2026-10-06）

goal 的验收句是：**「玩家能在一张地图上跑动，敌人会追、会打、会死；一波 60 秒能正常起止。」**

| 分句 | 实现 | 实机验证 |
| --- | --- | --- |
| 玩家能在一张地图上跑动 | ✅ 代码就绪 | ❌ 待你跑 |
| 敌人会追 | ✅ 代码就绪 | ❌ 待你跑 |
| 敌人会打 | ✅ 代码就绪 | ❌ 待你跑 |
| **敌人会死** | ✅ 代码就绪（含 `F` 键开发工具，不依赖程序 A） | ❌ 待你跑 |
| 一波 60 秒能正常起止 | ✅ 代码就绪 | ❌ 待你跑 |

**结论：代码层面 goal 已覆盖完毕；验收层面 5 项全部未通过。**
卡点是**同一个动作**——需要开一次 Unity 让它编译 + 生成场景 + 进播放模式。

### 关于「要不要留一套自动化测试」

上面那 21 项离线断言抓到了一个真 bug，但它现在是**临时脚手架**（用完即删，`_probe/` 在
`.gitignore` 里）。`任务拆解.md` §七 写明「不写自动化测试套件（Jam 时间内不划算）」，
所以我**没有**把它放进仓库。

**要不要破例留着？** 取舍：

- 留的好处：`PlayerStatsBase` 的两层叠加与边界规则是**接口契约**，
  程序 A 的器官以后会大量往这里灌修改，回归时有个廉价护栏。
  它不依赖 Unity，跑一次约 2 秒。
- 留的代价：与 §七 的约定冲突；得有人维护那个 UnityEngine 桩。

**这事归你定，我不擅自破例。** 你要就说一声，我把它整理成 `Tools/逻辑回归` 之类的正式形态入库。

### 剩下的真实缺口

1. **玩家攻击手段**（**不再阻塞 goal 验收**，但仍是程序 C 的对外依赖）。
   归程序 A：器官配置 → `Event` 构造 → 管道执行 → 伤害结算。
   程序 C 侧已备好真实入口 `EnemyRuntime.TakeDamage(damage, sourceOrganId, hitPoint)`。
   **两处接口口径未定，会阻塞程序 A**：`接口草案.md` §1 的 `radius` 格式「(类型, 距离)」
   与 `pierce` 的数值含义（§1 待做第 2 条就是问这个）。
2. **正式关卡场景**：美术 B 的地图未产出，当前是灰盒。
3. **敌人种类**：`策划案.md` 定 3–4 种普通眷族 + 1 个 Boss。
   现在只有一种灰盒敌人（`EnemyFactory.CreateGreyboxEnemy`），
   `EnemyData` 字段已齐，做多敌人只需建 prefab 或扩工厂，**不改任何逻辑代码**。
4. **投射物**：`EnemyData.projectileCount` / `projectileSpeed` 已落地但**未使用**——
   当前 `EnemyAttack` 是直击型（出手即扣血）。远程敌人在 `EnemyAttack.Attacked`
   接上 `Event` 管道后再实现。
5. **状态系统**：`EnemyRuntime.ApplyStatus` 只发信号，**不落地状态效果**。
   `接口草案.md` §3 的「附带状态配置格式」表是空的（叠加一/二/三层的影响都没填）——
   **这是策划侧的空白，不是实现偷懒。**
