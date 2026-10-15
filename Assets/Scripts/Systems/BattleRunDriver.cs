using System;
using System.Collections.Generic;
using System.IO;
using SpringUp.Organs;
using TideBorne.Battle;
using UnityEngine;

namespace Tideflesh.Systems
{
    /// <summary>
    /// 灰盒战场上的一局流程。场景里有 WaveDirector 时自动挂上：
    /// 到点结算进实验室，未装备没超上限才能开下一波。
    /// 左侧面板是实验室接上前的占位，出售和下一波已经走 RunController。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/流程/战场流程（BattleRunDriver）")]
    public sealed class BattleRunDriver : MonoBehaviour
    {
        public BattleRunLink Link { get; private set; }

        WaveDirector _director;
        OrganPipelineController _pipeline;
        string _status = "";
        Vector2 _scroll;
        GUIStyle _label;
        GUIStyle _button;
        bool _styleReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (SpringUp.Flow.FlowApplication.IsActive) return;
            if (UnityEngine.Object.FindFirstObjectByType<SpringUpRunDriver>() != null) return;
            if (UnityEngine.Object.FindFirstObjectByType<WaveDirector>() == null) return;
            if (UnityEngine.Object.FindFirstObjectByType<BattleRunDriver>() != null) return;
            var go = new GameObject("BattleRunDriver");
            go.AddComponent<BattleRunDriver>();
        }

        void Awake()
        {
            _director = FindFirstObjectByType<WaveDirector>();
            if (_director != null)
                _director.WaveCompleted += OnWaveCompleted;
        }

        void Start()
        {
            if (_director == null)
            {
                Debug.LogError("BattleRunDriver: 场景里没有 WaveDirector。");
                return;
            }

            try
            {
                var organsPath = Path.Combine(Application.dataPath, "Data", "Organs", "organs.json");
                var wavesPath = Path.Combine(Application.dataPath, "Data", "Waves", "waves.json");
                var catalog = Catalog.LoadOrgans(File.ReadAllText(organsPath));
                var waves = Catalog.LoadWaves(File.ReadAllText(wavesPath), catalog);
                Link = new BattleRunLink(new RunController(catalog, waves, OnSignal));
                Link.Begin();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            if (_director.CurrentWave == 0 && !_director.AutoStartEnabled)
                _director.StartNextWave();
        }

        void OnDestroy()
        {
            if (_director != null)
                _director.WaveCompleted -= OnWaveCompleted;
            if (!SpringUp.Flow.FlowApplication.IsActive && Time.timeScale == 0f)
                Time.timeScale = 1f;
        }

        void OnSignal(string name, object payload)
        {
            if (name == "LoadoutChanged" && Link != null)
                SyncHandPipeline();
            Debug.Log("[ProgramD] " + name);
        }

        void OnWaveCompleted(int wave, int cleared)
        {
            if (Link == null) return;
            Debug.Log("[ProgramD] 第 " + wave + " 波结算，清除残余 " + cleared);
            try
            {
                Link.NotifyWaveTimerFinished(wave);
            }
            catch (RunException ex)
            {
                Debug.LogError("[ProgramD] " + ex.Message);
                return;
            }

            if (Link.Run.Phase == "lab" || Link.Run.Phase == "run_won")
                Time.timeScale = 0f;
        }

        public void RequestNextWave()
        {
            if (Link == null || _director == null) return;
            if (!Link.TryStartNextWave(out _))
                return;
            Time.timeScale = 1f;
            _status = "";
            _director.StartNextWave();
        }

        void SyncHandPipeline()
        {
            try
            {
                var pipeline = HandPipeline();
                var asset = OrganCatalog.LoadDefault();
                var map = new Dictionary<string, SpringUp.Organs.OrganDefinition>();
                foreach (var definition in asset.CreateDefinitions())
                    map[definition.Id] = definition;
                var skipped = HandPipelineLoadout.Apply(pipeline.GetBody(BodyPart.Hand), Link.Run.Inventory, map);
                if (skipped > 0)
                    Debug.Log("[ProgramD] 手部有 " + skipped + " 个器官没有管道配置，这些槽留空。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ProgramD] 手部管道没写上：" + ex.Message);
            }
        }

        void OnGUI()
        {
            if (Link == null) return;
            EnsureStyle();
            var run = Link.Run;
            if (run.Phase == "combat")
            {
                DrawCombatHint(run);
                return;
            }

            DrawLab(run);
        }

        void DrawCombatHint(RunController run)
        {
            if (_director != null && _director.State == WaveState.Running)
            {
                if (GUI.Button(new Rect(16f, Screen.height - 78f, 200f, 28f), "调试：立刻结算本波", _button))
                    _director.CompleteCurrentWave();
            }

            GUI.Label(
                new Rect(16f, Screen.height - 40f, 520f, 28f),
                "第 " + run.Wave + " 波战斗中    髓质 " + run.Marrow,
                _label);
        }

        void DrawLab(RunController run)
        {
            var height = Mathf.Min(640f, Screen.height - 32f);
            var area = new Rect(16f, 16f, 500f, height);
            var previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;

            GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 12f, area.width - 24f, area.height - 24f));
            if (run.Phase == "run_won")
            {
                GUILayout.Label("七波结束。髓质 " + run.Marrow, _label);
                GUILayout.EndArea();
                return;
            }

