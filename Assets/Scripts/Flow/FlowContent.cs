using System;
using SpringUp.Organs;
using TideBorne.Battle;
using Tideflesh.Systems;
using UnityEngine;

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
        public PlayerChargeAttack Attack { get; private set; }
        FlowApplication app;
        RunSession session;
        bool initialized;
        public void Initialize(FlowApplication owner, RunSession current)
        {
            app = owner; session = current;
            if (kind == ContentKind.MainMenu) { initialized = true; return; }
            if (session == null || player == null || pipeline == null)
                throw new InvalidOperationException("Incomplete content dependencies");
            world.SetActive(true);
            PlayerLocator.Bind(player.transform);
            player.GetComponent<PlayerMotor>().InputAllowed = () => app.WorldInputAllowed;
            player.SetHealthRegeneration(kind == ContentKind.Battle);
            if (session.Health.HasValue) player.RestoreHealth(session.Health.Value);
            else player.ResetHealthToMax();
            pipeline.StepIntervalProvider = part => player.GetStepInterval((int)part);
            pipeline.CycleDurationProvider = part => player.GetCycleDuration((int)part);
            pipeline.GetComponent<BattleOrganTargeting>().AttackOrigin = player.transform;
            Attack = pipeline.gameObject.AddComponent<PlayerChargeAttack>();
            Attack.Configure(pipeline, () => app.WorldInputAllowed, 100, 20);
            initialized = true;
            session.Run.LoadoutChanged += LoadoutChanged;
            player.Died += Died;
            if (director != null) director.WaveCompleted += Completed;
            ApplyLoadout();
            if (kind == ContentKind.Battle) Attack.BeginWave();
            else Attack.RestoreCharge(session.Charge);
            if (dummy != null) { dummy.TrainingTarget = true; dummy.ResetTrainingTarget(); }
            initialized = true;
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
            HandPipelineLoadout.Apply(pipeline.GetBody(BodyPart.Hand), session.Run.Inventory, app.Configuration.Definitions);
            foreach (BodyPart part in Enum.GetValues(typeof(BodyPart))) pipeline.GetBody(part).ResetTiming();
            Attack.RefreshLoadout();
        }
        void ClearAttacks()
        {
            Attack?.CancelRound(); combat?.Clear(); effects?.Clear();
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
            if (session != null && player != null)
            {
                ClearAttacks();
                session.Health = player.CurrentHealth;
                session.Charge = Attack != null ? Attack.Charge : session.Charge;
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
