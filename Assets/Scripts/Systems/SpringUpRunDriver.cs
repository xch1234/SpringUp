using System;
using System.Collections.Generic;
using System.Linq;
using SpringUp.Laboratory;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PipelineDefinition = SpringUp.Organs.OrganDefinition;

namespace Tideflesh.Systems
{
    [DefaultExecutionOrder(-100)]
    public sealed class SpringUpRunDriver : MonoBehaviour
    {
        public TextAsset organsJson, wavesJson;
        public OrganCatalog organCatalog;
        public WaveDirector director;
        public PlayerRuntime player;
        public OrganPipelineController pipeline;
        public BattleOrganCombat combat;
        public LaboratoryDemoBootstrap laboratorySource;
        public LaboratoryPresenter presenter;
        public GameObject laboratoryRoot;
        public Camera battleCamera;
        public Text runStatus, rewardText, capacityText, errorText;
        public Button nextButton, sellButton, discardButton, restartButton;
        [Header("主动攻击充能")]
        [Min(1f), Tooltip("基础充能上限；整轮消耗超过该值时，上限自动提高至整轮消耗。")]
        public float chargeCapacity = 100f;
        [Min(0f), Tooltip("战斗中每秒自动恢复的充能。每波开始充满。")]
        public float chargeRecoveryPerSecond = 20f;
        public PlayerChargeAttack ChargeAttack { get; private set; }
        public RunController Run { get; private set; }
        public RunLaboratoryEquipment Equipment { get; private set; }
        public IReadOnlyDictionary<string, PipelineDefinition> Definitions => definitions;
        private readonly Dictionary<string, PipelineDefinition> definitions = new Dictionary<string, PipelineDefinition>();
        private bool restarting;
        private Rect originalViewport;
        private RunEquipmentHud equipmentHud;
        private ChargeAttackHud chargeHud;
        private const float HudHeight = 44f;

        private void Awake()
        {
            Time.timeScale = 1f;
            director.ConfigureExternal(7, 60);
            laboratoryRoot.SetActive(false);
            capacityText.transform.parent.gameObject.SetActive(false);
            battleCamera = battleCamera != null ? battleCamera : Camera.main;
            if (battleCamera != null) originalViewport = battleCamera.rect;
            var header = new GameObject("Battle HUD background", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(runStatus.transform.parent, false);
            header.transform.SetAsFirstSibling();
            var rect = (RectTransform)header.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1); rect.sizeDelta = new Vector2(0, HudHeight);
            header.GetComponent<Image>().color = new Color(.08f, .1f, .12f, 1f);
            header.GetComponent<Image>().raycastTarget = false;
            equipmentHud = RunEquipmentHud.Create(runStatus.transform.parent, runStatus.font);
            equipmentHud.gameObject.SetActive(false);
            ConfigureTiming(pipeline);
            ChargeAttack = gameObject.AddComponent<PlayerChargeAttack>();
            ChargeAttack.Configure(pipeline, () => Run?.Phase == "combat", chargeCapacity, chargeRecoveryPerSecond);
            chargeHud = ChargeAttackHud.Create(runStatus.transform.parent, runStatus.font, ChargeAttack);
            presenter.GetComponentInChildren<LaboratoryCombatDemo>(true).ConfigureTiming = ConfigureTiming;
            pipeline.enabled = false;
        }

        private void ConfigureTiming(OrganPipelineController target)
        {
            target.StepIntervalProvider = part => player.GetStepInterval((int)part);
            target.CycleDurationProvider = part => player.GetCycleDuration((int)part);
            foreach (SpringUp.Organs.BodyPart part in Enum.GetValues(typeof(SpringUp.Organs.BodyPart)))
            {
                var body = target.GetBody(part);
                body.StepInterval = Mathf.Max(BodyRuntime.MinimumStepInterval, player.GetStepInterval((int)part));
                body.CycleDuration = player.GetCycleDuration((int)part);
            }
        }

        private void LateUpdate()
        {
            if (battleCamera == null || Run == null) return;
            float reserved = Mathf.Clamp01(HudHeight * runStatus.canvas.scaleFactor / Mathf.Max(1, Screen.height));
            battleCamera.rect = Run.Phase == "combat"
                ? new Rect(originalViewport.x, originalViewport.y, originalViewport.width,
                    Mathf.Max(0.1f, originalViewport.height - reserved))
                : originalViewport;
        }

        private void Start()
        {
            try
            {
                var config = new RunConfiguration(organsJson.text, wavesJson.text, organCatalog);
                foreach (var pair in config.Definitions) definitions.Add(pair.Key, pair.Value);
                var catalog = config.Organs;
                var waves = config.Waves;
                Run = new RunController(catalog, waves, Signal);
                Equipment = new RunLaboratoryEquipment(Run, definitions);
                equipmentHud.Bind(Equipment);
                laboratorySource.Bind(Equipment);
                director.WaveCompleted += WaveCompleted;
                player.Died += PlayerDied;
                nextButton.onClick.AddListener(NextWave);
                sellButton.onClick.AddListener(SellSelected);
                discardButton.onClick.AddListener(DiscardSelected);
                restartButton.onClick.AddListener(RestartRun);
                player.ResetHealthToMax();
                Run.StartRun();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                errorText.text = "初始化失败：" + error.Message;
                Time.timeScale = 0f;
            }
        }

