using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class BattleActionButtonArtPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
        }

        static Button ButtonAt(BattleUiController ui, string name) =>
            ui.transform.Find("Composition/Controls/" + name).GetComponent<Button>();

        static void AssertArtwork(Button button, string spriteName)
        {
            var image = (Image)button.targetGraphic;
            Assert.IsTrue(image.enabled && image.gameObject.activeInHierarchy);
            Assert.AreEqual(spriteName, image.sprite.name);
            Assert.IsNotNull(image.GetComponent<ActionButtonArtwork>(), "操作插画只应延展两侧底纹");
            Assert.AreEqual(300, image.rectTransform.rect.width, .1f, "三种操作插画使用相同容器宽度");
            Assert.AreEqual(72, image.rectTransform.rect.height, .1f, "三种操作插画使用相同容器高度");
            Canvas.ForceUpdateCanvases();
            var mesh = image.canvasRenderer.GetMesh();
            Assert.IsNotNull(mesh);
            Assert.AreEqual(20, mesh.vertexCount);
            Assert.AreEqual(300, mesh.bounds.size.x, .1f, "三种操作按钮的可见外框必须等宽");
            Assert.AreEqual(72, mesh.bounds.size.y, .1f, "三种操作按钮的可见外框必须等高");
            var vertices = mesh.vertices;
            var uvs = mesh.uv;
            var sourceUv = UnityEngine.Sprites.DataUtility.GetOuterUV(image.sprite);
            foreach (int section in new[] { 0, 2, 4 })
            {
                int i = section * 4;
                float sourceWidth = (uvs[i + 2].x - uvs[i + 1].x) / (sourceUv.z - sourceUv.x) * image.sprite.rect.width;
                float sourceHeight = (uvs[i + 1].y - uvs[i].y) / (sourceUv.w - sourceUv.y) * image.sprite.rect.height;
                Assert.AreEqual((vertices[i + 1].y - vertices[i].y) / sourceHeight,
                    (vertices[i + 2].x - vertices[i + 1].x) / sourceWidth, .001f,
                    "题字和两端轮廓必须等比显示，不能随按钮外框拉伸");
            }
            Assert.AreEqual(image.rectTransform.rect.xMax, mesh.bounds.max.x, .1f,
                "可见外框仍应靠右对齐");
            var rect = (RectTransform)button.transform;
            Assert.AreEqual(new Vector2(300, 82), rect.sizeDelta, "点击区域同样保持一致");
            Assert.LessOrEqual(rect.anchoredPosition.x + rect.rect.width, 1920,
                "按钮插画必须完整留在画面内");
            Assert.AreEqual(1896, rect.anchoredPosition.x + rect.rect.width, .1f, "操作按钮右边缘必须对齐");
            if (image.transform != button.transform)
                Assert.AreEqual(rect.TransformPoint(new Vector3(rect.rect.xMax, 0)).x,
                    image.rectTransform.TransformPoint(new Vector3(image.rectTransform.rect.xMax, 0)).x,
                    .1f, "插画应跟随按钮在场景中的右边缘");
            Assert.IsFalse(button.GetComponentInChildren<Text>().enabled, "不能重复叠加图片中已有的题字");
        }

        static void Click(Button button)
        {
            Assert.IsTrue(button.IsInteractable());
            var rect = (RectTransform)button.transform;
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center))
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits, "图片中心必须可点击");
            Assert.AreSame(button, hits[0].gameObject.GetComponentInParent<Button>());
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static IEnumerator WaitForPhase(GameSession session, GamePhase phase)
        {
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Busy || session.Engine.Phase != phase || session.Engine.AwaitingStepPlayback)
                   && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(phase, session.Engine.Phase);
            Assert.IsFalse(session.Busy);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 预览场景_两张按钮插画等比显示且真实点击仍有效()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var next = ButtonAt(ui, "NextRoundButton");
            var discard = ButtonAt(ui, "DiscardButton");
            AssertArtwork(next, "EndPlayerPhase");
            AssertArtwork(discard, "Discard");
            Assert.IsFalse(discard.IsInteractable(), "未选择手牌不能弃牌");
            ui.SelectCard(0);
            Canvas.ForceUpdateCanvases();
            Click(discard);
            Assert.IsFalse(ui.Cards[0].gameObject.activeSelf);
            int round = ui.Round;
            Click(next);
            Assert.AreEqual(round + 1, ui.Round);
            yield return Capture(ui, "preview", "BattleButtonStyles");
        }

        [UnityTest]
        public IEnumerator 真实对局_结束出牌后显示结束弃牌_下一回合恢复插画()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var engine = new GameEngine(new Rules
            {
                CreepsPerElement = 0, MaxMoveSteps = 0, InitialHandSize = 3, HandLimit = 3,
                Goal = GoalSpec.Survive(500), RoundLimit = 500
            }, 12);
            engine.Start();
            engine.EndPreparationNow();
            session.Begin(engine, 12);
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var next = ButtonAt(ui, "NextRoundButton");
            var discard = ButtonAt(ui, "DiscardButton");
            AssertArtwork(next, "EndPlayerPhase");
            AssertArtwork(discard, "Discard");
            yield return Capture(ui, "game-player-action", "BattleButtonStyles");
            Click(next);
            yield return WaitForPhase(session, GamePhase.Discard);
            AssertArtwork(next, "EndDiscardPhase");
            Assert.IsFalse(next.GetComponent<Image>().enabled);
            var hitArea = next.transform.Find("HitArea").GetComponent<Image>();
            Assert.IsTrue(hitArea.raycastTarget && hitArea.enabled);
            Assert.AreEqual(82, hitArea.rectTransform.rect.height, .1f, "切换插画后保留完整点击高度");
            var label = next.GetComponentInChildren<Text>();
            Assert.IsFalse(label.enabled);
            Assert.AreEqual("结束弃牌", label.text, "阶段变化后不能继续显示结束出牌插画");
            ui.gameObject.SetActive(false);
            ui.gameObject.SetActive(true);
            AssertArtwork(next, "EndDiscardPhase");
            Assert.IsFalse(discard.IsInteractable());
            int count = engine.Hand.Count;
            ui.SelectCard(0);
            yield return null;
            AssertArtwork(discard, "Discard");
            yield return Capture(ui, "game-discard", "BattleButtonStyles");
            Click(discard);
            Assert.AreEqual(count - 1, engine.Hand.Count);
            yield return null;
            Click(next);
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            AssertArtwork(next, "EndPlayerPhase");
            ui.gameObject.SetActive(false);
            ui.gameObject.SetActive(true);
            AssertArtwork(next, "EndPlayerPhase");
        }

        static void AssertSkin(Button button, string name)
        {
            var image = (Image)button.targetGraphic;
            Assert.AreEqual(name, image.sprite.name);
            Assert.IsTrue(image.preserveAspect && image.raycastTarget);
            Assert.IsFalse(button.GetComponentInChildren<Text>(true).enabled);
            Assert.IsNotNull(button.GetComponent<MenuButtonFeedback>());
        }

        [UnityTest]
        public IEnumerator 暂停与结算_按钮插画保留点击与焦点()
        {
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var engine = new GameEngine(new Rules
            {
                CreepsPerElement = 0, MaxMoveSteps = 0, InitialHandSize = 3, HandLimit = 3,
                Goal = GoalSpec.Survive(1), RoundLimit = 5
            }, 12);
            engine.Start();
            engine.EndPreparationNow();
            session.Begin(engine, 12);
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var composition = ui.transform.Find("Composition");
            var settings = ButtonAt(ui, "SettingsButton");
            AssertSkin(settings, "Settings");
            Click(settings);
            yield return null;
            Assert.IsTrue(session.Paused && ui.SettingsOpen);
            var pause = composition.Find("SettingsPanel");
            var close = pause.Find("CloseButton").GetComponent<Button>();
            AssertSkin(close, "Continue");
            AssertSkin(pause.Find("RestartButton").GetComponent<Button>(), "Restart");
            AssertSkin(pause.Find("MenuButton").GetComponent<Button>(), "ReturnMenu");
            Assert.AreSame(close.gameObject, EventSystem.current.currentSelectedGameObject);
            yield return Capture(ui, "pause", "BattleButtonStyles");
            Click(close);
            yield return null;
            Assert.IsFalse(session.Paused || ui.SettingsOpen);
            Assert.AreSame(settings.gameObject, EventSystem.current.currentSelectedGameObject);
            var next = ButtonAt(ui, "NextRoundButton");
            Click(next);
            yield return WaitForPhase(session, GamePhase.Discard);
            Click(next);
            float until = Time.realtimeSinceStartup + 10;
            while (!engine.ResultReported && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsTrue(engine.ResultReported);
            yield return null;
            var result = composition.Find("ResultPanel/Dialog");
            var restart = result.Find("RestartButton").GetComponent<Button>();
            AssertSkin(restart, "Restart");
            AssertSkin(result.Find("MenuButton").GetComponent<Button>(), "ReturnMenu");
            AssertSkin(result.Find("NextLevelButton").GetComponent<Button>(), "NextLevel");
            Assert.AreSame(restart.gameObject, EventSystem.current.currentSelectedGameObject);
            yield return Capture(ui, "result", "BattleButtonStyles");
            Click(restart);
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            Assert.IsFalse(composition.Find("ResultPanel").gameObject.activeSelf);
        }

        internal static IEnumerator Capture(BattleUiController ui, string name, string folder = "BattleActionButtons")
        {
            var canvas = ui.GetComponent<Canvas>();
            var camera = Camera.main;
            var originalMode = canvas.renderMode;
            var originalCamera = canvas.worldCamera;
            float originalDistance = canvas.planeDistance;
            int originalMask = camera.cullingMask;
            var originalTarget = camera.targetTexture;
            var previous = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24);
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = ~0;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return new WaitForSecondsRealtime(.2f);
                Canvas.ForceUpdateCanvases();
                ui.GetComponent<BattleUiLayout>().Refresh();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                Assert.Greater(pixels.GetPixel(960, 540).grayscale, .2f, "运行截图必须包含实际界面");
                string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", folder));
                Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            }
            finally
            {
                canvas.renderMode = originalMode;
                canvas.worldCamera = originalCamera;
                canvas.planeDistance = originalDistance;
                camera.targetTexture = originalTarget;
                camera.cullingMask = originalMask;
                RenderTexture.active = previous;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
            yield return null;
        }
    }
}
