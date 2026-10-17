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
        public TorsoEquipmentRuntime Torso { get; private set; }
        public OrganCombatReport CombatReport { get; private set; }
        public RunLaboratoryEquipment Equipment { get; private set; }
        RunEquipmentHud equipmentHud;
        Camera battleCamera;
        float baseCameraSize;
        [SerializeField] Transform merchant;
        public Transform Merchant => merchant;
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
            equipmentHud.Bind(Equipment); equipmentHud.BindReport(CombatReport);
            battleCamera = world.GetComponentInChildren<Camera>(true);
            if (battleCamera != null) baseCameraSize = battleCamera.orthographicSize;
            if (kind == ContentKind.Rest) EnsureMerchant();
            initialized = true;
            if (kind == ContentKind.Battle) killMarrow = new KillMarrowBinding(session.Run);
            session.Run.LoadoutChanged += LoadoutChanged;
            player.Died += Died;
            if (director != null) director.WaveCompleted += Completed;
            ApplyLoadout();
            // Apply max-health modifiers before restoring a snapshot from the previous scene.
            if (session.Health.HasValue) player.RestoreHealth(session.Health.Value);
            else player.ResetHealthToMax();
            if (dummy != null) { dummy.TrainingTarget = true; dummy.ResetTrainingTarget(); }
        }
        // Created by the shared scene entry, so regenerated Rest scenes retain the interaction.
        public void EnsureMerchant()
        {
            if (merchant != null || kind != ContentKind.Rest) return;
            var go = new GameObject("Merchant · E Shop");
            go.transform.SetParent(world.transform, false);
            go.transform.position = player.transform.position + Vector3.left * 3;
            merchant = go.transform;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = player.GetComponentInChildren<SpriteRenderer>().sprite;
            sprite.color = new Color(.2f, .9f, .85f);
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1);
            var label = new GameObject("Merchant label").AddComponent<TextMesh>();
            label.transform.SetParent(go.transform, false); label.transform.localPosition = Vector3.up;
            label.text = "SHOP [E]"; label.characterSize = .15f; label.fontSize = 40;
            label.anchor = TextAnchor.MiddleCenter;
        }
        void Update()
        {
            if (initialized && pipeline != null) pipeline.AutomaticExecution = app.WorldInputAllowed;
            if (kind == ContentKind.Rest && Keyboard.current?.eKey.wasPressedThisFrame == true) app?.TryOpenShop();
        }
        void LateUpdate()
        {
            if (battleCamera != null && Torso != null)
                battleCamera.orthographicSize = baseCameraSize * Torso.Loadout.View;
            if (equipmentHud != null) equipmentHud.gameObject.SetActive(initialized && !app.HasModal && !app.IsTransitioning);
        }
        public void Begin()
        {
            if (kind == ContentKind.Battle &&
                !director.StartExternalWave(session.Run.Wave, session.Run.MaxWave, session.Run.WaveDuration))
                throw new InvalidOperationException("Wave did not start");
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
            dummy.ResetTrainingTarget();
            dummy.transform.position = player.transform.position + Vector3.right * 2;
        }
        public void Stop()
        {
            if (!initialized) { if (world != null) world.SetActive(false); return; }
            initialized = false;
            killMarrow?.Dispose(); killMarrow = null;
            MarrowDeathVisual.Clear();
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
