# MVP Numbers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把已锁定的 MVP 数值规划落到程序 D 的 JSON / 测试 / 对接说明里，供 Demo 联调；不改器官效果设计文案，不改 A/C 战斗结算代码。

**Architecture:** 数字只进数据文件：`organs.json`（约 11 个可加载器官，史莱姆核心仍 TBD 不入库）、`waves.json`（7 波）、新建 `mvp_baseline.json`（玩家与 4+1 敌人基准，给 C 对照）。`Catalog` / `RunController` 逻辑保持现状；仅更新 Edit Mode 测试期望。填表口径见 spec：`对敌 = max(1, Event.damage)`，`对己 = max(1, attackPower)`。

**Tech Stack:** Unity 6、`Tideflesh.Systems`、`JsonUtility`、NUnit Edit Mode、Markdown 对接说明。

**Spec:** `docs/superpowers/specs/2026-10-09-mvp-numbers-design.md`

---

## File map

| 文件 | 职责 |
|---|---|
| `Assets/Data/Organs/organs.json` | 图鉴数字位（增补钢管等） |
| `Assets/Data/Waves/waves.json` | 7 波时长 / 掉落 / 髓质 |
| `Assets/Data/Combat/mvp_baseline.json` | 玩家开局 + 敌人血攻移速（D 产出、C 对照） |
| `Assets/Scripts/Systems/Editor/ProgramDTests.cs` | 校验加载结果与跑通 7 波髓质 |
| `程序D-对接说明.md` | 同步文件清单与 Demo 数值指针 |
| （只读）`Assets/Scripts/Systems/Catalog.cs` | 校验规则：触发器要 `trigger_*`；`torso_special` 要 `modifiers`+`costs` |
| （只读）`Assets/Scripts/Systems/RunController.cs` | 末波无实验室直接 `run_won`；`optional_drops` 与保底一样**总会发放** |

**注意：** 当前 `CombatTimerFinished` 会把 `guaranteed_drops` **和** `optional_drops` 全部 `Spawn`。规划「1–2 个掉落」时，把该波实际要发的 id 都放进 `guaranteed_drops`，`optional_drops` 用 `[]`，避免误读「可选」。

---

### Task 1: 更新器官加载测试（RED）

**Files:**
- Modify: `Assets/Scripts/Systems/Editor/ProgramDTests.cs`
- Test: same（Unity Edit Mode）

- [ ] **Step 1: 改 `LoadOrgans_IndexesM1Ids` 为覆盖规划器官**

把该方法替换为：

```csharp
    [Test]
    public void LoadOrgans_IndexesMvpIds()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        Assert.AreEqual(11, catalog.Count);
        Assert.AreEqual(5, catalog["fist"].damage);
        Assert.AreEqual(8, catalog["steel_pipe"].damage);
        Assert.AreEqual(4, catalog["rusty_knife"].damage);
        Assert.AreEqual(3, catalog["slime_gland"].damage);
        Assert.AreEqual(0, catalog["collapse_body"].damage);
        Assert.AreEqual(12, catalog["tnt"].damage);
        Assert.AreEqual(3f, catalog["tnt"].radius);
        Assert.AreEqual("kill", catalog["many_limb_tentacle"].trigger_on);
        Assert.AreEqual("NONE", catalog["spider_nest"].trigger_target);
        Assert.AreEqual("torso_special", catalog["collection_net"].type);
        Assert.AreEqual(1.1f, catalog["collection_net"].MarrowGainMult, 0.001f);
        Assert.IsFalse(catalog.ContainsKey("slime_core"));
    }
```

- [ ] **Step 2: 在 Unity 跑该测试，确认失败**

Run: Unity Edit Mode → `ProgramDTests.LoadOrgans_IndexesMvpIds`  
Expected: FAIL（缺少 `steel_pipe` 等 id，或 `Count != 11`）

- [ ] **Step 3: Commit 测试（允许红）**

```bash
git add Assets/Scripts/Systems/Editor/ProgramDTests.cs
git commit -m "test(d): expect MVP organ catalog ids and damages"
```

---

### Task 2: 写入 `organs.json`（GREEN）

