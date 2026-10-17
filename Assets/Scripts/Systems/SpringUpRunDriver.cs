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
        public RunController Run { get; private set; }
        public RunLaboratoryEquipment Equipment { get; private set; }
        public IReadOnlyDictionary<string, PipelineDefinition> Definitions => definitions;
        private readonly Dictionary<string, PipelineDefinition> definitions = new Dictionary<string, PipelineDefinition>();
        public TorsoEquipmentRuntime Torso { get; private set; }
        public OrganCombatReport CombatReport { get; private set; }
        private float originalCameraSize;
        private bool restarting;
        private Rect originalViewport;
        private RunEquipmentHud equipmentHud;
        private const float HudHeight = 44f;

        private void Awake()
        {
            Time.timeScale = 1f;
            director.ConfigureExternal(7, 60);
            laboratoryRoot.SetActive(false);
            capacityText.transform.parent.gameObject.SetActive(false);
            battleCamera = battleCamera != null ? battleCamera : Camera.main;
            if (battleCamera != null) { originalViewport = battleCamera.rect; originalCameraSize = battleCamera.orthographicSize; }
            Torso = player.gameObject.AddComponent<TorsoEquipmentRuntime>(); Torso.Configure(player);
            combat.Torso = Torso;
            var header = new GameObject("Battle HUD background", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(runStatus.transform.parent, false);
            header.transform.SetAsFirstSibling();
            var rect = (RectTransform)header.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1); rect.sizeDelta = new Vector2(0, HudHeight);
            header.GetComponent<Image>().color = new Color(.08f, .1f, .12f, 1f);
            header.GetComponent<Image>().raycastTarget = false;
            equipmentHud = RunEquipmentHud.Create(runStatus.transform.parent, runStatus.font);
            CombatReport = gameObject.AddComponent<OrganCombatReport>();
            CombatReport.Bind(pipeline); combat.Report = CombatReport;
            equipmentHud.BindReport(CombatReport);
            equipmentHud.gameObject.SetActive(false);
            ConfigureTiming(pipeline);
            pipeline.AutomaticExecution = true;
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
            battleCamera.orthographicSize = originalCameraSize * Torso.Loadout.View;
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
                foreach (var definition in organCatalog.CreateDefinitions()) definitions.Add(definition.Id, definition);
                CombatReport.SetNames(definitions.Values);
                var catalog = Catalog.LoadOrgans(organsJson.text);
                foreach (var definition in definitions.Values)
                {
                    if (!catalog.TryGetValue(definition.Id, out var entry) || entry.MountKind != definition.MountKind
                        || entry.type != (definition.Type == OrganType.Torso ? "torso_special" : definition.Type == OrganType.Trigger ? "trigger" : "executor"))
                        throw new InvalidOperationException("库存图鉴与战斗定义不匹配：" + definition.Id);
                    // JSON 保留经济与掉落数据；运行时的战斗字段统一投影自 A 的资源快照。
                    entry.damage = definition.Damage;
                    entry.radius = definition.Radius;
                    entry.chance = definition.Status != null ? definition.Status.Chance * 100f
                        : definition.Type == OrganType.Trigger ? 100f : 0f;
                    entry.speed = definition.Projectile?.Speed ?? definition.BlackHole?.PullSpeed ?? 0f;
                    entry.description = new LaboratoryItem("definition", definition).Description;
                    if (definition.Status != null)
                        entry.status = new OrganStatusSpec { id = definition.Status.Kind.ToString().ToLowerInvariant(),
                            duration = definition.Status.Duration, max_stack = definition.Status.MaxStacks };
                    else if (definition.BlackHole != null)
                        entry.status = new OrganStatusSpec { id = "collapse", duration = definition.BlackHole.Duration, max_stack = 1 };
                }
                var waves = Catalog.LoadWaves(wavesJson.text, catalog);
                // 七波闭环的临时奖励：尚未实现的蜘蛛巢换成可用 TNT，不修改原波次表。
                foreach (var wave in waves)
                {
                    wave.guaranteed_drops = wave.guaranteed_drops.Select(id => id == "spider_nest" ? "tnt" : id).ToArray();
                    foreach (var id in wave.guaranteed_drops.Concat(wave.optional_drops))
                        if (!definitions.ContainsKey(id)) throw new InvalidOperationException("奖励缺少战斗定义：" + id);
                }
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
                var torsos = new List<TorsoSettings>();
                for (int slot = 1; slot <= Inventory.SlotCounts["torso"]; slot++)
                {
                    var id = Run.Inventory.EquippedAt("torso", slot);
                    if (id != null && definitions.TryGetValue(Run.Inventory.OrganOf(id).id, out var definition)) torsos.Add(definition.Torso);
                }
                Torso.Apply(torsos, pipeline);
                PipelineLoadout.Apply(pipeline, Run.Inventory, definitions);
                Equipment?.Refresh();
            }
            else if (name == "RunStarted")
            {
                laboratoryRoot.SetActive(false);
                CombatReport.ResetWave();
                player.ResetDamageProtection();
                foreach (SpringUp.Organs.BodyPart part in Enum.GetValues(typeof(SpringUp.Organs.BodyPart)))
                    pipeline.GetBody(part).ResetTiming();
                director.ConfigureExternal(Run.MaxWave, Run.WaveDuration);
                if (!director.StartNextWave()) throw new InvalidOperationException("战场未能开始下一波。");
                pipeline.enabled = true;
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
            capacityText.transform.parent.gameObject.SetActive(lab || Run.Phase == "run_won" || Run.Phase == "run_lost");
            runStatus.text = Run.Phase == "run_won" ? "七波完成" : Run.Phase == "run_lost" ? "本局失败"
                : "第 " + Run.Wave + " / " + Run.MaxWave + " 波    " + (lab ? "实验室" : Mathf.CeilToInt(director.RemainingTime) + " 秒")
                    + "    生命 " + player.CurrentHealth.ToString("0.#") + " / " + player.MaxHealth.ToString("0.#") + "    髓质 " + Run.Marrow;
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
            if (battleCamera != null) { battleCamera.rect = originalViewport; battleCamera.orthographicSize = originalCameraSize; }
            if (director != null) director.WaveCompleted -= WaveCompleted;
            if (player != null) player.Died -= PlayerDied;
            Time.timeScale = 1f;
        }
    }
}
