using UnityEngine;
namespace TideBorne.Battle
{
    public sealed class WaveSpawnSettings : ScriptableObject
    {
        // Keep the old serialized field for legacy assets/tools; it no longer limits spawning.
        [HideInInspector] public float firstSixSpawnSeconds = 50f;
        public float SpawnDuration(int wave, float duration) => Mathf.Max(0, duration);
        public static WaveSpawnSettings Load() => Resources.Load<EnemyCatalogReference>("EnemyCatalogReference").waves;
    }
}
