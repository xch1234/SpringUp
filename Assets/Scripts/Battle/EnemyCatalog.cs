using System;
using System.Linq;
using UnityEngine;
namespace TideBorne.Battle
{
    [CreateAssetMenu(menuName = "SpringUp/Enemy Catalog")]
    public sealed class EnemyCatalog : ScriptableObject
    {
        public EnemyDefinition[] enemies;
        public EnemyDefinition trainingDummy;
        public static EnemyCatalog Load()
        {
            var link = Resources.Load<EnemyCatalogReference>("EnemyCatalogReference");
            if (link == null || link.catalog == null) throw new InvalidOperationException("Missing EnemyCatalogReference");
            return link.catalog;
        }
        public EnemyDefinition Get(EnemyArchetype type) => enemies.First(e => e != null && e.archetype == type);
        public EnemyArchetype[] Spawnable() => enemies.Where(e => e != null && e.allowRegularSpawn && e.archetype != EnemyArchetype.Dummy).Select(e => e.archetype).ToArray();
    }
}
