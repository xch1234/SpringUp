using UnityEngine;

namespace TideBorne.Battle
{
    // Animate after movement input is read; HitFlashSkin copies the resulting sprite in LateUpdate.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PlayerSpriteAnimator : MonoBehaviour
    {
        [SerializeField] PlayerAnimationSettings settings;
        PlayerMotor motor;
        PlayerRuntime player;
        Rigidbody2D body;
        SpriteRenderer placeholder, visual;
        bool placeholderWasEnabled;
        float framePosition;
        public PlayerAnimationSettings Settings => settings;
        public SpriteRenderer Visual => visual;
        public bool FacingLeft { get; private set; }
        public bool IsMoving { get; private set; }
        public int FrameIndex { get; private set; }

        public static void Ensure(PlayerRuntime owner)
        {
            if (owner.GetComponent<PlayerSpriteAnimator>() != null || owner.GetComponent<PlayerMotor>() == null) return;
            var config = Resources.Load<PlayerAnimationSettings>("PlayerAnimationSettings");
            if (config != null && config.HasFrames) owner.gameObject.AddComponent<PlayerSpriteAnimator>();
        }

        void Awake()
        {
            if (settings == null) settings = Resources.Load<PlayerAnimationSettings>("PlayerAnimationSettings");
            motor = GetComponent<PlayerMotor>(); player = GetComponent<PlayerRuntime>(); body = GetComponent<Rigidbody2D>();
            if (settings == null || !settings.HasFrames || motor == null || body == null || player == null)
            { enabled = false; return; }
            placeholder = GetComponent<SpriteRenderer>();
            placeholderWasEnabled = placeholder != null && placeholder.enabled;
            var child = new GameObject("Player Animated Visual"); child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            visual = child.AddComponent<SpriteRenderer>();
            if (placeholder != null)
            {
                visual.sharedMaterial = placeholder.sharedMaterial;
                visual.sortingLayerID = placeholder.sortingLayerID;
                visual.sortingOrder = placeholder.sortingOrder;
            }
            visual.color = Color.white;
            FacingLeft = settings.initiallyFacingLeft;
            ApplyFrame(); ApplyLayout();
        }

        void OnEnable()
        {
            if (visual == null) return;
            visual.enabled = true;
            if (placeholder != null) placeholder.enabled = false;
        }

        void OnDisable()
        {
            if (visual == null) return;
            visual.enabled = false;
            if (placeholder != null) placeholder.enabled = placeholderWasEnabled;
        }

        void Update()
        {
            if (visual == null || settings == null || !settings.HasFrames) return;
            // Keep the exact pose while a menu pauses the world or the run has ended.
            if (Time.deltaTime <= 0 || player.CurrentHealth <= 0) return;
            Vector2 velocity = body.linearVelocity;
            IsMoving = motor.MoveInput.sqrMagnitude > .0001f && velocity.sqrMagnitude > .0025f;
            if (IsMoving && Mathf.Abs(velocity.x) > .05f) FacingLeft = velocity.x < 0;
            var frames = FacingLeft ? settings.leftFrames : settings.rightFrames;
            float rate = IsMoving ? settings.moveFramesPerSecond * (motor.IsSprinting ? settings.sprintAnimationMultiplier : 1)
                : settings.idleFramesPerSecond;
            framePosition = rate <= 0 ? 0 : Mathf.Repeat(framePosition + Time.deltaTime * rate, frames.Length);
            ApplyFrame();
        }

        void ApplyFrame()
        {
            var frames = FacingLeft ? settings.leftFrames : settings.rightFrames;
            FrameIndex = Mathf.FloorToInt(framePosition) % frames.Length;
            visual.sprite = frames[FrameIndex] != null ? frames[FrameIndex] : frames[0];
        }

        void LateUpdate() { if (visual != null && settings != null && settings.HasFrames) ApplyLayout(); }

        void ApplyLayout()
        {
            // Physics/organ targeting still use the rotating player root; only the artwork stays upright.
            visual.transform.SetPositionAndRotation(transform.position + (Vector3)settings.offset, Quaternion.identity);
            float frameHeight = settings.rightFrames[0].bounds.size.y;
            visual.transform.localScale = Vector3.one * (Mathf.Max(.1f, settings.height) / Mathf.Max(.001f, frameHeight));
        }
    }
}