**Files:**
- Modify: `Assets/Data/Organs/organs.json`

- [ ] **Step 1: 用下列完整内容覆盖 `organs.json`**

说明：icon 一律 `placeholder/organs/<id>.png`；史莱姆核心不入库；蜘蛛巢 `part` 取 `hand`（Catalog 必填，设计原文「待定」不改策划案，只选可加载部位）。

```json
{
  "version": 1,
  "organs": [
    {
      "id": "fist",
      "name": "拳头",
      "description": "初始器官。按躯干节拍挥出一拳。",
      "part": "hand",
      "type": "executor",
      "rarity": "common",
      "shop_price": 0,
      "icon": "placeholder/organs/fist.png",
      "damage": 5,
      "count": 1,
      "radius": 0
    },
    {
      "id": "steel_pipe",
      "name": "钢管",
      "description": "伤害 8，20% 概率眩晕。",
      "part": "hand",
      "type": "executor",
      "rarity": "common",
      "shop_price": 10,
      "icon": "placeholder/organs/steel_pipe.png",
      "damage": 8,
      "count": 1,
      "radius": 0,
      "chance": 20,
      "status": {
        "id": "stun",
        "duration": 1,
        "max_stack": 1
      }
    },
    {
      "id": "rusty_knife",
      "name": "锈刀",
      "description": "造成伤害时有概率施加流血。",
      "part": "hand",
      "type": "executor",
      "rarity": "common",
      "shop_price": 8,
      "icon": "placeholder/organs/rusty_knife.png",
      "damage": 4,
      "count": 1,
      "radius": 0,
      "chance": 30,
      "status": {
        "id": "bleed",
        "duration": 3,
        "max_stack": 3
      }
    },
    {
      "id": "slime_gland",
      "name": "粘液腺",
      "description": "造成伤害时降低敌人 15% 移速，可叠 3 层。",
      "part": "hand",
      "type": "executor",
      "rarity": "common",
      "shop_price": 10,
      "icon": "placeholder/organs/slime_gland.png",
      "damage": 3,
      "count": 1,
      "radius": 0,
      "status": {
        "id": "slow",
        "duration": 3,
        "max_stack": 3
      }
    },
    {
      "id": "many_limb_tentacle",
      "name": "多肢触手",
      "description": "击杀时重新触发同部位全部槽位。",
      "part": "hand",
      "type": "trigger",
      "rarity": "rare",
      "shop_price": 15,
      "icon": "placeholder/organs/many_limb_tentacle.png",
      "trigger_on": "kill",
      "trigger_target": "SELF_SLOTS",
      "chance": 100
    },
    {
      "id": "collapse_body",
      "name": "坍缩体",
      "description": "攻击变为生成持续 5 秒、可吸附敌人的黑洞。",
      "part": "hand",
      "type": "executor",
      "rarity": "rare",
      "shop_price": 20,
      "icon": "placeholder/organs/collapse_body.png",
      "damage": 0,
      "count": 1,
      "radius": 2,
      "status": {
        "id": "collapse",
        "duration": 5,
        "max_stack": 1
      }
    },
    {
      "id": "tnt",
      "name": "TNT",
      "description": "攻击变为大范围爆炸。",
      "part": "hand",
      "type": "executor",
      "rarity": "rare",
      "shop_price": 22,
      "icon": "placeholder/organs/tnt.png",
      "damage": 12,
      "count": 1,
      "radius": 3
    },
    {
      "id": "spider_nest",
      "name": "蜘蛛巢",
      "description": "造成伤害时 5% 概率召唤友方蜘蛛。",
      "part": "hand",
      "type": "trigger",
      "rarity": "rare",
      "shop_price": 18,
      "icon": "placeholder/organs/spider_nest.png",
      "trigger_on": "hit",
      "trigger_target": "NONE",
      "chance": 5
    },
    {
      "id": "compound_eye",
      "name": "复眼",
      "description": "暴击率 ×3，可见范围缩小约 30%。",
      "part": "torso",
      "type": "torso_special",
      "rarity": "rare",
      "shop_price": 20,
      "icon": "placeholder/organs/compound_eye.png",
      "modifiers": {
        "unequipped_capacity_delta": 0,
        "marrow_gain_mult": 1
      },
      "costs": ["view_radius"]
    },
    {
      "id": "treads",
      "name": "履带",
      "description": "大幅提升直线冲锋速度、撞飞直线上敌人，但转向变慢。",
      "part": "torso",
      "type": "torso_special",
      "rarity": "rare",
      "shop_price": 20,
      "icon": "placeholder/organs/treads.png",
      "modifiers": {
        "unequipped_capacity_delta": 0,
        "marrow_gain_mult": 1
      },
      "costs": ["turn_speed"]
    },
    {
      "id": "collection_net",
      "name": "收集网",
      "description": "每波结算的髓质 +10%，移速 −10%。",
      "part": "torso",
      "type": "torso_special",
      "rarity": "mechanical",
      "shop_price": 25,
      "icon": "placeholder/organs/collection_net.png",
      "modifiers": {
        "unequipped_capacity_delta": 0,
        "marrow_gain_mult": 1.1
      },
      "costs": ["move_speed"]
    }
  ]
}
```

