# 程序 D · M1 对接说明

版本：2026-10-07  
状态：本地已实现，供程序 A / B / C 联调  
命名空间：`Tideflesh.Systems`

一句话：程序 D 管**图鉴 JSON、波次表、库存、髓质、战斗↔实验室流程**；不管器官怎么结算、不管实验室 UI、不管玩家走路和敌人。

---

## 1. 文件在哪

| 路径 | 内容 |
|---|---|
| `Assets/Data/Organs/organs.json` | M1 图鉴：拳头、锈刀、多肢触手 |
| `Assets/Data/Waves/waves.json` | 两波配置；第 1 波必掉触手 + 锈刀 |
| `Assets/Scripts/Systems/Catalog.cs` | 加载与字段校验 |
| `Assets/Scripts/Systems/Inventory.cs` | 实例、槽位、未装备上限 5、卖/丢 |
| `Assets/Scripts/Systems/RunController.cs` | 状态机与对外 API |
| `Assets/Scripts/Systems/GameFlowBootstrap.cs` | 场景挂载示例：拖两个 TextAsset 即可 `StartRun` |
| `Assets/Scripts/Systems/Editor/ProgramDTests.cs` | Edit Mode 测试 |

首次用 Unity 打开工程后，会为上述资源生成 `.meta`，请一并提交。

---

## 2. 怎么接入（最小用法）

```csharp
using Tideflesh.Systems;

var catalog = Catalog.LoadOrgans(organsTextAsset.text);
var waves = Catalog.LoadWaves(wavesTextAsset.text, catalog);
var run = new RunController(catalog, waves, OnSignal);
run.StartRun();

void OnSignal(string name, object payload)
{
    // 按 name 分发给 A / B / C
}
```

或挂 `GameFlowBootstrap`：Inspector 里指定 `organsJson`、`wavesJson`，`Awake` 里会自动 `StartRun`。联调时把 `OnSignal` 换成你们的事件总线即可。

可读状态：

- `run.Phase`：`boot` / `combat` / `wave_settling` / `lab` / `run_won`
- `run.Wave`、`run.Marrow`
- `run.Inventory`
- `run.CanStartNextWave()`

波次时长在表里：`waves.json` → `duration_sec`（目前 60）。`RunStarted` **只带 wave 号**，C 自己查表或由 D 再补一个查询接口。

---

## 3. 信号（D 发出）

回调签名：`void Emit(string name, object payload)`。

| 信号名 | Payload 类型 | 何时 | 谁该听 |
|---|---|---|---|
| `LoadoutChanged` | `InventorySnapshot` | 开局装备拳头后；以及装备/拆/卖/丢之后 | A 重建管道；B 刷新 UI |
| `RunStarted` | `RunStartedPayload { Wave }` | 进入战斗 | C 刷怪开表；A 启动管道 |
| `WaveEnded` | `WaveEndedPayload { Wave, Reason }` | 波次结算开始；M1 的 `Reason` 固定 `"timer"` | C 停战斗；A 停管道 |
| `LabOpened` | `LabOpenedPayload { Drops, MarrowDelta }` | 进入实验室（第 2 波结束不发） | B 打开实验室 |
| `RunEnded` | `RunEndedPayload { Result }` | M1：第 2 波结束后 `Result = "won"` | 全员 |

### 3.1 `InventorySnapshot`（给 A / B）

```text
Equipped: Dictionary<部位, Dictionary<slot, instance_id>>
  部位: "head" | "hand" | "leg" | "torso"
  slot: 从 1 开始（头/手/腿各 1–6，躯干特殊槽 1–2）
  没有装备的部位键会省略；省略 = 该部位全空

Unequipped: List<OrganInstance { instance_id, organ_id }>
```

**装备槽里只有 `instance_id`。** 查器官定义：

```csharp
OrganDefinition def = run.Inventory.OrganOf(instanceId);
```

不要假设 `LoadoutChanged` 会带完整器官字段。

### 3.2 `LabOpened.Drops`

与背包条目相同：`List<OrganInstance>`，每项含 `instance_id`、`organ_id`。掉落已经进 `Unequipped`，payload 是本次新增列表，方便 UI 高亮。

---

## 4. 调用（别人调 D）

### 程序 C

| 调用 | 说明 |
|---|---|
| `run.CombatTimerFinished()` | **仅** `phase == combat` 时合法；M1 在 60 秒到点调用 |

不要自己切波次。听 `RunStarted` / `WaveEnded` 启停战场即可。

M1 约定：时间到就结算，场上敌人清掉，**不**因此广播击杀触发。

### 程序 B

