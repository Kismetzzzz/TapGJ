using UnityEngine;

namespace TapGJ.View
{
    /// <summary>独立的音乐/音效声道。随场景保留，音频素材在 Inspector 中指定。</summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GameAudio : MonoBehaviour
    {
        const string MusicKey = "TapGJ.Audio.MusicVolume";
        const string EffectsKey = "TapGJ.Audio.EffectsVolume";

        public static GameAudio Instance { get; private set; }

        [SerializeField] AudioSource musicSource;
        [SerializeField] AudioSource effectsSource;

        public float MusicVolume { get; private set; } = 0.8f;
        public float EffectsVolume { get; private set; } = 0.8f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstance() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            SetMusicVolume(PlayerPrefs.GetFloat(MusicKey, 0.8f));
            SetEffectsVolume(PlayerPrefs.GetFloat(EffectsKey, 0.8f));
        }

        public void SetMusicVolume(float volume)
        {
            MusicVolume = Normalize(volume);
            if (musicSource != null) musicSource.volume = MusicVolume;
        }

        public void SetEffectsVolume(float volume)
        {
            EffectsVolume = Normalize(volume);
            if (effectsSource != null) effectsSource.volume = EffectsVolume;
        }

        public void PlayEffect(AudioClip clip)
        {
            if (effectsSource != null && clip != null) effectsSource.PlayOneShot(clip);
        }

        public void SaveSettings()
        {
            PlayerPrefs.SetFloat(MusicKey, MusicVolume);
            PlayerPrefs.SetFloat(EffectsKey, EffectsVolume);
            PlayerPrefs.Save();
        }

        static float Normalize(float volume)
        {
            return float.IsNaN(volume) || float.IsInfinity(volume) ? 0.8f : Mathf.Clamp01(volume);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Instance == this) SaveSettings();
        }

        void OnApplicationQuit()
        {
            if (Instance == this) SaveSettings();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