- [ ] **Step 2: 再跑 `LoadOrgans_IndexesMvpIds`**

Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add Assets/Data/Organs/organs.json
git commit -m "data(d): fill MVP organ catalog numbers for demo"
```

---

### Task 3: 更新波次 / 流程测试（RED）

**Files:**
- Modify: `Assets/Scripts/Systems/Editor/ProgramDTests.cs`

- [ ] **Step 1: 替换波次与髓质相关测试**

将 `LoadWaves_KeepsOrderAndDrops`、`Wave1Timer_OpensLabWithBothDrops`、`Wave2Timer_WinsWithoutSecondLab` 替换为：

```csharp
    [Test]
    public void LoadWaves_HasSevenEntries()
    {
        var catalog = Catalog.LoadOrgans(File.ReadAllText(OrgansPath));
        var waves = Catalog.LoadWaves(File.ReadAllText(WavesPath), catalog);
        Assert.AreEqual(7, waves.Count);
        Assert.AreEqual(45, waves[0].marrow_reward);
        Assert.AreEqual("many_limb_tentacle", waves[0].guaranteed_drops[0]);
        Assert.AreEqual("rusty_knife", waves[0].guaranteed_drops[1]);
        Assert.AreEqual(0, waves[0].optional_drops.Length);
        Assert.AreEqual(120, waves[6].marrow_reward);
    }

    [Test]
    public void Wave1Timer_OpensLabWithBothDrops()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        run.CombatTimerFinished();
        Assert.AreEqual("lab", run.Phase);
        Assert.AreEqual(45, run.Marrow);
        CollectionAssert.AreEquivalent(
            new[] { "many_limb_tentacle", "rusty_knife" },
            BagOrganIds(run));
        Assert.AreEqual("WaveEnded", rec.Events[rec.Events.Count - 2].Name);
        Assert.AreEqual("LabOpened", rec.Events[rec.Events.Count - 1].Name);
        var lab = (LabOpenedPayload)rec.Events[rec.Events.Count - 1].Payload;
        Assert.AreEqual(45, lab.MarrowDelta);
        Assert.AreEqual(2, lab.Drops.Count);
    }

    [Test]
    public void Wave2Timer_OpensLabAgain()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        run.CombatTimerFinished();
        run.StartNextWave();
        run.CombatTimerFinished();
        Assert.AreEqual("lab", run.Phase);
        Assert.AreEqual(100, run.Marrow);
        var labCount = 0;
        foreach (var n in rec.Names)
            if (n == "LabOpened") labCount++;
        Assert.AreEqual(2, labCount);
    }

    [Test]
    public void Wave7Timer_WinsWithoutLab()
    {
        var rec = new Recorder();
        var run = NewRun(rec);
        run.StartRun();
        for (var w = 1; w <= 7; w++)
        {
            run.CombatTimerFinished();
            if (w < 7)
            {
                while (!run.CanStartNextWave())
                {
                    var bag = run.Inventory.UnequippedIds();
                    Assert.Greater(bag.Count, 0);
                    run.Sell(bag[0]);
                }
                run.StartNextWave();
            }
        }
        Assert.AreEqual("run_won", run.Phase);
        Assert.AreEqual("RunEnded", rec.Names[rec.Names.Count - 1]);
        var labCount = 0;
        foreach (var n in rec.Names)
            if (n == "LabOpened") labCount++;
        Assert.AreEqual(6, labCount);
    }
