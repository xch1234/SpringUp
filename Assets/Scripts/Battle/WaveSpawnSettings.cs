using UnityEngine;
namespace TideBorne.Battle
{
    public sealed class WaveSpawnSettings : ScriptableObject
    {
        [Min(0.01f)] public float firstSixSpawnSeconds = 50f;
        public float SpawnDuration(int wave, float duration) => wave >= 1 && wave <= 6
            ? Mathf.Min(duration, Mathf.Max(0.01f, firstSixSpawnSeconds)) : duration;
        public static WaveSpawnSettings Load() => Resources.Load<EnemyCatalogReference>("EnemyCatalogReference").waves;
    }
}
