using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class ReferenceMenuPlayModeTests
    {
        [UnityTest]
        public IEnumerator 四章插画_独立点击进入对应章节_返回恢复焦点()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            controller.EnterMenu();
            controller.OpenLevels();
            yield return null;
            var page = canvas.Find("Navigation/LevelsPanel");
            var scroll = page.Find("ScrollView").GetComponent<ScrollRect>();
            Assert.AreEqual(4, scroll.content.GetComponentsInChildren<Button>().Length);
            Assert.IsFalse(page.Find("ScrollArtwork").GetComponent<Image>().raycastTarget);
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                var button = scroll.content.GetComponentsInChildren<Button>()[i];
                var art = button.transform.Find("Artwork").GetComponent<Image>();
                Assert.AreSame(controller.Catalog.chapters[i].artwork, art.sprite);
                Assert.IsTrue(art.gameObject.activeInHierarchy);
                Assert.IsFalse(button.GetComponent<Image>().enabled, "插画不能叠加通用卡片底色");
                Assert.IsFalse(button.transform.Find("Label").gameObject.activeSelf, "不能叠加图片中已有的题字");
                var pointer = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(null, art.rectTransform.position)
                };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointer, hits);
                Assert.IsNotEmpty(hits, "插画中心必须可点击");
                Assert.AreSame(button, hits[0].gameObject.GetComponentInParent<Button>());
                ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
                Assert.IsTrue(canvas.Find("Navigation/ChapterPanel").gameObject.activeSelf);
                Assert.AreEqual(controller.Catalog.chapters[i].title + " · 关卡选择",
                    canvas.Find("Navigation/ChapterPanel/Title").GetComponent<Text>().text);
                controller.Back();
                Assert.AreEqual("Chapter" + (i + 1).ToString("00") + "Button", EventSystem.current.currentSelectedGameObject.name);
            }
        }

        [UnityTest]
        public IEnumerator 章节插画_重排与空项仍按配置绑定_无图保留文字卡片()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var original = controller.Catalog.chapters;
            try
            {
                controller.Catalog.chapters = new[]
                {
                    null, original[3], new ChapterCatalog.Chapter { title = "新增章节", description = "等待插画" }, original[0]
                };
                controller.OpenLevels();
                var scroll = Object.FindFirstObjectByType<Canvas>().transform
                    .Find("Navigation/LevelsPanel/ScrollView").GetComponent<ScrollRect>();
                var buttons = scroll.content.GetComponentsInChildren<Button>();
                CollectionAssert.AreEqual(new[] { "Chapter02Button", "Chapter03Button", "Chapter04Button" },
                    buttons.Select(b => b.name).ToArray());
                Assert.AreSame(original[3].artwork, buttons[0].transform.Find("Artwork").GetComponent<Image>().sprite);
                Assert.AreSame(original[0].artwork, buttons[2].transform.Find("Artwork").GetComponent<Image>().sprite);
                Assert.IsFalse(buttons[1].transform.Find("Artwork").gameObject.activeSelf);
                Assert.IsTrue(buttons[1].GetComponent<Image>().enabled);
                Assert.IsTrue(buttons[1].transform.Find("Label").gameObject.activeSelf);
                Assert.AreEqual("新增章节", buttons[1].transform.Find("Label").GetComponent<Text>().text);
                yield return null;
            }
            finally
            {
                controller.Catalog.chapters = original;
                controller.OpenLevels();
            }
        }

        [UnityTest]
        public IEnumerator 四章卷轴构图_不同屏幕比例保持全部插画并导出画面()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.EnterMenu();
            controller.OpenLevels();
            var cameraObject = new GameObject("ChapterPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 360;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.cullingMask = 1 << 5;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -10);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1;
            string output = Path.GetFullPath("Artifacts/ChapterSelection/previews");
            Directory.CreateDirectory(output);
            foreach (int width in new[] { 1280, 960, 1560, 640 })
            {
                var target = new RenderTexture(width, 720, 24);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                var page = canvas.transform.Find("Navigation/LevelsPanel");
                AssertInside(canvas, (RectTransform)page);
                AssertInside(canvas, (RectTransform)page.Find("BackButton"));
                var scroll = page.Find("ScrollView").GetComponent<ScrollRect>();
                Assert.AreEqual(4, scroll.content.GetComponentsInChildren<Button>().Length);
                Assert.LessOrEqual(scroll.content.rect.width, scroll.viewport.rect.width + 1, "四章应同时呈现");
                foreach (var button in scroll.content.GetComponentsInChildren<Button>())
                {
                    var art = button.transform.Find("Artwork").GetComponent<Image>();
                    AssertInside(canvas, art.rectTransform);
                    Assert.AreEqual(art.sprite.rect.width / art.sprite.rect.height,
                        art.rectTransform.rect.width / art.rectTransform.rect.height, .001f, "插画必须保持原始比例");
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, art.transform);
                    Assert.GreaterOrEqual(bounds.min.x, scroll.viewport.rect.xMin - 1);
                    Assert.LessOrEqual(bounds.max.x, scroll.viewport.rect.xMax + 1);
                }
                Capture(camera, target, Path.Combine(output, "chapters-" + width + ".png"));
                if (width == 1280)
                {
                    string feedbackOutput = Path.GetFullPath("Artifacts/ChapterFeedback/previews");
                    Directory.CreateDirectory(feedbackOutput);
                    var button = scroll.content.GetComponentsInChildren<Button>()[0];
                    var feedback = button.GetComponent<MenuButtonFeedback>();
                    var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                    EventSystem.current.SetSelectedGameObject(null);
                    feedback.OnPointerExit(pointer);
                    yield return new WaitForSecondsRealtime(.15f);
                    var art = button.transform.Find("Artwork").GetComponent<Image>();
                    var center = RectTransformUtility.WorldToScreenPoint(camera, art.rectTransform.position);
                    var sample = new RectInt(Mathf.RoundToInt(center.x) - 10, Mathf.RoundToInt(center.y) - 10, 20, 20);
                    float idleBrightness = Capture(camera, target, Path.Combine(feedbackOutput, "idle.png"), sample);
                    feedback.OnPointerEnter(pointer);
                    yield return new WaitForSecondsRealtime(.15f);
                    float hoverBrightness = Capture(camera, target, Path.Combine(feedbackOutput, "hover.png"), sample);
                    Assert.Greater(hoverBrightness, idleBrightness + .005f, "实际渲染画面的插画内部必须提亮");
                    feedback.OnPointerDown(pointer);
                    yield return new WaitForSecondsRealtime(.15f);
                    Capture(camera, target, Path.Combine(feedbackOutput, "pressed.png"));
                    feedback.OnPointerUp(pointer);
                    feedback.OnPointerExit(pointer);
                    yield return new WaitForSecondsRealtime(.15f);
                }
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
            }
            Object.Destroy(cameraObject);
        }

        [UnityTest]
        public IEnumerator 参考设置_帮助关于返回设置_关闭恢复入口焦点()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var settingsButton = canvas.Find("Navigation/UtilityButtons/SettingsButton").GetComponent<Button>();
            Assert.Greater(settingsButton.transform.parent.GetSiblingIndex(), canvas.Find("Navigation/EntryPage").GetSiblingIndex(),
                "右侧按钮必须显示在全屏入口美术上方");
            EventSystem.current.SetSelectedGameObject(settingsButton.gameObject);
            settingsButton.onClick.Invoke();
            var settings = canvas.Find("SettingsPanel");
            var effects = (RectTransform)settings.Find("EffectsSlider");
            var music = (RectTransform)settings.Find("MusicSlider");
            Assert.Greater(effects.anchoredPosition.y, music.anchoredPosition.y, "音效应位于音乐上方");
            Assert.IsFalse(settings.Find("AccountButton").GetComponent<Button>().interactable);
            Assert.IsFalse(canvas.Find("Navigation").GetComponent<CanvasGroup>().blocksRaycasts);
            var slider = effects.GetComponent<Slider>();
            float originalVolume = slider.value;
            float originalMusic = GameAudio.Instance.MusicVolume;
            var handleArea = (RectTransform)slider.handleRect.parent;
            foreach (float value in new[] { 0f, 1f })
            {
                var position = handleArea.TransformPoint(new Vector3(value == 0 ? handleArea.rect.xMin : handleArea.rect.xMax, 0));
                slider.OnPointerDown(new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left,
                    position = RectTransformUtility.WorldToScreenPoint(null, position)
                });
                Assert.AreEqual(value, slider.value, .001f, "实际点击轨道必须调整音量");
                Assert.AreEqual(value, GameAudio.Instance.EffectsVolume, .001f);
                Assert.AreEqual(originalMusic, GameAudio.Instance.MusicVolume, .001f);
            }
            slider.value = originalVolume;

            foreach (string name in new[] { "Help", "About" })
            {
                settings.Find(name + "Button").GetComponent<Button>().onClick.Invoke();
                Assert.IsTrue(canvas.Find("NoticePanel").gameObject.activeSelf);
                Assert.AreEqual(name == "Help" ? "帮助" : "关于", canvas.Find("NoticePanel/Title").GetComponent<Text>().text);
                controller.Back();
                Assert.IsTrue(settings.gameObject.activeSelf);
                Assert.IsFalse(canvas.Find("NoticePanel").gameObject.activeSelf);
                Assert.IsFalse(canvas.Find("Navigation").GetComponent<CanvasGroup>().interactable);
                Assert.AreSame(settings.Find(name + "Button").gameObject, EventSystem.current.currentSelectedGameObject);
            }
            settings.Find("BackButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(canvas.Find("Navigation/EntryPage").gameObject.activeSelf);
            Assert.AreSame(settingsButton.gameObject, EventSystem.current.currentSelectedGameObject);
            settingsButton.onClick.Invoke();
            settings.Find("DoneButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(settings.gameObject.activeSelf);
            Assert.IsFalse(canvas.Find("ModalBackdrop").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator 参考构图_三种屏幕比例保留全部控件并导出画面()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var cameraObject = new GameObject("MenuPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 360;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .085f, .105f);
            camera.cullingMask = 1 << 5;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0, 0, -10);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1;
            string output = Path.GetFullPath("Artifacts/ReferenceMenu/previews");
            Directory.CreateDirectory(output);
            foreach (int width in new[] { 1280, 960, 1560 })
            {
                var target = new RenderTexture(width, 720, 24);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                var entry = (RectTransform)canvas.transform.Find("Navigation/EntryPage");
                AssertInside(canvas, entry);
                AssertInside(canvas, (RectTransform)canvas.transform.Find("Navigation/UtilityButtons"));
                Capture(camera, target, Path.Combine(output, "entry-" + width + ".png"));
                controller.OpenSettings();
                yield return null;
                AssertInside(canvas, (RectTransform)canvas.transform.Find("SettingsPanel"));
                Capture(camera, target, Path.Combine(output, "settings-" + width + ".png"));
                controller.ClosePanels();
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
            }
            Object.Destroy(cameraObject);
        }

        static void AssertInside(Canvas canvas, RectTransform frame)
        {
            var bounds = ((RectTransform)canvas.transform).rect;
            var corners = new Vector3[4];
            frame.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = canvas.transform.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(bounds.xMin - 1, bounds.xMax + 1), frame.name + " 横向越界");
                Assert.That(local.y, Is.InRange(bounds.yMin - 1, bounds.yMax + 1), frame.name + " 纵向越界");
            }
        }

        [UnityTest]
        public IEnumerator 卷轴关卡页_不同屏幕比例保留全部关卡并可滚到末尾()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.EnterMenu();
            controller.OpenLevels();
            var cameraObject = new GameObject("ScrollPreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 360;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << 5;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.transform.position = new Vector3(0,0,-10);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            canvas.GetComponent<CanvasScaler>().enabled = false;
            canvas.scaleFactor = 1;
            string output = Path.GetFullPath("Artifacts/ScrollLevelSelection/previews");
            Directory.CreateDirectory(output);
            foreach (int width in new[] { 1280, 960, 1560 })
            {
                var target = new RenderTexture(width,720,24);
                camera.targetTexture = target;
                controller.OpenChapter(0);
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                var page = canvas.transform.Find("Navigation/ChapterPanel");
                var art = page.Find("ScrollArtwork").GetComponent<Image>();
                Assert.IsNotNull(art.sprite);
                Assert.IsFalse(art.raycastTarget, "纸面装饰不能挡住关卡点击");
                AssertInside(canvas, art.rectTransform);
                AssertInside(canvas, (RectTransform)page.Find("BackButton"));
                var scroll = page.Find("ScrollView").GetComponent<ScrollRect>();
                var buttons = scroll.content.GetComponentsInChildren<Button>();
                Assert.AreEqual(controller.Catalog.chapters[0].levels.Length, buttons.Length);
                Assert.Greater(scroll.content.rect.width, scroll.viewport.rect.width, "第五关必须保留在可滚动内容中");
                Capture(camera, target, Path.Combine(output, "levels-"+width+".png"));
                var last = buttons[buttons.Length-1];
                EventSystem.current.SetSelectedGameObject(last.gameObject);
                yield return null;
                yield return null;
                var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, last.transform);
                Assert.GreaterOrEqual(bounds.min.x, scroll.viewport.rect.xMin-1);
                Assert.LessOrEqual(bounds.max.x, scroll.viewport.rect.xMax+1);
                Assert.Less(scroll.content.anchoredPosition.x,0);
                if (width == 1280) Capture(camera,target,Path.Combine(output,"levels-last-1280.png"));
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
            }
            Object.Destroy(cameraObject);
        }

        static float Capture(Camera camera, RenderTexture target, string path, RectInt? sampleRegion = null)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            float brightness = 0;
            if (sampleRegion.HasValue)
            {
                var region = sampleRegion.Value;
                brightness = texture.GetPixels(region.x, region.y, region.width, region.height)
                    .Average(color => (color.r + color.g + color.b) / 3f);
            }
            RenderTexture.active = previous;
            Object.Destroy(texture);
            return brightness;
        }
    }
}
