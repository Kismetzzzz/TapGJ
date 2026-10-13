using System.Collections;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class MenuVisualPlayModeTests
    {
        [UnityTest]
        public IEnumerator 焦点描边_支持键盘和禁用状态_保留美术着色()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var entry = canvas.Find("Navigation/EntryPage/EnterButton").GetComponent<Button>();
            var image = entry.GetComponent<Image>();
            var ring = entry.transform.Find("FocusRing").GetComponent<CanvasGroup>();
            image.color = Color.yellow;
            EventSystem.current.SetSelectedGameObject(entry.gameObject);
            yield return null;
            Assert.AreEqual(1f, ring.alpha);
            Assert.AreEqual(Color.yellow, image.color);
            EventSystem.current.SetSelectedGameObject(null);
            yield return null;
            Assert.AreEqual(0f, ring.alpha);
            EventSystem.current.SetSelectedGameObject(entry.gameObject);
            entry.interactable = false;
            yield return null;
            Assert.AreEqual(0f, ring.alpha, "禁用按钮不能显示可操作的焦点描边");
            Assert.AreEqual(Color.yellow, image.color);
        }

        [UnityTest]
        public IEnumerator 窄屏_保留模板字号_缩小可见区域并允许横向滚动()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(960, 720);
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.EnterMenu();
            controller.OpenLevels();
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var panel = (RectTransform)canvas.transform.Find("Navigation/LevelsPanel");
            var scroll = panel.Find("ScrollView").GetComponent<ScrollRect>();
            Assert.LessOrEqual(panel.rect.width, rect.rect.width - 60, "选择页应留出屏幕边距");
            var template = canvas.transform.Find("Navigation/CardTemplates/ChapterCard/Description").GetComponent<Text>();
            foreach (var text in scroll.content.GetComponentsInChildren<Text>())
                if (text.name == "Description") Assert.AreEqual(template.fontSize, text.fontSize, "窄屏应保留可读字号");
            Assert.Greater(scroll.content.rect.width, scroll.viewport.rect.width, "卡片超出窄屏时应允许横向滚动");
            Assert.IsTrue(scroll.horizontal);
            var leaderboard = (RectTransform)canvas.transform.Find("LeaderboardPanel");
            Assert.LessOrEqual(leaderboard.sizeDelta.x * leaderboard.localScale.x, rect.rect.width - 60,
                "窄屏弹窗应保留安全边距");
        }
    }
}