            var bag = run.Inventory.UnequippedIds();
            var cap = run.Inventory.UnequippedCapacity();
            GUILayout.Label("实验室    第 " + run.Wave + " 波结束    髓质 " + run.Marrow, _label);
            GUILayout.Label("未装备 " + bag.Count + " / " + cap, _label);
            GUILayout.Label("手部槽会写进器官管道。没有对应配置的器官（例如蜘蛛巢）只留在库存。", _label);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(Mathf.Max(160f, height - 230f)));
            foreach (var instanceId in bag)
                DrawBagRow(run, instanceId);
            GUILayout.Space(8f);
            GUILayout.Label("已装备", _label);
            foreach (var part in new[] { "head", "hand", "leg", "torso" })
                DrawEquipped(run, part);
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status, _label);
            if (run.CanStartNextWave())
            {
                if (GUILayout.Button("开始下一波", _button))
                    RequestNextWave();
            }
            else
            {
                var extra = bag.Count - cap;
                GUILayout.Label("未装备超过 " + cap + "，请再卖掉或丢掉 " + extra + " 件。", _label);
            }

            GUILayout.EndArea();
        }

        void DrawBagRow(RunController run, string instanceId)
        {
            var organ = run.Inventory.OrganOf(instanceId);
            GUILayout.BeginHorizontal();
            GUILayout.Label(organ.name + "（" + PartLabel(organ.part) + "）", _label, GUILayout.Width(220f));
            if (GUILayout.Button("装备", _button, GUILayout.Width(64f)))
                TryEquipFirstEmpty(run, instanceId, organ.part);
            if (GUILayout.Button("出售", _button, GUILayout.Width(64f)))
                Try(run, () => run.Sell(instanceId));
            if (GUILayout.Button("丢弃", _button, GUILayout.Width(64f)))
                Try(run, () => run.Discard(instanceId));
            GUILayout.EndHorizontal();
        }

        void DrawEquipped(RunController run, string part)
        {
            var count = Inventory.SlotCounts[part];
            for (var slot = 1; slot <= count; slot++)
            {
                var instanceId = run.Inventory.EquippedAt(part, slot);
                if (instanceId == null) continue;
                var organ = run.Inventory.OrganOf(instanceId);
                var capturedPart = part;
                var capturedSlot = slot;
                GUILayout.BeginHorizontal();
                GUILayout.Label(PartLabel(part) + " " + slot + "  " + organ.name, _label, GUILayout.Width(280f));
                if (GUILayout.Button("卸下", _button, GUILayout.Width(64f)))
                    Try(run, () => run.Unequip(capturedPart, capturedSlot));
                GUILayout.EndHorizontal();
            }
        }

        OrganPipelineController HandPipeline()
        {
            if (_pipeline != null) return _pipeline;
            _pipeline = FindFirstObjectByType<OrganPipelineController>();
            if (_pipeline == null)
                _pipeline = gameObject.AddComponent<OrganPipelineController>();
            if (_pipeline.GetComponent<BattleOrganTargeting>() == null)
                _pipeline.gameObject.AddComponent<BattleOrganTargeting>();
            if (_pipeline.GetComponent<BattleOrganCombat>() == null)
                _pipeline.gameObject.AddComponent<BattleOrganCombat>();
            return _pipeline;
        }

        void TryEquipFirstEmpty(RunController run, string instanceId, string part)
        {
            var slot = FirstEmptySlot(run.Inventory, part);
            if (slot == 0)
            {
                _status = PartLabel(part) + "没有空槽。";
                return;
            }

            Try(run, () => run.Equip(instanceId, part, slot));
        }

        static int FirstEmptySlot(Inventory inventory, string part)
        {
            var count = Inventory.SlotCounts[part];
            for (var slot = 1; slot <= count; slot++)
            {
                if (inventory.EquippedAt(part, slot) == null)
                    return slot;
            }

            return 0;
        }

        void Try(RunController run, Action action)
        {
            try
            {
                action();
                _status = "";
            }
            catch (Exception ex) when (ex is RunException || ex is InventoryException)
            {
                _status = ex.Message;
            }
        }

        static string PartLabel(string part)
        {
            switch (part)
            {
                case "head": return "头";
                case "hand": return "手";
                case "leg": return "腿";
                case "torso": return "躯干";
                default: return part;
            }
        }

        void EnsureStyle()
        {
            if (_styleReady) return;
            _styleReady = true;
            Font font = null;
            try
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei" }, 16);
            }
            catch (Exception)
            {
                font = null;
            }

            _label = new GUIStyle(GUI.skin.label)
            {
                font = font,
                fontSize = 16,
                wordWrap = true,
                normal = { textColor = Color.white }
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                font = font,
                fontSize = 15
            };
        }
    }
}
