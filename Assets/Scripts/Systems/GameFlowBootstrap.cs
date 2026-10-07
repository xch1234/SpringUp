using UnityEngine;

namespace Tideflesh.Systems
{
    /// <summary>
    /// 把 organs.json / waves.json 拖到这两个槽，运行时即可开局。
    /// 程序 C 在 60 秒结束时调用 CombatTimerFinished。
    /// </summary>
    public class GameFlowBootstrap : MonoBehaviour
    {
        public TextAsset organsJson;
        public TextAsset wavesJson;

        public RunController Run { get; private set; }

        void Awake()
        {
            if (organsJson == null || wavesJson == null)
            {
                Debug.LogError("GameFlowBootstrap: assign organsJson and wavesJson TextAssets.");
                return;
            }
            var catalog = Catalog.LoadOrgans(organsJson.text);
            var waves = Catalog.LoadWaves(wavesJson.text, catalog);
            Run = new RunController(catalog, waves, OnSignal);
            Run.StartRun();
        }

        void OnSignal(string name, object payload)
        {
            Debug.Log($"[ProgramD] {name}");
        }
    }
}
