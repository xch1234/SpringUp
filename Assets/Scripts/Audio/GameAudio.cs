using UnityEngine;

namespace TideBorne.Audio
{
    /// <summary>
    /// 全局音频：一条循环的 BGM 音轨 + 一条 2D 音效轨。
    ///
    /// 挂在 Bootstrap 场景（它常驻、内容场景是 additive 加载的），所以切场景不会打断音乐。
    /// 战斗与休整/标题的 BGM 由 <c>FlowApplication</c> 在切场景完成后通知切换。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("潮涌之躯/音频/游戏音频（GameAudio）")]
    public class GameAudio : MonoBehaviour
    {
        /// <summary>当前实例。Bootstrap 只有一个，便于刷怪/受击这类无引用调用。</summary>
        public static GameAudio Instance { get; private set; }

        [Header("音乐")]
        [Tooltip("标题与休整界面的 BGM。")]
        [SerializeField] private AudioClip restMusic;

        [Tooltip("战斗界面的 BGM。")]
        [SerializeField] private AudioClip battleMusic;

        [Header("音效")]
        [Tooltip("敌人受击音效。")]
        [SerializeField] private AudioClip enemyHitSfx;

        [Header("音量")]
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.55f;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.85f;

        /// <summary>同时命中多个敌人时，避免同一帧叠出好几层受击音。</summary>
        private const float HitSfxMinInterval = 0.04f;

        private AudioSource _musicSource;
        private AudioSource _sfxSource;
        private AudioClip _currentMusic;
        private float _lastHitSfxTime = float.NegativeInfinity;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            musicVolume = SpringUp.Flow.GameSettings.Music;
            sfxVolume = SpringUp.Flow.GameSettings.Sfx;
            SpringUp.Flow.GameSettings.Changed += ApplyVolumes;

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
            _musicSource.volume = musicVolume;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.volume = sfxVolume;
        }

        private void OnDestroy()
        {
            SpringUp.Flow.GameSettings.Changed -= ApplyVolumes;
            if (Instance == this)
                Instance = null;
        }

        /// <summary>按当前内容切 BGM：战斗用战场曲，其余（标题 / 休整）用休整曲。</summary>
        public void SetBattle(bool battle)
        {
            PlayMusic(battle ? battleMusic : restMusic);
        }

        /// <summary>播放一次敌人受击音效（木桩也算敌人）。</summary>
        public void PlayEnemyHit()
        {
            if (enemyHitSfx == null)
                return;

            if (Time.unscaledTime - _lastHitSfxTime < HitSfxMinInterval)
                return;

            _lastHitSfxTime = Time.unscaledTime;
            _sfxSource.PlayOneShot(enemyHitSfx);
        }

        void ApplyVolumes()
        {
            musicVolume = SpringUp.Flow.GameSettings.Music; sfxVolume = SpringUp.Flow.GameSettings.Sfx;
            if (_musicSource != null) _musicSource.volume = musicVolume;
            if (_sfxSource != null) _sfxSource.volume = sfxVolume;
        }
        public void PreviewSfx() => PlayEnemyHit();

        private void PlayMusic(AudioClip clip)
        {
            if (clip == null || _currentMusic == clip)
                return;

            _currentMusic = clip;
            _musicSource.clip = clip;
            _musicSource.volume = musicVolume;
            _musicSource.Play();
        }
    }
}