```

保留 `SellRustyKnife_GivesFour`（`shop_price` 仍为 8 → 卖出 4）。

- [ ] **Step 2: 跑上述测试，确认失败**

Expected: FAIL（`waves.Count` 仍为 2，髓质仍为 10）

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Systems/Editor/ProgramDTests.cs
git commit -m "test(d): expect seven waves and MVP marrow rewards"
```

---

### Task 4: 写入 `waves.json`（GREEN）

**Files:**
- Modify: `Assets/Data/Waves/waves.json`

- [ ] **Step 1: 用下列完整内容覆盖 `waves.json`**

```json
{
  "waves": [
    {
      "wave": 1,
      "duration_sec": 60,
      "guaranteed_drops": ["many_limb_tentacle", "rusty_knife"],
      "optional_drops": [],
      "marrow_reward": 45
    },
    {
      "wave": 2,
      "duration_sec": 60,
      "guaranteed_drops": ["steel_pipe"],
      "optional_drops": [],
      "marrow_reward": 55
    },
    {
      "wave": 3,
      "duration_sec": 60,
      "guaranteed_drops": ["slime_gland"],
      "optional_drops": [],
      "marrow_reward": 65
    },
    {
      "wave": 4,
      "duration_sec": 60,
      "guaranteed_drops": ["tnt"],
      "optional_drops": [],
      "marrow_reward": 80
    },
    {
      "wave": 5,
      "duration_sec": 60,
      "guaranteed_drops": ["collapse_body"],
      "optional_drops": [],
      "marrow_reward": 90
    },
    {
      "wave": 6,
      "duration_sec": 60,
      "guaranteed_drops": ["spider_nest"],
      "optional_drops": [],
      "marrow_reward": 100
    },
    {
      "wave": 7,
      "duration_sec": 60,
      "guaranteed_drops": [],
      "optional_drops": [],
      "marrow_reward": 120
    }
  ]
}
```

- [ ] **Step 2: 跑 `LoadWaves_HasSevenEntries`、`Wave1Timer_OpensLabWithBothDrops`、`Wave2Timer_OpensLabAgain`、`Wave7Timer_WinsWithoutLab`、`OverCapacity_BlocksNextWave`**

Expected: 全部 PASS  
（`OverCapacity`：波 1 掉 2 个进背包，再 Spawn 4 个拳头 → 未装备 6 > 5，仍应挡住下一波。）

- [ ] **Step 3: Commit**

```bash
git add Assets/Data/Waves/waves.json
git commit -m "data(d): expand waves to seven with MVP marrow and drops"
```

---

### Task 5: 玩家 / 敌人基准 JSON（给 C 对照）

**Files:**
- Create: `Assets/Data/Combat/mvp_baseline.json`
- Create: `Assets/Data/Combat/.gitkeep`（若目录需占位；有 json 则可不建）

- [ ] **Step 1: 写入 `mvp_baseline.json`**

