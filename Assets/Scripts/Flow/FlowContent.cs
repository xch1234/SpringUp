using System;
using System.Collections.Generic;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpringUp.Flow
{
    public enum ContentKind { MainMenu, Battle, Rest }
    // Serialized scene entry; bootstrap locates this only among the loaded scene's roots.
    [DefaultExecutionOrder(-100)]
    public sealed class FlowContent : MonoBehaviour
    {
        public ContentKind kind;
        public GameObject world;
        public PlayerRuntime player;
        public WaveDirector director;
        public OrganPipelineController pipeline;
        public BattleOrganCombat combat;
        public BattleOrganEffects effects;
        public EnemyRuntime dummy;
        private readonly List<EnemyRuntime> trainingDummies = new List<EnemyRuntime>();
        public TorsoEquipmentRuntime Torso { get; private set; }
        public OrganCombatReport CombatReport { get; private set; }
        public RunLaboratoryEquipment Equipment { get; private set; }
        RunEquipmentHud equipmentHud;
        public RectTransform EquipmentHudRect => equipmentHud != null ? (RectTransform)equipmentHud.transform : null;
        Camera battleCamera;
        BattleMapLayout battleMap;
        [Tooltip("休整区与战斗共用的基础视野配置。")]
        public BattleMapSettings viewSettings;
        float baseCameraSize;
        [SerializeField] Transform merchant;
        public Transform Merchant => merchant;
        [SerializeField] Transform laboratory;
        public Transform Laboratory => laboratory;
        [SerializeField, Min(.1f)] float interactionRadius = 2f;
        public float InteractionRadius => interactionRadius;
        RestInteractionSettings interactionSettings;
        public RestInteractionSettings InteractionSettings => interactionSettings != null ? interactionSettings
            : interactionSettings = Resources.Load<RestInteractionSettings>("RestInteractionSettings");
        // Normalized positions on the ship sprite, measured from its lower-left corner.
        // Stand on the deck immediately to the left of each painted counter.
        [SerializeField] Vector2 shopAnchor = new Vector2(.645f, .60f);
        [SerializeField] Vector2 laboratoryAnchor = new Vector2(.645f, .38f);
        FlowApplication app;
        RunSession session;
        KillMarrowBinding killMarrow;
        bool initialized;
        public void Initialize(FlowApplication owner, RunSession current)
        {
            app = owner; session = current;
            if (kind == ContentKind.MainMenu) { initialized = true; return; }
            if (session == null || player == null || pipeline == null)
                throw new InvalidOperationException("Incomplete content dependencies");
            pipeline.AutomaticExecution = false;
            // FlowApplication owns the single persistent listener, including loading screens.
            foreach (var listener in world.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            world.SetActive(true);
            PlayerLocator.Bind(player.transform);
            player.GetComponent<PlayerMotor>().InputAllowed = () => app.WorldInputAllowed;
            player.SetHealthRegeneration(kind == ContentKind.Battle);
            pipeline.StepIntervalProvider = part => player.GetStepInterval((int)part);
            pipeline.CycleDurationProvider = part => player.GetCycleDuration((int)part);
            pipeline.GetComponent<BattleOrganTargeting>().AttackOrigin = player.transform;
            Torso = player.gameObject.AddComponent<TorsoEquipmentRuntime>(); Torso.Configure(player);
            combat.Torso = Torso;
            CombatReport = pipeline.gameObject.AddComponent<OrganCombatReport>();
            CombatReport.Bind(pipeline); CombatReport.SetNames(app.Configuration.Definitions.Values);
            combat.Report = CombatReport;
            Equipment = new RunLaboratoryEquipment(session.Run, app.Configuration.Definitions);
            equipmentHud = RunEquipmentHud.Create(app.uiRoot, app.hud.font);
            equipmentHud.Bind(Equipment); equipmentHud.BindReport(CombatReport); equipmentHud.BindPipeline(pipeline);
            battleCamera = world.GetComponentInChildren<Camera>(true);
            battleMap = kind == ContentKind.Battle ? world.GetComponent<BattleMapLayout>() : null;
            if (battleCamera != null) baseCameraSize = battleCamera.orthographicSize;
            if (kind == ContentKind.Rest) EnsureMerchant();
            initialized = true;
            if (kind == ContentKind.Battle) killMarrow = new KillMarrowBinding(session.Run, app.ShowKillReward);
            session.Run.LoadoutChanged += LoadoutChanged;
            player.Died += Died;
            if (director != null) director.WaveCompleted += Completed;
            ApplyLoadout();
            // Apply max-health modifiers before restoring a snapshot from the previous scene.
            if (session.Health.HasValue) player.RestoreHealth(session.Health.Value);
            else player.ResetHealthToMax();
            if (dummy != null) { dummy.TrainingTarget = true; dummy.ResetTrainingTarget(); }
            if (kind == ContentKind.Rest) BindTrainingTargets();
        }

        /// <summary>
        /// 休整场景里所有木桩都按训练靶处理（含船头新增的三根）：打不死、可反复试器官。
        /// 只找场景里已激活的实例，不会影响战斗场景的敌人。
        /// </summary>
        private void BindTrainingTargets()
        {
            trainingDummies.Clear();
            var dummies = FindObjectsByType<EnemyRuntime>(FindObjectsInactive.Exclude);
            for (var i = 0; i < dummies.Length; i++)
            {
                var candidate = dummies[i];
                if (candidate == null || candidate.Data == null)
                    continue;

                if (candidate.Data.Definition.archetype != EnemyArchetype.Dummy)
                    continue;

                candidate.TrainingTarget = true;
                candidate.ResetTrainingTarget();
                trainingDummies.Add(candidate);
            }

            // 场景里原来的单根 Training dummy 已被删除，这里把 HUD/状态栏用的引用补到第一根上。
            if (dummy == null && trainingDummies.Count > 0)
                dummy = trainingDummies[0];
        }
        // Kept as the shared builder entry point; both counters follow the ship artwork.
        public void EnsureMerchant()
        {
            if (kind != ContentKind.Rest || world == null) return;
            var ship = world.transform.Find("ShipBackground")?.GetComponent<SpriteRenderer>();
            merchant = ConfigureCounter(merchant, "Merchant · E Shop", "Merchant label", "SHOP [E]", shopAnchor, ship);
            laboratory = ConfigureCounter(laboratory, "Laboratory · E Equipment", "Laboratory label", "LAB [E]", laboratoryAnchor, ship);
        }
        Transform ConfigureCounter(Transform counter, string objectName, string labelName, string text,
            Vector2 anchor, SpriteRenderer ship)
        {
            if (counter == null) counter = world.transform.Find(objectName);
            if (counter == null)
            {
                counter = new GameObject(objectName).transform;
                counter.SetParent(world.transform, false);
            }
            var local = new Vector3((anchor.x - .5f) * 48f, (anchor.y - .5f) * 36f, 0f);
            if (ship != null && ship.sprite != null)
            {
                var bounds = ship.sprite.bounds;
                local = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, anchor.x),
                    Mathf.Lerp(bounds.min.y, bounds.max.y, anchor.y), 0f);
                counter.position = ship.transform.TransformPoint(local);
            }
            else counter.position = world.transform.TransformPoint(local);
            counter.localScale = Vector3.one;
            // The counter is already painted into the background; remove the cyan placeholder visually.
            var placeholder = counter.GetComponent<SpriteRenderer>();
            if (placeholder != null) placeholder.enabled = false;
            var label = counter.Find(labelName)?.GetComponent<TextMesh>();
            if (label == null)
            {
                label = new GameObject(labelName).AddComponent<TextMesh>();
                label.transform.SetParent(counter, false);
            }
            label.transform.localPosition = new Vector3(-.6f, .8f, -.1f);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.text = text; label.characterSize = .12f; label.fontSize = 40;
            label.anchor = TextAnchor.MiddleRight;
            label.gameObject.SetActive(false);
            return counter;
        }
        public bool IsNearCounter(Transform counter)
        {
            if (kind != ContentKind.Rest || player == null || counter == null ||
                !counter.gameObject.activeInHierarchy || !player.gameObject.activeInHierarchy) return false;
            if (counter != merchant && counter != laboratory) return false;
            var ship = world != null ? world.transform.Find("ShipBackground")?.GetComponent<SpriteRenderer>() : null;
            var settings = InteractionSettings;
            if (ship != null && ship.sprite != null && settings != null)
            {
                var point = ship.transform.InverseTransformPoint(player.transform.position);
                var bounds = ship.sprite.bounds;
                var uv = new Vector2((point.x - bounds.min.x) / bounds.size.x, (point.y - bounds.min.y) / bounds.size.y);
                if (ship.flipX) uv.x = 1 - uv.x;
                if (ship.flipY) uv.y = 1 - uv.y;
                return RestInteractionSettings.Contains(counter == merchant ? settings.shopArea : settings.laboratoryArea, uv);
            }
            // Legacy scenes without the ship/config retain their circular proximity check.
            return ((Vector2)(counter.position - player.transform.position)).sqrMagnitude <= interactionRadius * interactionRadius;
        }
        public bool IsInTrainingArea()
        {
            if (kind != ContentKind.Rest || player == null || !player.gameObject.activeInHierarchy) return false;
            var ship = world != null ? world.transform.Find("ShipBackground")?.GetComponent<SpriteRenderer>() : null;
            var settings = InteractionSettings;
            if (ship == null || ship.sprite == null || settings == null) return false;
            var point = ship.transform.InverseTransformPoint(player.transform.position);
            var bounds = ship.sprite.bounds;
            var uv = new Vector2((point.x - bounds.min.x) / bounds.size.x, (point.y - bounds.min.y) / bounds.size.y);
            if (ship.flipX) uv.x = 1 - uv.x;
            if (ship.flipY) uv.y = 1 - uv.y;
            return RestInteractionSettings.Contains(settings.trainingArea, uv);
        }
        void OnDrawGizmosSelected()
        {
            if (kind != ContentKind.Rest || world == null || InteractionSettings == null || !InteractionSettings.showSceneGizmos) return;
            var ship = world.transform.Find("ShipBackground")?.GetComponent<SpriteRenderer>();
            if (ship == null || ship.sprite == null) return;
            DrawCounterArea(ship, InteractionSettings.trainingArea, Color.yellow);
            DrawCounterArea(ship, InteractionSettings.shopArea, Color.green);
            DrawCounterArea(ship, InteractionSettings.laboratoryArea, Color.cyan);
        }
        static void DrawCounterArea(SpriteRenderer ship, Rect area, Color color)
        {
            var bounds = ship.sprite.bounds;
            var corners = new[] { area.min, new Vector2(area.xMax, area.yMin), area.max, new Vector2(area.xMin, area.yMax) };
            var previous = Gizmos.color; Gizmos.color = color;
            Vector3 WorldPoint(Vector2 uv)
            {
                if (ship.flipX) uv.x = 1 - uv.x;
                if (ship.flipY) uv.y = 1 - uv.y;
                return ship.transform.TransformPoint(new Vector3(bounds.min.x + bounds.size.x * uv.x, bounds.min.y + bounds.size.y * uv.y, 0));
            }
            for (int i = 0; i < 4; i++) Gizmos.DrawLine(WorldPoint(corners[i]), WorldPoint(corners[(i + 1) % 4]));
            Gizmos.color = previous;
        }
        void UpdateCounterHint(Transform counter, bool available)
        {
            if (counter == null) return;
            var label = counter.GetComponentInChildren<TextMesh>(true);
            if (label != null) label.text = (counter == merchant ? "SHOP" : "LAB") + " [" + GameSettings.Display(GameBinding.Interact) + "]";
            if (label != null && label.gameObject.activeSelf != available) label.gameObject.SetActive(available);
        }
        void Update()
        {
            if (initialized && pipeline != null) pipeline.AutomaticExecution = app.WorldInputAllowed
                && (kind != ContentKind.Rest || IsInTrainingArea());
            if (initialized && kind == ContentKind.Rest)
            {
                UpdateCounterHint(merchant, app.CanInteractWithShop());
                UpdateCounterHint(laboratory, app.CanInteractWithLaboratory());
                if (GameSettings.Pressed(GameBinding.Interact)) app.TryInteract();
            }
        }
        void LateUpdate()
        {
            if (battleCamera != null && Torso != null)
                battleCamera.orthographicSize = (battleMap != null && battleMap.settings != null
                    ? battleMap.settings.CameraHalfHeight : viewSettings != null ? viewSettings.CameraHalfHeight : baseCameraSize) * Torso.Loadout.View;
        }
        public void ApplyEquipmentHudLayout(EquipmentHudStyle style)
        {
            if (equipmentHud == null) return;
            equipmentHud.ApplyLayout(style);
            equipmentHud.gameObject.SetActive(initialized && style.visible && !app.HasModal && !app.IsTransitioning);
        }
        public void Begin()
        {
            if (kind == ContentKind.Battle)
            {
                // 每个战斗波次开始时满血。跨波次沿用血量会让后面的波次一开局就带伤，
                // 与「每波重新布阵、重新打」的节奏不符；休整期仍然沿用上一波结束时的血量。
                if (player != null) player.ResetHealthToMax();

                if (!director.StartExternalWave(session.Run.Wave, session.Run.MaxWave, session.Run.WaveDuration))
                    throw new InvalidOperationException("Wave did not start");
            }
            if (player != null) PlayerLocator.Bind(player.transform);
        }
        void Completed(int wave, int cleared)
        {
            if (session.Run.Stage != RunPhase.Combat || session.Run.Wave != wave || app.IsTransitioning) return;
            if (player.CurrentHealth <= 0) session.Run.LoseRun();
            else session.Run.CombatTimerFinished();
            app.ResolveRunPhase();
        }
        void Died()
        {
            if (kind == ContentKind.MainMenu || app.IsTransitioning) return;
            session.Run.LoseRun(); app.ResolveRunPhase();
        }
        void LoadoutChanged(InventorySnapshot _) => ApplyLoadout();
        void ApplyLoadout()
        {
            ClearAttacks();
            var torsos = new List<TorsoSettings>();
            for (int slot = 1; slot <= Inventory.SlotCounts["torso"]; slot++)
            {
                var id = session.Run.Inventory.EquippedAt("torso", slot);
                if (id != null && app.Configuration.Definitions.TryGetValue(session.Run.Inventory.OrganOf(id).id, out var definition))
                    torsos.Add(definition.Torso);
            }
            Torso.Apply(torsos, pipeline);
            PipelineLoadout.Apply(pipeline, session.Run.Inventory, app.Configuration.Definitions);
            combat.SpawnEquippedSummons();
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart))) pipeline.GetBody(part).ResetTiming();
            Equipment.Refresh();
        }
        void ClearAttacks()
        {
            pipeline?.ResetTriggers(); combat?.Clear(); effects?.Clear();
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var projectile in root.GetComponentsInChildren<Projectile>(true))
                    Destroy(projectile.gameObject);
        }
        public void ResetDummy()
        {
            if (kind != ContentKind.Rest || !app.WorldInputAllowed) return;
            ClearAttacks();

            if (trainingDummies.Count == 0 && dummy != null)
                trainingDummies.Add(dummy);

            // 三根木桩固定在船头，不再像以前那样把木桩搬到玩家旁边；这里只复位血量。
            for (var i = 0; i < trainingDummies.Count; i++)
            {
                var target = trainingDummies[i];
                if (target != null)
                    target.ResetTrainingTarget();
            }
        }
        public void Stop()
        {
            if (!initialized) { if (world != null) world.SetActive(false); return; }
            initialized = false;
            killMarrow?.Dispose(); killMarrow = null;
            if (pipeline != null) pipeline.AutomaticExecution = false;
            if (equipmentHud != null) { equipmentHud.gameObject.SetActive(false); Destroy(equipmentHud.gameObject); }
            if (session != null && player != null)
            {
                ClearAttacks();
                session.Health = player.CurrentHealth;
                session.Run.LoadoutChanged -= LoadoutChanged;
                player.Died -= Died;
                player.GetComponent<PlayerMotor>().InputAllowed = null;
            }
            if (director != null) director.WaveCompleted -= Completed;
            if (world != null) world.SetActive(false);
        }
        void OnDestroy() => Stop();
    }
}
