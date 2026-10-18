using UnityEngine;
namespace SpringUp.Flow
{
    [CreateAssetMenu(menuName = "SpringUp/Reward Flight Settings")]
    public sealed class RewardFlightSettings : ScriptableObject
    {
        public Sprite sprite;
        public Color marrowColor = new Color(1f, .78f, .18f);
        public Color organColor = new Color(.7f, .35f, 1f);
        [Min(1)] public float size = 14f;
        [Min(0)] public float holdSeconds = .1f;
        [Min(.01f)] public float flightSeconds = .4f;
        [Min(0)] public float arrivalSeconds = .08f;
        public float arcHeight = 35f;
        public AnimationCurve flightProgress = new AnimationCurve(new Keyframe(0, 0, 0, 0), new Keyframe(1, 1, 2, 2));
        [Tooltip("Normalized Canvas anchor for the shared receiver, near the upper left.")]
        public Vector2 receiverAnchor = new Vector2(.03f, .96f);
        [Min(1)] public int maxConcurrent = 128;
    }
}