| 调用 | 说明 |
|---|---|
| `run.Equip(instanceId, part, slot)` | 仅实验室；只从「未装备」装入；`part` 必须与器官 `part` 一致；目标槽必须空 |
| `run.Unequip(part, slot)` | 仅实验室；拆下进未装备（可暂时超过 5） |
| `run.Sell(instanceId)` | 仅实验室；髓质 `+= shop_price / 2`（整除） |
| `run.Discard(instanceId)` | 仅实验室；删除，不给髓质 |
| `run.StartNextWave()` | 仅实验室且 `Unequipped` 数量 ≤ 上限（默认 5） |
| `run.CanStartNextWave()` | 给「开始下一波」按钮用 |
| `run.TryBuy(...)` / `run.TryUpgradeTorso(...)` | M1 固定 `{ Ok=false, Reason="not_open" }` |

没有「一次替换」：先 `Unequip` 再 `Equip`。

按钮规则建议：

- `phase != lab` → 禁用装备/下一波
- `!CanStartNextWave()` → 禁用下一波，并显示「请卖掉或丢掉 N 件」

### 程序 A

| 你需要的 | 怎么拿 |
|---|---|
| 当前装载 | `LoadoutChanged` → `InventorySnapshot`，再用 `OrganOf` |
| 战斗开始/结束 | `RunStarted` / `WaveEnded` |
| 器官数值与类型 | `OrganDefinition`：`type` = `trigger` / `executor` / `torso_special` |

背包容量、髓质倍率由 D 根据已装备躯干器官计算；A 不要改库存。

---

## 5. 器官 JSON 字段（A 与 D 共用）

以 `organs.json` 为准。没有 `cooldown`、没有 `damage_type`。

**公共：** `id`, `name`, `description`, `part`, `type`, `rarity`, `shop_price`, `icon`

**trigger：** `trigger_on`（`hit`/`kill`/`hurt`/`interval`/`expire`）、`trigger_target`（`SELF_SLOTS` / `PART:head|hand|leg` / `ALL_EMITTERS` / `NONE`）、`chance`（0–100）；`interval` 时要有 `rate_percent`（0.1–1.0）

**executor：** `damage`, `count`, `radius`；可选 `pierce`, `speed`, `chance`, `status{id,duration,max_stack}`

**torso_special：** `modifiers`（D 认 `unequipped_capacity_delta`、`marrow_gain_mult`）、`costs`

M1 样例：

- 开局已装备：`fist` → 手部槽 1（不占 5 格背包）
- 第 1 波掉落：`many_limb_tentacle`（击杀 → `SELF_SLOTS`）+ `rusty_knife`（30% 流血）

---

## 6. M1 流程时序

```text
StartRun
  → LoadoutChanged（拳头在 hand:1）
  → RunStarted { Wave=1 }
  → phase=combat

C: CombatTimerFinished()
  → WaveEnded { Wave=1, Reason="timer" }
  → 掉落进背包，髓质 +10（可乘躯干倍率）
  → LabOpened { Drops, MarrowDelta }
  → phase=lab

B: Equip(触手, "hand", 2) 等
  → 每次成功操作后 LoadoutChanged

B: StartNextWave()   // 要求未装备 ≤ 5
  → RunStarted { Wave=2 }
  → phase=combat

C: CombatTimerFinished()
  → WaveEnded { Wave=2, Reason="timer" }
  → RunEnded { Result="won" }
  → phase=run_won
  （不再进实验室）
```

非法调用会抛：

- `RunException`：错误阶段、重复 `StartRun`、不能下一波等
- `InventoryException`：部位不符、槽占用、空槽、非法槽号等
- `CatalogException`：JSON 校验失败（含 `OrganId`、`Reason`）

---

## 7. M1 明确不做（不要按已完成对接）

- 完整 7 波、Boss、商店营业、躯干升级树数值
- 暂停 / 存档 / 主菜单 / HUD
- 掉落与当前 build 加权、43 器官填满
- `expire` 触发语义（枚举可出现，样例不用）

---

## 8. 联调检查清单

- [ ] Unity 打开工程，两个 JSON 能在 Project 窗口看到，并生成 `.meta`
- [ ] `GameFlowBootstrap` 或等价代码能 `StartRun`，Console 出现 `LoadoutChanged` → `RunStarted`
- [ ] C 模拟到点调用 `CombatTimerFinished`，出现 `WaveEnded` → `LabOpened`，背包有触手和锈刀，髓质为 10
- [ ] B 能装触手到手部槽 2；超 5 格时下一波按钮禁用
- [ ] A 在 `LoadoutChanged` 后能用 `OrganOf` 读到拳头与触手定义
- [ ] 第二波到点后只有一次 `LabOpened`（第一波的），最后是 `RunEnded won`
- [ ] Edit Mode：Test Runner 跑 `ProgramDTests`

---

## 9. 联系与变更

接口要改字段或信号名时：先在群里公告，再改本文件和代码。  
当前实现在本地 git（`feat(d): add M1 organ data, inventory, and wave run flow`），未推远端时以本仓库工作区为准。
