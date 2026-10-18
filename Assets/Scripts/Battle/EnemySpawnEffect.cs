using System;
using System.Collections;
using UnityEngine;

namespace TideBorne.Battle
{
    /// <summary>
    /// 敌人生成水花：在生成点播一次水柱动画，播完回调、然后销毁自己。
    ///
    /// 刷怪器与产兵组件都走 <see cref="TryPlay"/>：先在水花位置播完整段动画（当前 1 秒），
    /// 动画结束后才真正把敌人放出来。prefab 放在 <c>Resources/Effects/EnemySpawn</c>，
    /// 这样不用给每个场景/每个敌人 prefab 逐个接线。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/战斗/敌人生成水花（EnemySpawnEffect）")]
    public class EnemySpawnEffect : MonoBehaviour
    {
        /// <summary>水花 prefab 的 Resources 路径（不含扩展名）。</summary>
        public const string PrefabPath = "Effects/EnemySpawn";

        /// <summary>动画找不到时的兜底时长，秒（与 12 帧 @ 12fps 的实际时长一致）。</summary>
        private const float FallbackDuration = 1f;

        private static GameObject s_Prefab;

        private Animator _animator;
        private Coroutine _running;
        private Action _finished;

        /// <summary>
        /// 在指定位置播一次水花。<paramref name="onFinished"/> 在动画结束后调用。
        /// 返回是否成功播放；找不到 prefab 时返回 false，调用方应当直接生成敌人。
        /// </summary>
        public static bool TryPlay(Vector2 position, Action onFinished)
        {
            if (s_Prefab == null)
                s_Prefab = Resources.Load<GameObject>(PrefabPath);

            if (s_Prefab == null)
                return false;

            var instance = Instantiate(s_Prefab, new Vector3(position.x, position.y, 0f), Quaternion.identity);
            var effect = instance.GetComponent<EnemySpawnEffect>();
            if (effect == null)
            {
                Destroy(instance);
                return false;
            }

            effect.Play(onFinished);
            return true;
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        /// <summary>开始播放，播完调用 <paramref name="onFinished"/> 并销毁自己。</summary>
        public void Play(Action onFinished)
        {
            _finished = onFinished;

            if (_running != null)
                StopCoroutine(_running);

            _running = StartCoroutine(FinishAfterClip());
        }

        private IEnumerator FinishAfterClip()
        {
            var duration = ResolveDuration();
            if (duration > 0f)
                yield return new WaitForSeconds(duration);
            else
                yield return null;

            _running = null;

            var callback = _finished;
            _finished = null;
            callback?.Invoke();

            Destroy(gameObject);
        }

        /// <summary>取动画长度；取不到就用兜底值，保证敌人不会因为动画缺失而永远不出来。</summary>
        private float ResolveDuration()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            var controller = _animator != null ? _animator.runtimeAnimatorController : null;
            if (controller == null || controller.animationClips == null || controller.animationClips.Length == 0)
                return FallbackDuration;

            return controller.animationClips[0].length;
        }
    }
}
