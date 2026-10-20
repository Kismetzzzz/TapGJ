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
            Assert.IsTrue(image.preserveAspect, "完整插画必须等比显示");
            float visibleHeight = Mathf.Min(image.rectTransform.rect.height,
                image.rectTransform.rect.width * image.sprite.rect.height / image.sprite.rect.width);
            Assert.AreEqual(72, visibleHeight, .1f, "两张插画的显示高度必须一致");
            var rect = (RectTransform)button.transform;
            Assert.AreEqual(1896, rect.anchoredPosition.x + rect.rect.width, .1f, "按钮右边缘必须对齐");
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
            yield return Capture(ui, "preview");
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
            yield return Capture(ui, "game-player-action");
            Click(next);
            yield return WaitForPhase(session, GamePhase.Discard);
            Assert.IsFalse(next.transform.Find("Artwork").gameObject.activeSelf);
            Assert.IsTrue(next.GetComponent<Image>().enabled);
            var label = next.GetComponentInChildren<Text>();
            Assert.IsTrue(label.enabled);
            Assert.AreEqual("结束弃牌", label.text, "阶段变化后不能继续显示结束出牌插画");
            Assert.IsFalse(discard.IsInteractable());
            int count = engine.Hand.Count;
            ui.SelectCard(0);
            yield return null;
            AssertArtwork(discard, "Discard");
            yield return Capture(ui, "game-discard");
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
