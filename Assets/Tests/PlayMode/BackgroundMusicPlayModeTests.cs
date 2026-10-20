using System.Collections;
using NUnit.Framework;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public class BackgroundMusicPlayModeTests
    {
        const string MusicKey = "TapGJ.Audio.MusicVolume";
        const string EffectsKey = "TapGJ.Audio.EffectsVolume";
        bool _hadMusic, _hadEffects;
        float _music, _effects, _timeScale;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu != null) menu.enabled = false;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            if (ui != null) ui.CloseSettings();
            _hadMusic = PlayerPrefs.HasKey(MusicKey);
            _hadEffects = PlayerPrefs.HasKey(EffectsKey);
            _music = PlayerPrefs.GetFloat(MusicKey);
            _effects = PlayerPrefs.GetFloat(EffectsKey);
            _timeScale = Time.timeScale;
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            PlayerPrefs.SetFloat(MusicKey, .7f);
            PlayerPrefs.SetFloat(EffectsKey, .6f);
        }

        [UnityTearDown]
        public IEnumerator Teardown()
        {
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu != null) menu.enabled = false;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            if (ui != null) ui.CloseSettings();
            if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            if (_hadMusic) PlayerPrefs.SetFloat(MusicKey, _music); else PlayerPrefs.DeleteKey(MusicKey);
            if (_hadEffects) PlayerPrefs.SetFloat(EffectsKey, _effects); else PlayerPrefs.DeleteKey(EffectsKey);
            PlayerPrefs.Save();
            GameLaunchContext.Clear();
            Time.timeScale = _timeScale;
        }

        static AudioSource Music => GameAudio.Instance.transform.Find("Music").GetComponent<AudioSource>();

        static void AssertPlaying()
        {
            Assert.IsNotNull(GameAudio.Instance);
            Assert.AreEqual(1, Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None).Length);
            Assert.IsNotNull(Music.clip);
            Assert.AreEqual("BackgroundMusic", Music.clip.name);
            Assert.Greater(Music.clip.length, 0);
            Assert.IsTrue(Music.loop);
            Assert.IsTrue(Music.isPlaying);
            Assert.IsFalse(Music.playOnAwake, "先应用保存音量，再开始播放。");
            Assert.AreEqual(0, Music.spatialBlend);
            Assert.AreEqual(1, Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length);
        }

        [UnityTest]
        public IEnumerator 背景音乐_菜单进对局再返回时保持同一播放器和进度()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            AssertPlaying();
            Assert.AreEqual(.7f, Music.volume, .001f);
            var original = GameAudio.Instance;
            Music.time = 5;
            foreach (string scene in new[] { "Game", "MainMenu" })
            {
                yield return SceneManager.LoadSceneAsync(scene);
                yield return null;
                AssertPlaying();
                Assert.AreSame(original, GameAudio.Instance);
                Assert.GreaterOrEqual(Music.time, 4.9f, "换场景不能从头播放。");
            }
        }

        [UnityTest]
        public IEnumerator 背景音乐_直接打开局内场景也自动播放()
        {
            foreach (string scene in new[] { "Game", "BattleUI" })
            {
                if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(scene);
                yield return null;
                AssertPlaying();
                Assert.AreEqual(.7f, Music.volume, .001f);
            }
        }

        [UnityTest]
        public IEnumerator 局内音乐滑块_暂停时即时生效_静音和重新启动后保留()
        {
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            ui.OpenSettings();
            yield return null;
            Assert.AreEqual(0, Time.timeScale);
            AssertPlaying();
            var panel = ui.transform.Find("Composition/SettingsPanel");
            var slider = panel.Find("MusicSlider").GetComponent<Slider>();
            var value = panel.Find("MusicValue").GetComponent<Text>();
            Assert.AreEqual(.7f, slider.value, .001f);
            slider.value = .25f;
            Assert.AreEqual(.25f, Music.volume, .001f);
            Assert.AreEqual("25%", value.text);
            Assert.AreEqual(.6f, GameAudio.Instance.EffectsVolume, .001f);
            slider.value = 0;
            Assert.AreEqual(0, Music.volume);
            Assert.AreEqual("0%", value.text);
            slider.value = 1;
            Assert.AreEqual(1, Music.volume);
            slider.value = 0;
            ui.CloseSettings();
            Assert.AreEqual(0, PlayerPrefs.GetFloat(MusicKey));
            Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            AssertPlaying();
            Assert.AreEqual(0, Music.volume);
            Object.FindFirstObjectByType<MainMenuController>().OpenSettings();
            var menuSlider = Object.FindFirstObjectByType<Canvas>()
                .transform.Find("SettingsPanel/MusicSlider").GetComponent<Slider>();
            Assert.AreEqual(0, menuSlider.value);
            menuSlider.value = .45f;
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null;
            ui = Object.FindFirstObjectByType<BattleUiController>();
            ui.OpenSettings();
            slider = ui.transform.Find("Composition/SettingsPanel/MusicSlider").GetComponent<Slider>();
            Assert.AreEqual(.45f, slider.value, .001f);
            Assert.AreEqual(.45f, Music.volume, .001f);
            ui.CloseSettings();
        }
    }
}
