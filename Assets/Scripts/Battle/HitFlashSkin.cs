using System.Collections.Generic;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 受击闪红蒙皮。为每个 <see cref="SpriteRenderer"/> 在其下挂一层同图、纯红的叠加精灵，
    /// 只改变叠加层的 alpha，主体精灵的颜色、材质、贴图都不动。
    ///
    /// **为什么用叠加层而不是改 <c>SpriteRenderer.color</c>**：叠加层跟着主体的当前帧走
    /// （<see cref="LateUpdate"/> 每帧同步 sprite、翻转、绘制模式与排序），所以动画换图时蒙皮
    /// 自动换帧；同时不受主体颜色影响，换美术、染色、开火闪黄都不会跟它打架。
    ///
    /// 计时：渐入 <see cref="fadeInDuration"/> → 满红保持 <see cref="holdDuration"/> → 渐出
    /// <see cref="fadeOutDuration"/>。受击时从当前红度重新渐入并重新计时；连续受击会一直保持满红。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战斗/受击闪红蒙皮（HitFlashSkin）")]
    public class HitFlashSkin : MonoBehaviour
    {
        /// <summary>叠加层的 GameObject 名。重建时用它排除自己的叠加层，避免递归套娃。</summary>
        private const string OverlayName = "__HitFlashSkin";

        [Tooltip("叠加层的颜色。")]
        [SerializeField] private Color flashColor = new Color(1f, 0f, 0f, 1f);

        [Tooltip("满红时叠加层的不透明度。0 = 不可见，1 = 完全盖住原图。")]
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.8f;

        [Tooltip("渐入时长，秒。")]
        [SerializeField, Min(0.001f)] private float fadeInDuration = 0.05f;

        [Tooltip("满红保持时长，秒。受击期间持续保持满红。")]
        [SerializeField, Min(0f)] private float holdDuration = 0.05f;

        [Tooltip("渐出时长，秒。")]
        [SerializeField, Min(0.001f)] private float fadeOutDuration = 0.05f;

        /// <summary>一个主体渲染器与它的叠加层。</summary>
        private sealed class Layer
        {
            public SpriteRenderer source;
            public SpriteRenderer overlay;
        }

        private readonly List<Layer> layers = new List<Layer>();

        /// <summary>最近一次受击的时间。用负无穷表示当前没有闪红。</summary>
        private float lastHitTime = float.NegativeInfinity;

        /// <summary>本次渐入的起点红度，用于连续受击时从当前值接着拉满。</summary>
        private float rampStartValue;

        /// <summary>当前红度，0~1。</summary>
        private float intensity;

        private bool built;

        /// <summary>当前红度，0~1。0 表示没在闪。</summary>
        public float Intensity { get { return intensity; } }

        /// <summary>当前是否处于闪红中。</summary>
        public bool IsFlashing { get { return intensity > 0.0001f; } }

        private void Awake()
        {
            Rebuild();
        }

        private void OnEnable()
        {
            lastHitTime = float.NegativeInfinity;
            rampStartValue = 0f;
            intensity = 0f;

            if (!built)
                Rebuild();

            ApplyAlpha();
        }

        private void OnDisable()
        {
            // 池化复用时必须归零，否则下次取出来会带着上一只敌人的红。
            lastHitTime = float.NegativeInfinity;
            rampStartValue = 0f;
            intensity = 0f;
            ApplyAlpha();
        }

        /// <summary>
        /// 触发一次受击闪红。连续受击时从当前红度重新渐入，并重新开始保持窗口，
        /// 因此只要挨打不停就一直保持满红，停下后渐出。
        /// </summary>
        public void Flash()
        {
            if (!built)
                Rebuild();

            rampStartValue = intensity;
            lastHitTime = Time.time;
        }

        /// <summary>重建叠加层。角色新增/更换渲染器后调用。</summary>
        [ContextMenu("重建受击蒙皮")]
        public void Rebuild()
        {
            built = true;
            layers.Clear();

            var renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i];
                if (source == null || source.gameObject.name == OverlayName)
                    continue;

                layers.Add(new Layer { source = source, overlay = CreateOverlay(source) });
            }

            ApplyAlpha();
        }

        private SpriteRenderer CreateOverlay(SpriteRenderer source)
        {
            var go = new GameObject(OverlayName);
            go.layer = source.gameObject.layer;

            var transform = go.transform;
            transform.SetParent(source.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            var overlay = go.AddComponent<SpriteRenderer>();
            overlay.sprite = source.sprite;
            overlay.sharedMaterial = source.sharedMaterial;
            overlay.sortingLayerID = source.sortingLayerID;
            overlay.sortingOrder = source.sortingOrder + 1;
            overlay.color = WithAlpha(0f);

            return overlay;
        }

        private void LateUpdate()
        {
            // 动画在 Update 里换帧，这里在渲染前同步，保证蒙皮永远是当前帧。
            UpdateIntensity();
            SyncOverlays();
        }

        private void UpdateIntensity()
        {
            if (lastHitTime == float.NegativeInfinity)
            {
                intensity = 0f;
                return;
            }

            var elapsed = Time.time - lastHitTime;
            var total = fadeInDuration + holdDuration + fadeOutDuration;
            if (elapsed >= total)
            {
                lastHitTime = float.NegativeInfinity;
                intensity = 0f;
                return;
            }

            if (elapsed < fadeInDuration)
                intensity = Mathf.Lerp(rampStartValue, 1f, elapsed / fadeInDuration);
            else if (elapsed < fadeInDuration + holdDuration)
                intensity = 1f;
            else
                intensity = 1f - (elapsed - fadeInDuration - holdDuration) / fadeOutDuration;

            intensity = Mathf.Clamp01(intensity);
        }

        private void SyncOverlays()
        {
            var alpha = intensity * maxAlpha;

            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.source == null || layer.overlay == null)
                    continue;

                var source = layer.source;
                var overlay = layer.overlay;

                overlay.sprite = source.sprite;
                overlay.flipX = source.flipX;
                overlay.flipY = source.flipY;
                overlay.drawMode = source.drawMode;
                overlay.size = source.size;
                overlay.sortingLayerID = source.sortingLayerID;
                overlay.sortingOrder = source.sortingOrder + 1;
                overlay.enabled = source.enabled && alpha > 0.001f;
                overlay.color = WithAlpha(alpha);
            }
        }

        private void ApplyAlpha()
        {
            var alpha = intensity * maxAlpha;
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.overlay == null)
                    continue;

                layer.overlay.color = WithAlpha(alpha);
                layer.overlay.enabled = layer.source != null && layer.source.enabled && alpha > 0.001f;
            }
        }

        private Color WithAlpha(float alpha)
        {
            return new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
        }
    }
}
