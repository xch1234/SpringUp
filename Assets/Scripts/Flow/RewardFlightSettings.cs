using UnityEngine;
namespace SpringUp.Flow
{
    [CreateAssetMenu(menuName = "SpringUp/Reward Flight Settings")]
    public sealed class RewardFlightSettings : ScriptableObject
    {
        public Sprite marrowSprite;
        public Sprite organSprite;
        public Color marrowColor = Color.white;
        public Color organColor = Color.white;
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
