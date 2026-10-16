using System;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using BodyPart = SpringUp.Organs.BodyPart;
using OrganInstance = SpringUp.Organs.OrganInstance;

namespace SpringUp.EditorChecks
{
    [InitializeOnLoad]
    public static class IntegratedFlowChecks
    {
        private const string Key = "SpringUp.Flow.Checks";
        private static double nextCheck;
        private static int stage;
        private static float previewHealth;
        private static Keyboard keyboard;
        private static Vector3 movementStart;
        private static EnemyRuntime chaseTarget;
        private static float chaseHealth;
        static IntegratedFlowChecks()
        {
            if (SessionState.GetBool(Key, false)) EditorApplication.playModeStateChanged += StateChanged;
        }
        public static void RunBatch()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("只在独立 batchmode 副本运行。");
            if (!System.IO.File.Exists(SpringUpDemoBuilder.ScenePath)) SpringUpDemoBuilder.Build();
            else EditorSceneManager.OpenScene(SpringUpDemoBuilder.ScenePath);
            SessionState.SetBool(Key, true);
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.EnterPlaymode();
        }
        private static void StateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            stage = -3; nextCheck = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update += Check;
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("[Integrated Flow Checks] " + message); }
        private static void Check()
        {
            if (EditorApplication.timeSinceStartup < nextCheck) return;
            try
            {
                var driver = Object.FindFirstObjectByType<SpringUpRunDriver>();
                Require(driver != null && driver.Run != null, "统一入口启动。");
                var run = driver.Run;
                if (stage == -3)
                {
                    CheckCadence(driver);
                    // Hidden batch editors have no focused Game View.
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    Application.runInBackground = true;
                    Require(!driver.capacityText.transform.parent.gameObject.activeSelf, "Combat must hide settlement background.");
                    CheckEquipmentHud(driver, true);
                    Require(!driver.Equipment.TryUnequip(LaboratoryPart.Hand, 0, out _)
                        && run.Inventory.EquippedAt("hand", 1) != null, "战斗期间禁止拆卸。");
                    Require(driver.battleCamera.rect.yMax < 1f, "Combat viewport must reserve HUD space.");
                    movementStart = driver.player.transform.position;
                    keyboard = InputSystem.AddDevice<Keyboard>();
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.D));
                    stage = -2; nextCheck = EditorApplication.timeSinceStartup + .5; return;
                }
                if (stage == -2)
                {
                    Debug.Log("[Playtest Input] input=" + driver.player.GetComponent<PlayerMotor>().MoveInput
                        + " position=" + driver.player.transform.position + " start=" + movementStart);
                    Require(driver.player.transform.position.x > movementStart.x + .2f, "Real keyboard input must move the player.");
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    InputSystem.RemoveDevice(keyboard); keyboard = null;
                    EnemyRuntime.ClearBattlefield();
                    driver.pipeline.enabled = false;
                    var go = Object.Instantiate(Resources.Load<GameObject>("Enemies/Normal"),
                        driver.player.transform.position + Vector3.right * 6f, Quaternion.identity);
                    chaseTarget = go.GetComponent<EnemyRuntime>();
                    chaseHealth = driver.player.CurrentHealth;
                    stage = -1; nextCheck = EditorApplication.timeSinceStartup + 4; return;
                }
                if (stage == -1)
                {
                    Require(chaseTarget != null && Vector2.Distance(chaseTarget.Position, driver.player.transform.position) < 1.3f,
                        "Melee enemy must naturally chase past the former 5.4m stop distance.");
                    Require(driver.player.CurrentHealth < chaseHealth, "Natural approach must cause contact damage through solid colliders.");
                    EnemyRuntime.ClearBattlefield();
                    driver.player.ResetHealthToMax();
                    driver.pipeline.enabled = true;
                    stage = 0;
                }
                if (stage == 0)
                {
                    Require(run.Phase == "combat" && run.Wave == 1 && driver.director.CurrentWave == 1, "第一波只启动一次。");
                    Require(Object.FindFirstObjectByType<BattleRunDriver>() == null, "无灰盒流程重复接管。");
                    var go = new GameObject("Flow target"); go.SetActive(false);
                    go.transform.position = driver.player.transform.position + Vector3.right;
                    go.AddComponent<EnemyData>(); var enemy = go.AddComponent<EnemyRuntime>(); go.SetActive(true);
                    float before = enemy.CurrentHealth;
                    Require(!driver.pipeline.AutomaticExecution && driver.ChargeAttack.TryAttack(), "统一入口使用主动充能攻击。");
                    Require(enemy.CurrentHealth < before && driver.ChargeAttack.Charge < driver.ChargeAttack.Capacity,
                        "开局拳头主动攻击实际扣敌人血并消耗充能。");
                    driver.director.CompleteCurrentWave();
                    Require(run.Phase == "lab" && run.Marrow >= 427 && run.Marrow <= 472 && Time.timeScale == 0, "第一波奖励与暂停。");
                    Require(!driver.ChargeAttack.TryAttack(), "休息阶段禁止实战攻击。");
                    CheckEquipmentHud(driver, false);
                    var bag = driver.Equipment.Inventory;
                    var knife = bag.Single(x => x.DefinitionId == "rust_knife");
                    var tentacle = bag.Single(x => x.DefinitionId == "multi_tentacle");
                    driver.presenter.SelectInventory(knife.InstanceId); driver.presenter.SelectSlot(1, 1);
                    driver.presenter.installButton.onClick.Invoke();
                    Require(run.Inventory.EquippedAt("hand", 2) == knife.InstanceId, "B 安装按钮写回 D 库存。");
                    Require(driver.Equipment.TryEquip(tentacle.InstanceId, LaboratoryPart.Hand, 2, out _), "装备触手。");
                    CheckEquipmentHud(driver, false);
                    Require(driver.pipeline.GetBody(BodyPart.Hand).GetSlot(1).InstanceId == knife.InstanceId, "A 保留 D 实例身份。");
                    Require(run.Inventory.OrganOf(knife.InstanceId).damage == knife.Definition.Damage, "D 战斗字段与 A/B 定义一致。");
                    var preview = driver.presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                    Require(Mathf.Approximately(preview.Pipeline.GetBody(BodyPart.Hand).StepInterval, .15f)
                        && Mathf.Approximately(preview.Pipeline.GetBody(BodyPart.Hand).CycleDuration, 2f), "Shared preview timing.");
                    Require(ReferenceEquals(preview.Pipeline.GetBody(BodyPart.Hand).GetSlot(1).Definition,
                        driver.pipeline.GetBody(BodyPart.Hand).GetSlot(1).Definition), "预览与实战共用战斗定义。");
                    previewHealth = preview.targets[0].CurrentHealth;
                    stage = 1; nextCheck = EditorApplication.timeSinceStartup + 2.2; return;
                }
                if (stage == 1)
                {
                    var preview = driver.presenter.GetComponentInChildren<LaboratoryCombatDemo>();
                    Require(preview.targets[0].CurrentHealth < previewHealth, "暂停战场时预览继续运行。");
                    int total = run.Inventory.Instances.Count;
                    var knifeId = run.Inventory.EquippedAt("hand", 2);
                    Require(driver.Equipment.TryUnequip(LaboratoryPart.Hand, 1, out _), "卸下。");
                    Require(driver.Equipment.TryEquip(knifeId, LaboratoryPart.Hand, 0, out _), "替换拳头。");
                    Require(run.Inventory.Instances.Count == total && run.Inventory.UnequippedIds().Count == 1, "替换事务守恒。");
                    string fistId = run.Inventory.UnequippedIds().Single();
                    Require(!driver.Equipment.TryEquip(fistId, LaboratoryPart.Head, 0, out _)
                        && run.Inventory.EquippedAt("hand", 1) == knifeId, "非法替换不改变库存。");
                    Require(driver.Equipment.TryEquip(fistId, LaboratoryPart.Hand, 1, out _), "装回拳头。");
                    while (run.Inventory.UnequippedIds().Count <= run.Inventory.UnequippedCapacity()) run.Inventory.Spawn("fist");
                    driver.Equipment.Refresh(); driver.NextWave();
                    Require(run.Phase == "lab" && driver.director.CurrentWave == 1, "超容量阻止下一波。");
                    while (!run.CanStartNextWave()) driver.Equipment.TryDiscard(run.Inventory.UnequippedIds()[0], out _);
                    driver.NextWave(); driver.NextWave();
                    Require(run.Wave == 2 && driver.director.CurrentWave == 2 && Time.timeScale == 1, "第二波与重复点击保护。");
                    Require(driver.ChargeAttack.Charge == driver.ChargeAttack.Capacity
                        && driver.ChargeAttack.RequiredCharge == driver.Definitions["rust_knife"].ChargeCost + driver.Definitions["fist"].ChargeCost,
                        "下一波充满且按实际配装更新整轮消耗，触发器不额外收费。");
                    CheckEquipmentHud(driver, true);
                    var equippedBefore = run.Inventory.EquippedAt("hand", 1);
                    var blockedCandidate = run.Inventory.Spawn("fist");
                    Require(!driver.Equipment.TryEquip(blockedCandidate, LaboratoryPart.Hand, 0, out _)
                        && !driver.Equipment.TryUnequip(LaboratoryPart.Hand, 0, out _)
                        && run.Inventory.EquippedAt("hand", 1) == equippedBefore
                        && run.Inventory.UnequippedIds().Contains(blockedCandidate), "新一波禁止替换和拆卸且保持库存。");
                    var enemyGo = new GameObject("Second wave target"); enemyGo.SetActive(false);
                    enemyGo.transform.position = driver.player.transform.position;
                    enemyGo.AddComponent<EnemyData>(); var enemy = enemyGo.AddComponent<EnemyRuntime>(); enemyGo.SetActive(true);
                    float hp = enemy.CurrentHealth;
                    driver.pipeline.GetBody(BodyPart.Hand).ExecuteAllSlots((_, organ) =>
                    {
                        if (ParameterisedBehaviour.TryCreateAttack(organ, enemy.TargetId, out var attack, effectCenter: enemy.Position))
                            driver.combat.Resolve(attack);
                    });
                    Require(enemy.CurrentHealth < hp, "第二波装备实际造成伤害。");
                    for (int wave = 2; wave <= 7; wave++)
                    {
                        driver.director.CompleteCurrentWave();
                        int marrow = run.Marrow;
                        driver.director.CompleteCurrentWave();
                        Require(run.Marrow == marrow, "重复结算不重复发奖励。");
                        if (wave < 7)
                        {
                            while (!run.CanStartNextWave()) driver.Equipment.TrySell(run.Inventory.UnequippedIds()[0], out _);
                            driver.NextWave();
                        }
                    }
                    Require(run.Phase == "run_won" && run.Wave == 7 && !driver.laboratoryRoot.activeSelf, "连续七波进入通关。");
                    driver.RestartRun(); stage = 2; nextCheck = EditorApplication.timeSinceStartup + 1; return;
                }
                if (stage == 2)
                {
                    Require(run.Phase == "combat" && run.Wave == 1 && run.Marrow == 0 && run.Inventory.Instances.Count == 1,
                        "重开清除库存、奖励与波次残留。");
                    Require(driver.combat.ActiveBlackHoles == 0 && driver.player.CurrentHealth == driver.player.MaxHealth, "重开恢复生命并清理效果。");
                    driver.player.TakeDamage(driver.player.MaxHealth);
                    Require(run.Phase == "run_lost" && Time.timeScale == 0 && !driver.pipeline.enabled, "玩家死亡进入失败并停止战斗。");
                    driver.director.CompleteCurrentWave();
                    Require(run.Phase == "run_lost" && run.Marrow == 0, "死亡后不能结算奖励。");
                    Debug.Log("[Integrated Flow Checks] PLAY PASS: first wave damage, lab UI, shared inventory/config, paused preview, second wave, capacity, seven waves, restart, defeat.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }
        private static void Finish(int code)
        {
            if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Check;
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.Exit(code);
        }

        private static void CheckEquipmentHud(SpringUpRunDriver driver, bool visible)
        {
            var hud = driver.runStatus.canvas.GetComponentInChildren<RunEquipmentHud>(true);
            Require(hud != null && hud.gameObject.activeSelf == visible, "战斗显示装备 HUD，休息交由实验室显示。");
            Require(hud.GetComponentsInChildren<Selectable>(true).Length == 0
                && hud.GetComponentsInChildren<Graphic>(true).All(g => !g.raycastTarget), "装备 HUD 只读且不拦截输入。");
            string[] parts = { "head", "hand", "leg", "torso" };
            for (int p = 0; p < parts.Length; p++)
                for (int s = 0; s < driver.Equipment.SlotCount((LaboratoryPart)p); s++)
                {
                    var cell = hud.transform.Find(parts[p] + " slot " + (s + 1));
                    var item = driver.Equipment.GetSlot((LaboratoryPart)p, s);
                    Require(cell != null && cell.GetComponentInChildren<Text>().text == (s + 1) + "\n" + (item?.Name ?? "空"),
                        "HUD 槽位与真实装备同步：" + parts[p] + (s + 1));
                }
        }

        private static void CheckCadence(SpringUpRunDriver driver)
        {
            Require(Mathf.Approximately(driver.player.GetStepInterval(1), .15f)
                && Mathf.Approximately(driver.player.GetCycleDuration(1), 2f), "Scene timing must be 0.15 / 2.");
            var body = new BodyRuntime(BodyPart.Hand, .15f) { CycleDuration = 2f };
            for (int i = 0; i < 6; i++) body.SetSlot(i, new OrganInstance(driver.Definitions["fist"]));
            body.Tick(.14f); Require(body.ExecutedStepCount == 0, "Initial delay.");
            body.Tick(.01f); Require(body.ExecutedStepCount == 1, "First slot at .15.");
            body.Tick(.75f); Require(body.ExecutedStepCount == 6, "Six slots spaced .15.");
            body.Tick(1.24f); Require(body.ExecutedStepCount == 6, "No early wrap.");
            body.Tick(.01f); Require(body.ExecutedStepCount == 7, "Next round begins two seconds after first.");
            int count = body.ExecutedStepCount;
            body.ExecuteAllSlots((_, __) => { });
            Require(body.ExecutedStepCount == count, "Triggered scan must not move normal clock.");
            body.CycleDuration = .05f; body.ResetTiming();
            count = body.ExecutedStepCount;
            body.Tick(1.05f); Require(body.ExecutedStepCount == count + 7, "No overlapping rounds at extreme speed.");
            var single = new BodyRuntime(BodyPart.Hand, .15f) { CycleDuration = 2 };
            single.SetSlot(5, new OrganInstance(driver.Definitions["fist"]));
            single.Tick(2.15f); Require(single.ExecutedStepCount == 2, "Sparse and single slot cycle.");
            single.SetSlot(5, null); single.Tick(100);
            single.SetSlot(0, new OrganInstance(driver.Definitions["fist"]));
            single.Tick(.14f); Require(single.ExecutedStepCount == 2, "Empty body cannot bank time.");
            var stats = new SerializedObject(driver.player.Stats);
            stats.FindProperty("baseCooldown").floatValue = .3f;
            stats.FindProperty("baseCycleDuration").floatValue = 4f;
            stats.ApplyModifiedPropertiesWithoutUndo(); driver.player.Stats.InvalidateModifiers();
            Require(Mathf.Approximately(driver.player.GetStepInterval(1), .3f)
                && Mathf.Approximately(driver.player.GetCycleDuration(1), 4f), "Invalidated timing values must refresh.");
            stats.FindProperty("baseCooldown").floatValue = .15f;
            stats.FindProperty("baseCycleDuration").floatValue = 2f;
            stats.ApplyModifiedPropertiesWithoutUndo(); driver.player.Stats.InvalidateModifiers();
            Debug.Log("[Playtest Regression] EDIT PASS: cadence, sparse slots, empty reset, overlap guard, stat invalidation.");
        }
    }
}