```json
{
  "version": 1,
  "note": "Program D MVP numbers for C alignment. Fill-table damage: enemy hit = max(1, Event.damage); player hit = max(1, attackPower). No multiplier zones in planning.",
  "player": {
    "max_health": 100,
    "base_cooldown": 1.0,
    "move_speed": 5.0,
    "armor": 0
  },
  "enemy_baseline": {
    "move_speed": 2.5,
    "health": 20,
    "attack_power": 5,
    "attack_cooldown": 1.5,
    "ranged_range": 6,
    "ranged_projectile_speed": 4
  },
  "enemies": [
    {
      "id": "enemy_tidewalker",
      "display_name": "潮行仆",
      "greybox": "Normal",
      "health": 20,
      "attack_power": 5,
      "move_speed": 2.5,
      "drop_table_id": "drop_tidewalker"
    },
    {
      "id": "enemy_skitter",
      "display_name": "掠潮迅兽",
      "greybox": "Fast",
      "health": 14,
      "attack_power": 5,
      "move_speed": 3.25,
      "drop_table_id": "drop_skitter"
    },
    {
      "id": "enemy_saltgaze",
      "display_name": "盐雾凝视者",
      "greybox": "RangedKeeper",
      "health": 20,
      "attack_power": 5,
      "move_speed": 2.0,
      "drop_table_id": "drop_saltgaze"
    },
    {
      "id": "enemy_broodnurse",
      "display_name": "腐潮哺育者",
      "greybox": "Medic",
      "health": 16,
      "attack_power": 0,
      "move_speed": 2.75,
      "heal_interval": 3,
      "heal_radius": 5,
      "heal_percent": 0.2,
      "drop_table_id": "drop_broodnurse"
    },
    {
      "id": "enemy_boss_deepone",
      "display_name": "深渊初醒",
      "greybox": "BossPlaceholder",
      "health": 200,
      "attack_power": 10,
      "move_speed": 2.5,
      "drop_table_id": "drop_boss"
    }
  ],
  "wave_strength_mult": [1.0, 1.1, 1.2, 1.35, 1.5, 1.7, 1.7]
}
```

- [ ] **Step 2: 不写 C# 加载器**（YAGNI；C 可手抄或日后接）

- [ ] **Step 3: Commit**

```bash
git add Assets/Data/Combat/mvp_baseline.json
git commit -m "data(d): add MVP player and enemy baseline for C"
```

---

### Task 6: 更新对接说明 + 全量测试

**Files:**
- Modify: `程序D-对接说明.md`

- [ ] **Step 1: 改 §1 文件表**

将器官/波次两行改为：

```markdown
| `Assets/Data/Organs/organs.json` | MVP 图鉴：11 个器官数字位（史莱姆核心暂不入库） |
| `Assets/Data/Waves/waves.json` | 7 波；波 1 保底触手+锈刀；髓质按 MVP 规划 |
| `Assets/Data/Combat/mvp_baseline.json` | 玩家/敌人基准血攻（给 C 对照，D 不加载） |
| `docs/superpowers/specs/2026-10-09-mvp-numbers-design.md` | 数值规划原文 |
```

- [ ] **Step 2: 在文档末尾追加一节（若已有「变更」则合并）**

```markdown
## MVP 数值（2026-10-09）

- 填表口径：对敌 `max(1, Event.damage)`；对己 `max(1, attackPower)`；规划期不按乘区估数。
- 波 1 髓质 45；全 7 波髓质 45/55/65/80/90/100/120。
- 敌人与玩家开局数见 `Assets/Data/Combat/mvp_baseline.json`。
- Demo 后再调数字；不借机改器官效果设计。
```

- [ ] **Step 3: 跑全部 `ProgramDTests`**

Expected: 全部 PASS

- [ ] **Step 4: Commit**

```bash
git add 程序D-对接说明.md
git commit -m "docs(d): point integration guide at MVP numbers"
```

---

## Spec coverage checklist

| Spec 项 | Task |
|---|---|
| 填表伤害口径（文档化） | Task 5 note + Task 6 |
| 玩家 100 / 1.0s / 5.0 / armor 0 | Task 5 |
| 4+1 敌人血攻移速 | Task 5 |
| 12 器官数字（史莱姆 TBD） | Task 2（11 入库） |
| 7 波构成与髓质 | Task 4 |
| 波 1 保底一对 | Task 4 |
| 不改设计文案 / 不改结算代码 | 全任务只碰数据与测试/说明 |
| Demo 后可调 | Task 6 注明 |

## Out of scope（本计划不做）

- 修改 `EnemyArchetypeConfig.cs` 或玩家战斗脚本（归 C）
- 实现暴击/流血/眩晕结算（归 A）
- 商店开放、`TryBuy`、躯干升级树
- 波次强度倍率自动乘到刷怪（C 读 `wave_strength_mult` 自行用）
- 推远程 / 开 PR（除非用户另嘱）
