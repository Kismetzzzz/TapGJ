using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class MenuElementsPlayModeTests
    {
        [UnityTest]
        public IEnumerator 入口按钮_不叠加外框_保留键盘聚焦及确认()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var button = canvas.Find("Navigation/EntryPage/EnterButton").GetComponent<Button>();
            Assert.IsFalse(button.transform.Find("FocusRing").gameObject.activeSelf);
            Assert.AreSame(button.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.AreNotEqual(button.colors.normalColor, button.colors.selectedColor, "键盘聚焦应由按钮本身变色提示");
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            ExecuteEvents.Execute(button.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            yield return null;
            Assert.AreEqual(1, clicks);
            Assert.IsFalse(canvas.Find("Navigation/Menu").gameObject.activeSelf);
            Assert.IsTrue(canvas.Find("Navigation/LevelsPanel").gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator 独立素材_点击区域和返回流程保留_公告与说明正确切换()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>().transform;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var entry = canvas.Find("Navigation/EntryPage/EnterButton").GetComponent<Button>();
            AssertArtwork(entry.GetComponent<Image>(), "StartButton");
            Click(entry);
            yield return null;
            Assert.IsFalse(canvas.Find("Navigation/Menu").gameObject.activeSelf);
            Assert.IsTrue(canvas.Find("Navigation/LevelsPanel").gameObject.activeSelf);
            controller.Back();
            yield return null;
            Assert.IsTrue(canvas.Find("Navigation/EntryPage").gameObject.activeSelf);
            var utility = canvas.Find("Navigation/UtilityButtons");
            foreach (string name in new[] { "SettingsButton", "UserButton", "NoticeButton" })
                AssertArtwork(utility.Find(name).GetComponent<Image>(), name);
            Assert.IsFalse(utility.Find("UserButton").GetComponent<Button>().interactable);

            var settings = canvas.Find("SettingsPanel");
            var done = settings.Find("DoneButton").GetComponent<Button>();
            AssertArtwork(done.GetComponent<Image>(), "ReturnButton");
            Click(utility.Find("SettingsButton").GetComponent<Button>());
            yield return null;
            Assert.IsTrue(settings.gameObject.activeSelf);
            Click(done);
            yield return null;
            Assert.IsFalse(settings.gameObject.activeSelf);
            var panel = canvas.Find("NoticePanel");
            var close = panel.Find("CloseButton").GetComponent<Button>();
            AssertArtwork(close.GetComponent<Image>(), "ReturnButton");
            Assert.AreSame(done.GetComponent<Image>().sprite, close.GetComponent<Image>().sprite);
            Assert.IsFalse(close.transform.Find("FocusRing").gameObject.activeSelf, "返回素材不应叠加通用圆角外框");
            Assert.AreEqual(0f, close.transform.Find("Label").GetComponent<Text>().color.a, "不能重复叠加图片已有的文字");
            Assert.AreNotEqual(close.colors.normalColor, close.colors.selectedColor, "键盘聚焦应保留变色反馈");
            var announcement = panel.Find("ReferenceArtwork").GetComponent<Image>();
            var information = panel.Find("InformationArtwork").GetComponent<Image>();
            AssertArtwork(announcement, "NoticeArtwork");
            AssertArtwork(information, "DialogPanel");
            Assert.IsFalse(announcement.raycastTarget);
            Assert.IsFalse(information.raycastTarget);
            var noticeButton = utility.Find("NoticeButton").GetComponent<Button>();
            EventSystem.current.SetSelectedGameObject(noticeButton.gameObject);
            Click(noticeButton);
            yield return null;
            Assert.IsTrue(announcement.gameObject.activeInHierarchy);
            Assert.IsFalse(information.gameObject.activeSelf);
            Assert.IsFalse(panel.Find("Title").GetComponent<Text>().enabled, "公告不能叠加图片已有的题字");
            string announcementText = panel.Find("Message").GetComponent<Text>().text;
            Assert.IsFalse(canvas.Find("Navigation").GetComponent<CanvasGroup>().blocksRaycasts);
            Click(panel.Find("CloseButton").GetComponent<Button>());
            yield return null;
            Assert.AreSame(noticeButton.gameObject, EventSystem.current.currentSelectedGameObject);

            foreach (string name in new[] { "Help", "About" })
            {
                controller.OpenSettings();
                yield return null;
                var origin = settings.Find(name + "Button").GetComponent<Button>();
                Click(origin);
                yield return null;
                Assert.IsFalse(announcement.gameObject.activeSelf);
                Assert.IsTrue(information.gameObject.activeInHierarchy);
                var title = panel.Find("Title").GetComponent<Text>();
                Assert.IsTrue(title.enabled);
                Assert.AreEqual(name == "Help" ? "帮助" : "关于", title.text);
                Click(panel.Find("CloseButton").GetComponent<Button>());
                yield return null;
                Assert.IsTrue(settings.gameObject.activeSelf);
                Assert.AreSame(origin.gameObject, EventSystem.current.currentSelectedGameObject);
                Click(done);
                yield return null;
            }
            Click(noticeButton);
            yield return null;
            Assert.IsTrue(announcement.gameObject.activeInHierarchy, "说明页之后重新打开公告必须恢复公告美术");
            Assert.AreEqual(announcementText, panel.Find("Message").GetComponent<Text>().text);
            Assert.AreSame(close.gameObject, EventSystem.current.currentSelectedGameObject);
            ExecuteEvents.Execute(close.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
            yield return null;
            Assert.IsFalse(panel.gameObject.activeSelf, "红金色返回按钮应支持键盘确认");
            Assert.AreSame(noticeButton.gameObject, EventSystem.current.currentSelectedGameObject);
            Click(noticeButton);
            yield return null;
            var backdrop = canvas.Find("ModalBackdrop").GetComponent<Button>();
            Click(backdrop, ((RectTransform)canvas).TransformPoint(new Vector3(-600, -320)));
            Assert.IsFalse(panel.gameObject.activeSelf, "面板外的背景仍应支持点击关闭");
        }

        [UnityTest]
        public IEnumerator 素材界面_宽窄及矮窗口完整呈现并导出画面()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/MainMenu.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var cameraObject = new GameObject("MenuElementsPreviewCamera");
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
            string output = Path.GetFullPath("Artifacts/MenuElements/previews");
            Directory.CreateDirectory(output);
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 720), new Vector2Int(1560, 720), new Vector2Int(1280, 540) })
            {
                var target = new RenderTexture(size.x, size.y, 24);
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return null;
                AssertInside(canvas, (RectTransform)canvas.transform.Find("Navigation/EntryPage/EnterButton"));
                foreach (Transform button in canvas.transform.Find("Navigation/UtilityButtons"))
                    if (button.gameObject.activeSelf) AssertInside(canvas, (RectTransform)button);
                Capture(camera, target, Path.Combine(output, "entry-" + size.x + "x" + size.y + ".png"));
                controller.OpenSettings();
                yield return null;
                AssertInside(canvas, (RectTransform)canvas.transform.Find("SettingsPanel/DoneButton"));
                Capture(camera, target, Path.Combine(output, "settings-" + size.x + "x" + size.y + ".png"));
                controller.ClosePanels();
                controller.OpenNotice();
                yield return null;
                var panel = canvas.transform.Find("NoticePanel");
                AssertInside(canvas, (RectTransform)panel.Find("ReferenceArtwork"));
                AssertInside(canvas, (RectTransform)panel.Find("CloseButton"));
                AssertInside(canvas, (RectTransform)panel.Find("Message"));
                AssertContentSpacing(panel);
                Capture(camera, target, Path.Combine(output, "notice-" + size.x + "x" + size.y + ".png"));
                controller.ClosePanels();
                controller.OpenSettings();
                canvas.transform.Find("SettingsPanel/HelpButton").GetComponent<Button>().onClick.Invoke();
                yield return null;
                AssertInside(canvas, (RectTransform)panel.Find("InformationArtwork"));
                AssertInside(canvas, (RectTransform)panel.Find("CloseButton"));
                AssertContentSpacing(panel);
                Capture(camera, target, Path.Combine(output, "help-" + size.x + "x" + size.y + ".png"));
                controller.Back();
                controller.ClosePanels();
                camera.targetTexture = null;
                target.Release();
                Object.Destroy(target);
            }
            Object.Destroy(cameraObject);
        }

        static void AssertContentSpacing(Transform panel)
        {
            var content = (RectTransform)panel.Find("ContentHitArea");
            var button = RectTransformUtility.CalculateRelativeRectTransformBounds(content, panel.Find("CloseButton"));
            var message = panel.Find("Message").GetComponent<Text>();
            var body = RectTransformUtility.CalculateRelativeRectTransformBounds(content, message.transform);
            Assert.GreaterOrEqual(button.min.x, content.rect.xMin - 1, "返回按钮必须位于面板内");
            Assert.LessOrEqual(button.max.x, content.rect.xMax + 1, "返回按钮必须位于面板内");
            Assert.GreaterOrEqual(button.min.y, content.rect.yMin - 1, "返回按钮不能压在面板外的背景文字上");
            Assert.LessOrEqual(button.max.y + 16, body.min.y, "正文与返回按钮之间应留出间距");
            Assert.LessOrEqual(message.preferredHeight, message.rectTransform.rect.height, "给按钮留空间后，正文仍须完整显示");
        }

        static void AssertArtwork(Image image, string name)
        {
            Assert.IsNotNull(image.sprite);
            Assert.AreEqual(name, image.sprite.name);
            Assert.AreEqual(image.sprite.rect.width / image.sprite.rect.height,
                image.rectTransform.rect.width / image.rectTransform.rect.height, .001f, "图片必须按原始比例呈现");
        }

        static void Click(Button button, Vector3? worldPosition = null)
        {
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, worldPosition ?? button.transform.position)
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.IsNotEmpty(hits, button.name + " @ " + pointer.position + " / " + Screen.width + "x" + Screen.height);
            Assert.AreSame(button, hits[0].gameObject.GetComponentInParent<Button>(), button.name + " 点击被其他对象遮挡");
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        static void AssertInside(Canvas canvas, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var frame = ((RectTransform)canvas.transform).rect;
            foreach (var corner in corners)
            {
                var local = canvas.transform.InverseTransformPoint(corner);
                Assert.That(local.x, Is.InRange(frame.xMin - 1, frame.xMax + 1), rect.name + " 横向越界");
                Assert.That(local.y, Is.InRange(frame.yMin - 1, frame.yMax + 1), rect.name + " 纵向越界");
            }
        }

        static void Capture(Camera camera, RenderTexture target, string path)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            RenderTexture.active = previous;
            Object.Destroy(texture);
        }
    }
}