        private void Signal(string name, object payload)
        {
            if (name == "LoadoutChanged")
            {
                HandPipelineLoadout.Apply(pipeline.GetBody(BodyPart.Hand), Run.Inventory, definitions);
                ChargeAttack.RefreshLoadout();
                Equipment?.Refresh();
            }
            else if (name == "RunStarted")
            {
                laboratoryRoot.SetActive(false);
                foreach (SpringUp.Organs.BodyPart part in Enum.GetValues(typeof(SpringUp.Organs.BodyPart)))
                    pipeline.GetBody(part).ResetTiming();
                director.ConfigureExternal(Run.MaxWave, Run.WaveDuration);
                if (!director.StartNextWave()) throw new InvalidOperationException("战场未能开始下一波。");
                pipeline.enabled = true;
                ChargeAttack.BeginWave();
                Time.timeScale = 1f;
            }
            else if (name == "WaveEnded") StopCombat();
            else if (name == "LabOpened")
            {
                var reward = (LabOpenedPayload)payload;
                rewardText.text = "本波奖励：" + string.Join("、", reward.Drops.Select(d => Run.Inventory.Catalog[d.organ_id].name))
                    + "    髓质 +" + reward.MarrowDelta;
                laboratoryRoot.SetActive(true);
            }
            else if (name == "RunEnded") StopCombat();
            RefreshView();
        }

        private void StopCombat()
        {
            ChargeAttack.CancelRound();
            pipeline.enabled = false;
            combat.Clear();
            pipeline.GetComponent<BattleOrganEffects>()?.Clear();
            foreach (var projectile in FindObjectsByType<Projectile>(FindObjectsSortMode.None))
                if (projectile.gameObject.scene == gameObject.scene) Destroy(projectile.gameObject);
            Time.timeScale = 0f;
        }

        private void WaveCompleted(int wave, int cleared)
        {
            if (Run.Phase != "combat" || Run.Wave != wave) return;
            if (player.CurrentHealth <= 0f) Run.LoseRun();
            else Run.CombatTimerFinished();
        }
        private void PlayerDied() => Run?.LoseRun();
        public void NextWave()
        {
            if (Run == null || !Run.CanStartNextWave()) return;
            errorText.text = "";
            Run.StartNextWave();
        }
        private void SellSelected()
        {
            var id = presenter.SelectedInstanceId;
            if (id != null && !Equipment.TrySell(id, out var error)) errorText.text = error;
        }
        private void DiscardSelected()
        {
            var id = presenter.SelectedInstanceId;
            if (id != null && !Equipment.TryDiscard(id, out var error)) errorText.text = error;
        }
        public void RestartRun()
        {
            if (restarting) return;
            restarting = true;
            StopCombat();
            EnemyRuntime.ClearBattlefield();
            SimplePool.Clear();
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameObject.scene.path);
        }
        private void Update() => RefreshView();
        private void RefreshView()
        {
            if (Run == null) return;
            bool lab = Run.Phase == "lab";
            equipmentHud.gameObject.SetActive(Run.Phase == "combat");
            chargeHud.Refresh(Run.Phase == "combat");
            capacityText.transform.parent.gameObject.SetActive(lab || Run.Phase == "run_won" || Run.Phase == "run_lost");
            runStatus.text = Run.Phase == "run_won" ? "七波完成" : Run.Phase == "run_lost" ? "本局失败"
                : "第 " + Run.Wave + " / " + Run.MaxWave + " 波    " + (lab ? "实验室" : Mathf.CeilToInt(director.RemainingTime) + " 秒")
                    + "    生命 " + Mathf.CeilToInt(player.CurrentHealth) + " / " + player.MaxHealth + "    髓质 " + Run.Marrow;
            int count = Run.Inventory.UnequippedIds().Count, cap = Run.Inventory.UnequippedCapacity();
            capacityText.text = "未装备 " + count + " / " + cap + (count > cap ? "    超出 " + (count - cap) + " 件" : "");
            nextButton.gameObject.SetActive(lab);
            nextButton.interactable = Run.CanStartNextWave();
            sellButton.gameObject.SetActive(lab); discardButton.gameObject.SetActive(lab);
            sellButton.interactable = discardButton.interactable = lab && presenter.SelectedInstanceId != null;
            restartButton.gameObject.SetActive(Run.Phase == "run_won" || Run.Phase == "run_lost");
            capacityText.gameObject.SetActive(lab); rewardText.gameObject.SetActive(lab);
        }
        private void OnDestroy()
        {
            if (battleCamera != null) battleCamera.rect = originalViewport;
            if (director != null) director.WaveCompleted -= WaveCompleted;
            if (player != null) player.Died -= PlayerDied;
            if (!SpringUp.Flow.FlowApplication.IsActive) Time.timeScale = 1f;
        }
    }
}
