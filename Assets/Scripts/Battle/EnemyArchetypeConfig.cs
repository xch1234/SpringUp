using System;
namespace TideBorne.Battle
{
    // Legacy callers and generators now resolve the same assets as gameplay.
    public static class EnemyArchetypeConfig
    {
        public static EnemyDefinition Get(EnemyArchetype type) => EnemyCatalog.Load().Get(type);
        public static EnemyArchetype[] AllArchetypes() => (EnemyArchetype[])Enum.GetValues(typeof(EnemyArchetype));
        public static EnemyArchetype[] SpawnableArchetypes() => EnemyCatalog.Load().Spawnable();
        public static float DummyHealth => Get(EnemyArchetype.Dummy).maxHealth;
    }
}
