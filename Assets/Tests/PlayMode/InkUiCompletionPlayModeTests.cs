using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class InkUiCompletionPlayModeTests
    {
        const string Previews = "InkUiCompletion/previews";
        readonly Dictionary<string, int?> _progress = new Dictionary<string, int?>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("BattleUI");
            foreach (var pair in _progress)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                else PlayerPrefs.DeleteKey(pair.Key);
            if (_progress.Count > 0) PlayerPrefs.Save();
            _progress.Clear();
        }

        static void AssertArtwork(Transform target, string name)
        {
            var image = target.GetComponent<Image>();
            Assert.IsTrue(image.enabled, target.name + " 的底图必须启用。");
            Assert.IsNotNull(image.sprite, target.name);
            Assert.AreEqual(name, image.sprite.name);
            Assert.AreEqual(Color.white, image.color, "保留水墨素材原色。");
            Assert.AreEqual(Image.Type.Sliced, image.type);
            if (target.TryGetComponent<Outline>(out var outline)) Assert.IsFalse(outline.enabled);
        }

        static void Click(Button button)
        {
            Assert.IsTrue(button.IsInteractable());
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position)
            };
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        [UnityTest]
        public IEnumerator 返回素材_宽窄窗口可见_点击与键盘返回保留()
        {
            yield return SceneManager.LoadSceneAsync("MainMenu");
            yield return null;
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var chapters = canvas.transform.Find("Navigation/LevelsPanel");
            var levels = canvas.transform.Find("Navigation/ChapterPanel");
            var chapterBack = chapters.Find("BackButton").GetComponent<Button>();
            var levelBack = levels.Find("BackButton").GetComponent<Button>();
            AssertArtwork(chapterBack.transform, "ChapterBack");
            AssertArtwork(levelBack.transform, "LevelBack");
            foreach (var button in new[] { chapterBack, levelBack })
            {
                Assert.AreSame(button.GetComponent<Image>(), button.targetGraphic);
                Assert.AreEqual(new Vector2(104, 46), ((RectTransform)button.transform).sizeDelta);
                Assert.AreEqual(0, button.transform.Find("Label").GetComponent<Text>().color.a,
                    "图片已包含返回题字，不应叠字。");
            }
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 720),
                new Vector2Int(1560, 720), new Vector2Int(1280, 540) })
            {
                menu.EnterMenu();
                menu.OpenLevels();
                yield return CaptureMenu(canvas, chapterBack, "chapters", size);
                menu.OpenChapter(0);
                yield return CaptureMenu(canvas, levelBack, "levels", size);
            }
            Click(levelBack);
            Assert.IsTrue(chapters.gameObject.activeSelf);
            Assert.AreEqual("Chapter01Button", EventSystem.current.currentSelectedGameObject.name);
            EventSystem.current.SetSelectedGameObject(chapterBack.gameObject);
            ExecuteEvents.Execute(chapterBack.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.IsTrue(canvas.transform.Find("Navigation/ModePage").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator 面板素材_计时文字和音乐控件可见_暂停继续保留()
        {
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Game");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var composition = ui.transform.Find("Composition");
            var timer = composition.Find("Controls/TimerPanel");
            Assert.IsFalse(timer.GetComponent<Image>().enabled);
            Assert.IsFalse(timer.Find("Heading").gameObject.activeSelf);
            var timeRect = (RectTransform)timer.Find("Time");
            Assert.AreEqual(0, timeRect.anchoredPosition.x);
            Assert.AreEqual(400, timeRect.sizeDelta.x, "纯时间文字在原面板区域居中。");
            AssertArtwork(composition.Find("SettingsPanel"), "PausePanel");
            AssertArtwork(composition.Find("ResultPanel/Dialog"), "ResultPanel");
            Assert.AreEqual("TaskPanel", composition.Find("Controls/TaskPanel").GetComponent<Image>().sprite.name);
            Assert.IsNotEmpty(timer.Find("Time").GetComponent<Text>().text);
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "game-timer", Previews);
            var settings = composition.Find("Controls/SettingsButton").GetComponent<Button>();
            EventSystem.current.SetSelectedGameObject(settings.gameObject);
            Click(settings);
            yield return null;
            Assert.IsTrue(session.Paused && ui.SettingsOpen);
            Assert.AreEqual(0, Time.timeScale);
            var panel = composition.Find("SettingsPanel");
            Assert.IsTrue(panel.Find("MusicSlider").GetComponent<Slider>().IsInteractable());
            Assert.IsTrue(GameAudio.Instance.transform.Find("Music").GetComponent<AudioSource>().isPlaying);
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "game-pause", Previews);
            Click(panel.Find("CloseButton").GetComponent<Button>());
            yield return null;
            Assert.IsFalse(session.Paused || ui.SettingsOpen);
            Assert.AreSame(settings.gameObject, EventSystem.current.currentSelectedGameObject);
        }

        [UnityTest]
        public IEnumerator 结算素材_胜负共用面板_动态标题和重开保留()
        {
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Game");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            string levelId = Object.FindFirstObjectByType<LevelLoader>().CurrentLevel.levelId;
            foreach (string key in new[] { "TapGJ.cleared." + levelId, "TapGJ.best." + levelId })
                _progress[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var composition = ui.transform.Find("Composition");
            foreach (bool victory in new[] { true, false })
            {
                var engine = new GameEngine(new Rules
                {
                    CreepsPerElement = 0, MaxMoveSteps = 0, InitialHandSize = 3, HandLimit = 3,
                    Goal = GoalSpec.Survive(victory ? 1 : 2), RoundLimit = victory ? 5 : 1
                }, 12);
                engine.Start();
                engine.EndPreparationNow();
                session.Begin(engine, 12);
                yield return WaitForPhase(session, GamePhase.PlayerAction);
                var next = composition.Find("Controls/NextRoundButton").GetComponent<Button>();
                Click(next);
                yield return WaitForPhase(session, GamePhase.Discard);
                Click(next);
                float until = Time.realtimeSinceStartup + 10;
                while (!engine.ResultReported && Time.realtimeSinceStartup < until) yield return null;
                Assert.IsTrue(engine.ResultReported);
                yield return null;
                var result = composition.Find("ResultPanel/Dialog");
                Assert.IsTrue(result.gameObject.activeInHierarchy);
                AssertArtwork(result, "ResultPanel");
                Assert.AreEqual(victory ? "通关成功" : "本局结束", result.Find("Heading").GetComponent<Text>().text);
                Assert.IsNotEmpty(result.Find("Message").GetComponent<Text>().text);
                yield return BattleActionButtonArtPlayModeTests.Capture(ui, victory ? "game-victory" : "game-defeat", Previews);
                Click(result.Find("RestartButton").GetComponent<Button>());
                yield return WaitForPhase(session, GamePhase.PlayerAction);
                Assert.IsFalse(composition.Find("ResultPanel").gameObject.activeSelf);
            }
        }

        static IEnumerator WaitForPhase(GameSession session, GamePhase phase)
        {
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Engine.Phase != phase || session.Busy) && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(phase, session.Engine.Phase);
            Assert.IsFalse(session.Busy);
            yield return null;
        }

        static IEnumerator CaptureMenu(Canvas canvas, Button button, string name, Vector2Int size)
        {
            var cameraObject = new GameObject("InkUiPreviewCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            bool originalScaler = scaler.enabled;
            var previous = RenderTexture.active;
            var target = new RenderTexture(size.x, size.y, 24);
            var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
            try
            {
                camera.orthographic = true;
                camera.orthographicSize = size.y * .5f;
                camera.cullingMask = 1 << 5;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.targetTexture = target;
                camera.backgroundColor = new Color(.055f, .085f, .105f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10;
                scaler.enabled = false;
                canvas.scaleFactor = 1;
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)canvas.transform;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(rect, button.transform);
                Assert.GreaterOrEqual(bounds.min.x, rect.rect.xMin - 1);
                Assert.LessOrEqual(bounds.max.x, rect.rect.xMax + 1);
                Assert.GreaterOrEqual(bounds.min.y, rect.rect.yMin - 1);
                Assert.LessOrEqual(bounds.max.y, rect.rect.yMax + 1);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                pixels.Apply();
                string directory = Path.GetFullPath("Artifacts/InkUiCompletion/previews");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, $"{name}-{size.x}x{size.y}.png"), pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                scaler.enabled = originalScaler;
                RenderTexture.active = previous;
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
                Object.Destroy(cameraObject);
            }
        }
    }
}
