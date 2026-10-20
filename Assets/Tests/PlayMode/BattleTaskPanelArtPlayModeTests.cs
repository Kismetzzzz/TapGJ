using System.Collections;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class BattleTaskPanelArtPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
        }

        static Transform AssertPanel(BattleUiController ui)
        {
            var panel = ui.transform.Find("Composition/Controls/TaskPanel");
            var image = panel.GetComponent<Image>();
            Assert.AreEqual("TaskPanel", image.sprite.name);
            Assert.AreEqual(Image.Type.Simple, image.type);
            Assert.AreEqual(Color.white, image.color);
            Assert.IsTrue(image.preserveAspect);
            Assert.IsFalse(panel.GetComponent<Outline>().enabled);
            Assert.IsFalse(panel.Find("Heading").GetComponent<Text>().enabled, "不能重复叠加插画自带的题字");
            var rect = (RectTransform)panel;
            Assert.AreEqual(image.sprite.rect.width / image.sprite.rect.height,
                rect.rect.width / rect.rect.height, .001f, "面板必须保持原图比例");
            foreach (var graphic in panel.GetComponentsInChildren<Graphic>())
                Assert.IsFalse(graphic.raycastTarget, "任务面板装饰不能拦截操作");
            return panel;
        }

        static void AssertContent(Transform panel, GoalProgress expected)
        {
            var objective = panel.Find("Objective").GetComponent<Text>();
            var progress = panel.Find("Progress").GetComponent<Text>();
            Assert.AreEqual(expected.Description, objective.text);
            Assert.AreEqual(expected.Label, progress.text);
            Assert.AreEqual(expected.Fill, panel.Find("ProgressFill").GetComponent<Image>().fillAmount, .001f);
            foreach (var text in new[] { objective, progress })
            {
                Assert.IsTrue(text.enabled);
                Canvas.ForceUpdateCanvases();
                var settings = text.GetGenerationSettings(text.rectTransform.rect.size);
                settings.resizeTextForBestFit = false;
                settings.fontSize = text.cachedTextGenerator.fontSizeUsedForBestFit;
                float height = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
                Assert.LessOrEqual(height, text.rectTransform.rect.height + 1, "较长关卡文字必须完整显示");
            }
            var objectiveRect = objective.rectTransform;
            var progressRect = progress.rectTransform;
            Assert.LessOrEqual(-objectiveRect.anchoredPosition.y + objectiveRect.rect.height,
                -progressRect.anchoredPosition.y, "目标和进度不能重叠");
        }

        [UnityTest]
        public IEnumerator 预览面板_原图标题不重复_长目标和五行进度完整显示()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var panel = AssertPanel(ui);
            var progress = new GoalProgress
            {
                Description = "场上出现过的元素数量都相等（只剩一种也算）",
                Label = "金 12　木 12　水 12　火 12　土 12（差 0）",
                Fill = .75f
            };
            ui.SetGoalProgress(progress);
            yield return null;
            AssertContent(panel, progress);
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "long-objective", "TaskPanelArt");
        }

        [UnityTest]
        public IEnumerator 真实任务面板_显示当前关卡_推进回合后更新进度()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForAction(session);
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var panel = AssertPanel(ui);
            AssertContent(panel, session.Engine.GetGoalProgress());
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "game", "TaskPanelArt");
            var engine = new GameEngine(new Rules
            {
                CreepsPerElement = 0, MaxMoveSteps = 0, InitialHandSize = 0, HandLimit = 0,
                Goal = GoalSpec.Survive(500), RoundLimit = 500
            }, 12);
            engine.Start();
            engine.EndPreparationNow();
            session.Begin(engine, 12);
            yield return WaitForAction(session);
            AssertContent(panel, engine.GetGoalProgress());
            string before = panel.Find("Progress").GetComponent<Text>().text;
            session.EndPlayerRound();
            float until = Time.realtimeSinceStartup + 10;
            while (session.Busy && Time.realtimeSinceStartup < until) yield return null;
            session.EndDiscardRound();
            yield return WaitForAction(session);
            Assert.Greater(engine.Round, 1);
            AssertContent(panel, engine.GetGoalProgress());
            Assert.AreNotEqual(before, panel.Find("Progress").GetComponent<Text>().text);
        }

        static IEnumerator WaitForAction(GameSession session)
        {
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Busy || session.Engine.Phase != GamePhase.PlayerAction)
                   && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(session.Busy);
            Assert.AreEqual(GamePhase.PlayerAction, session.Engine.Phase);
            yield return null;
        }
    }
}
