using System.Collections;
using System.Collections.Generic;
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
        public IEnumerator 选择页山水背景_只在章节关卡显示_宽窄屏铺满且不挡点击()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var navigation = canvas.transform.Find("Navigation");
            var background = navigation.Find("SelectionBackground").GetComponent<Image>();
            Assert.IsNotNull(background.sprite);
            Assert.IsFalse(background.raycastTarget, "背景不能拦截卡片和按钮点击");
            Assert.IsFalse(background.gameObject.activeSelf, "入口页沿用自己的美术");
            controller.EnterMenu();
            Assert.IsFalse(background.gameObject.activeInHierarchy, "模式页使用自己的题字画面");
            Assert.IsTrue(navigation.Find("ModePage").gameObject.activeSelf);
            controller.OpenLevels();
            Assert.IsTrue(background.gameObject.activeInHierarchy);
            Assert.AreEqual(0f, navigation.Find("LevelsPanel").GetComponent<Image>().color.a,
                "章节页不能用白底遮住山水");
            Assert.Less(background.transform.GetSiblingIndex(), navigation.Find("LevelsPanel").GetSiblingIndex());
            Assert.Less(background.transform.GetSiblingIndex(), navigation.Find("ChapterPanel").GetSiblingIndex());

            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = (RectTransform)canvas.transform;
            foreach (var size in new[] { new Vector2(1280, 720), new Vector2(960, 720),
                new Vector2(1560, 720), new Vector2(1280, 540) })
            {
                canvasRect.sizeDelta = size;
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, background.transform);
                Assert.LessOrEqual(bounds.min.x, canvasRect.rect.xMin + 1, "背景左侧不能留白");
                Assert.GreaterOrEqual(bounds.max.x, canvasRect.rect.xMax - 1, "背景右侧不能留白");
                Assert.LessOrEqual(bounds.min.y, canvasRect.rect.yMin + 1, "背景下侧不能留白");
                Assert.GreaterOrEqual(bounds.max.y, canvasRect.rect.yMax - 1, "背景上侧不能留白");
                Assert.AreEqual(background.sprite.rect.width / background.sprite.rect.height,
                    background.rectTransform.rect.width / background.rectTransform.rect.height, .002f,
                    "背景必须等比铺满");
            }
            controller.OpenChapter(0);
            Assert.IsTrue(background.gameObject.activeInHierarchy);
            controller.OpenSettings();
            controller.ClosePanels();
            Assert.IsTrue(background.gameObject.activeInHierarchy, "关闭弹窗后保留选择页背景");
            controller.Back();
            Assert.IsTrue(background.gameObject.activeInHierarchy);
            controller.Back();
            Assert.IsFalse(background.gameObject.activeSelf, "返回入口后隐藏选择页背景");
        }

        [UnityTest]
        public IEnumerator 章节插画反馈_悬停提亮_按下微缩_点击区域和美术配置保持稳定()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.EnterMenu();
            controller.OpenLevels();
            yield return null;
            var button = Object.FindFirstObjectByType<Canvas>().transform
                .Find("Navigation/LevelsPanel/ScrollView/Viewport/Content").GetComponentInChildren<Button>();
            var art = button.transform.Find("Artwork").GetComponent<Image>();
            var hitArea = (RectTransform)button.transform.Find("ArtworkHitArea");
            var ring = button.transform.Find("FocusRing").GetComponent<CanvasGroup>();
            var feedback = button.GetComponent<MenuButtonFeedback>();
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            EventSystem.current.SetSelectedGameObject(null);
            feedback.OnPointerExit(pointer);
            yield return new WaitForSecondsRealtime(.15f);
            var originalScale = art.rectTransform.localScale;
            var originalHitSize = hitArea.sizeDelta;
            var originalHitScale = hitArea.localScale;
            var originalHitPosition = hitArea.position;
            var originalColor = art.color;
            var originalTint = art.material.GetColor("_Color");
            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                feedback.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.Greater(art.material.GetColor("_Color").r, originalTint.r, "悬停应轻微提亮");
                Assert.AreEqual(0f, ring.alpha, .001f, "悬停不能叠圆角外框");
                Assert.AreEqual(originalScale, art.rectTransform.localScale);

                feedback.OnPointerDown(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Right });
                yield return new WaitForSecondsRealtime(.15f);
                Assert.AreEqual(originalScale, art.rectTransform.localScale, "右键不应触发按下动画");
                feedback.OnPointerDown(pointer);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.Less(Vector3.Distance(originalScale * .97f, art.rectTransform.localScale), .001f);
                Assert.AreEqual(originalHitSize, hitArea.sizeDelta);
                Assert.AreEqual(originalHitScale, hitArea.localScale);
                Assert.AreEqual(originalHitPosition, hitArea.position);
                Assert.AreEqual(originalColor, art.color, "反馈不能改写原图着色");
                pointer.position = RectTransformUtility.WorldToScreenPoint(null,
                    hitArea.TransformPoint(new Vector3(hitArea.rect.xMax * .99f, 0)));
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.IsNotEmpty(hits);
                Assert.AreSame(button, hits[0].gameObject.GetComponentInParent<Button>(), "微缩后的原始边缘仍可点击");

                feedback.OnPointerUp(pointer);
                feedback.OnPointerExit(pointer);
                yield return new WaitForSecondsRealtime(.15f);
                Assert.AreEqual(originalScale, art.rectTransform.localScale);
                Assert.AreEqual(originalTint, art.material.GetColor("_Color"));
                Assert.AreEqual(0f, ring.alpha);

                feedback.OnPointerDown(pointer);
                yield return new WaitForSecondsRealtime(.15f);
                button.interactable = false;
                yield return null;
                Assert.AreEqual(originalScale, art.rectTransform.localScale, "禁用后立即恢复");
                Assert.AreEqual(originalTint, art.material.GetColor("_Color"));
                Assert.AreEqual(0f, ring.alpha);
                button.interactable = true;
                yield return null;
                Assert.AreEqual(originalScale, art.rectTransform.localScale, "恢复交互不能重新进入过期的按下状态");
                feedback.enabled = false;
                Assert.AreEqual(originalScale, art.rectTransform.localScale);
                feedback.enabled = true;
                yield return new WaitForSecondsRealtime(.15f);
                Assert.AreEqual(originalScale, art.rectTransform.localScale, "重新启用不能残留按下状态");
            }
            finally { Time.timeScale = previousTimeScale; }
        }

        [UnityTest]
        public IEnumerator 章节插画反馈_键盘聚焦提亮_确认只进入一次对应章节()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.EnterMenu();
            controller.OpenLevels();
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var button = canvas.Find("Navigation/LevelsPanel/ScrollView/Viewport/Content")
                .GetComponentsInChildren<Button>()[1];
            var art = button.transform.Find("Artwork").GetComponent<Image>();
            var scale = art.rectTransform.localScale;
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            EventSystem.current.SetSelectedGameObject(button.gameObject);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(0f, button.transform.Find("FocusRing").GetComponent<CanvasGroup>().alpha, "聚焦不能叠圆角外框");
            Assert.Greater(art.material.GetColor("_Color").r, 1f);
            ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            Assert.AreEqual(1, clicks, "反馈不能重复触发 Button 的确认事件");
            Assert.IsTrue(canvas.Find("Navigation/ChapterPanel").gameObject.activeSelf);
            Assert.AreEqual(controller.Catalog.chapters[1].title + " · 关卡选择",
                canvas.Find("Navigation/ChapterPanel/Title").GetComponent<Text>().text);
            Assert.AreEqual(scale, art.rectTransform.localScale, "离开页面应清除反馈状态");
            controller.Back();
            Assert.AreEqual(button.name, EventSystem.current.currentSelectedGameObject.name);
        }

        [UnityTest]
        public IEnumerator 焦点描边_支持键盘和禁用状态_保留美术着色()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            menu.EnterMenu();
            menu.OpenLevels();
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var entry = canvas.Find("Navigation/LevelsPanel/BackButton").GetComponent<Button>();
            var image = entry.GetComponent<Image>();
            var ring = entry.transform.Find("FocusRing").GetComponent<CanvasGroup>();
            image.color = Color.yellow;
            EventSystem.current.SetSelectedGameObject(entry.gameObject);
            yield return null;
            Assert.AreEqual(0f, ring.alpha, "聚焦不能叠圆角外框");
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
        public IEnumerator 窄屏_四章卷轴等比缩放_保留模板字号与横向滚动能力()
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
            Assert.LessOrEqual(panel.rect.width * panel.localScale.x, rect.rect.width, "完整卷轴应缩放到屏幕内");
            var template = canvas.transform.Find("Navigation/CardTemplates/ChapterCard/Description").GetComponent<Text>();
            foreach (var text in scroll.content.GetComponentsInChildren<Text>())
                if (text.name == "Description") Assert.AreEqual(template.fontSize, text.fontSize, "窄屏应保留可读字号");
            Assert.LessOrEqual(scroll.content.rect.width, scroll.viewport.rect.width + 1, "四张章节插画应同时呈现");
            Assert.IsTrue(scroll.horizontal);
            var leaderboard = (RectTransform)canvas.transform.Find("LeaderboardPanel");
            Assert.LessOrEqual(leaderboard.sizeDelta.x * leaderboard.localScale.x, rect.rect.width - 60,
                "窄屏弹窗应保留安全边距");
        }
    }
}
