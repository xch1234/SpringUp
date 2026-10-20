using UnityEngine;

namespace Tideflesh.Systems
{
    [CreateAssetMenu(menuName = "SpringUp/范围器官表现配置")]
    public sealed class AreaEffectVisualSettings : ScriptableObject
    {
        public Color explosionColor = new Color(1, .45f, .08f, .85f);
        public Color flashColor = new Color(1, .95f, .65f, .7f);
        [Min(.05f)] public float explosionSeconds = .3f;
        public Color fogColor = new Color(.25f, .8f, .15f, .22f);
        [Min(0)] public float fogAnimationSpeed = .8f;
        public Color holeColor = new Color(.15f, .03f, .25f, .65f);
        public Color swirlColor = new Color(.65f, .3f, 1f, .8f);
        [Min(0)] public float holeAnimationSpeed = 2f;
        [Min(.01f)] public float lineWidth = .06f;
        public int sortingOrder = 7;
    }
}
