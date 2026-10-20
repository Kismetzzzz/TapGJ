using System.Linq;
using NUnit.Framework;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public class MainMenuTests
    {
        const string MenuPath = "Assets/Scenes/MainMenu.unity";
        GameObject _audioObject;

        [TearDown]
        public void TearDown()
        {
            if (_audioObject != null) Object.DestroyImmediate(_audioObject);
        }

        [Test]
        public void 主菜单场景_保存真实UGUI对象和所有控制器引用()
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("请先保存当前场景，再运行场景引用检查。");
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
            try
            {
                var roots = scene.GetRootGameObjects();
                var controller = roots.SelectMany(g => g.GetComponentsInChildren<MainMenuController>(true)).Single();
                var canvas = roots.SelectMany(g => g.GetComponentsInChildren<Canvas>(true)).Single();
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>());
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, canvas.GetComponent<CanvasScaler>().uiScaleMode);
                Assert.AreEqual(1, roots.SelectMany(g => g.GetComponentsInChildren<EventSystem>(true)).Count());
                Assert.AreEqual(1, roots.SelectMany(g => g.GetComponentsInChildren<StandaloneInputModule>(true)).Count());
                Assert.AreEqual(2, roots.SelectMany(g => g.GetComponentsInChildren<AudioSource>(true)).Count());

                var data = new SerializedObject(controller);
                var iterator = data.GetIterator();
                while (iterator.NextVisible(true))
                {
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                        Assert.IsNotNull(iterator.objectReferenceValue, "场景引用未绑定：" + iterator.name);
                }
                Assert.IsFalse(((GameObject)data.FindProperty("levelsPanel").objectReferenceValue).activeSelf);
                Assert.IsFalse(((GameObject)data.FindProperty("settingsPanel").objectReferenceValue).activeSelf);
                var leaderboard = (LeaderboardPanel)data.FindProperty("leaderboardPanel").objectReferenceValue;
                Assert.IsFalse(leaderboard.gameObject.activeSelf);
                var leaderboardData = new SerializedObject(leaderboard);
                Assert.AreEqual(10, leaderboardData.FindProperty("rows").arraySize);
                var leaderboardProperties = leaderboardData.GetIterator();
                while (leaderboardProperties.NextVisible(true))
                    if (leaderboardProperties.propertyType == SerializedPropertyType.ObjectReference)
                        Assert.IsNotNull(leaderboardProperties.objectReferenceValue,
                            "排行榜引用未绑定：" + leaderboardProperties.propertyPath);
                Assert.IsFalse(((Button)data.FindProperty("backdropButton").objectReferenceValue).gameObject.activeSelf);

                foreach (var label in canvas.GetComponentsInChildren<Text>(true))
                {
                    Assert.IsNotNull(label.font, label.name + " 没有字体");
                    Assert.IsFalse(label.raycastTarget, label.name + " 的装饰文字不应拦截点击");
                }

                string[] sharedButtonPaths =
                {
                    "Navigation/EntryPage/EnterButton", "Navigation/Menu/StartButton", "Navigation/Menu/QuitButton",
                    "Navigation/Menu/LeaderboardButton", "Navigation/UtilityButtons/SettingsButton",
                    "Navigation/UtilityButtons/UserButton", "Navigation/UtilityButtons/NoticeButton",
                    "SettingsPanel/DoneButton",
                    "LeaderboardPanel/AccountCard/LoginButton", "LeaderboardPanel/GlobalButton",
                    "LeaderboardPanel/FriendsButton", "LeaderboardPanel/RefreshButton",
                    "LeaderboardPanel/CloseButton", "Navigation/LevelsPanel/BackButton",
                    "Navigation/ChapterPanel/BackButton", "NoticePanel/CloseButton"
                };
                foreach (string buttonPath in sharedButtonPaths)
                {
                    var button = canvas.transform.Find(buttonPath).GetComponent<Button>();
                    Assert.AreEqual("Assets/UI/Prefabs/MenuButton.prefab",
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(button.gameObject), buttonPath);
                    Assert.AreEqual(Image.Type.Sliced, button.GetComponent<Image>().type, buttonPath);
                    Assert.AreSame(button.GetComponent<Image>(), button.targetGraphic, buttonPath);
                    Assert.IsNotNull(button.transform.Find("Label").GetComponent<Text>(), buttonPath);
                    var buttonLabel = button.transform.Find("Label").GetComponent<Text>();
                    Assert.LessOrEqual(buttonLabel.preferredHeight, buttonLabel.rectTransform.rect.height,
                        buttonPath + " 的文字高度应能完整显示");
                    var icon = button.transform.Find("Icon").GetComponent<Image>();
                    Assert.IsTrue(icon.preserveAspect);
                    Assert.IsFalse(icon.raycastTarget);
                    bool lockedLeaderboard = button.name == "LeaderboardButton";
                    Assert.AreEqual(lockedLeaderboard, icon.gameObject.activeSelf);
                    if (lockedLeaderboard)
                    {
                        Assert.IsFalse(button.interactable);
                        Assert.IsNotNull(icon.sprite);
                        Assert.AreEqual("Assets/UI/Art/UiLock.png", AssetDatabase.GetAssetPath(icon.sprite));
                        StringAssert.Contains("暂未开放", buttonLabel.text);
                    }
                    var ring = button.transform.Find("FocusRing").GetComponent<CanvasGroup>();
                    Assert.IsNotNull(button.GetComponent<MenuButtonFeedback>());
                    Assert.IsFalse(ring.blocksRaycasts, "焦点装饰不能拦截按钮点击");
                    Assert.IsFalse(ring.GetComponent<Image>().raycastTarget);
                    // 底图、状态图和字体跟随 Prefab；入口可单独配置图标。
                    var modifications = PrefabUtility.GetPropertyModifications(button.gameObject);
                    bool referenceArtwork = buttonPath == "Navigation/EntryPage/EnterButton"
                        || buttonPath.StartsWith("Navigation/UtilityButtons/") || buttonPath == "SettingsPanel/DoneButton"
                        || buttonPath == "NoticePanel/CloseButton";
                    Assert.IsFalse(modifications.Any(m => m.propertyPath == "m_Sprite" && m.target.name != "Icon" && !referenceArtwork
                        || m.propertyPath.StartsWith("m_SpriteState.") || m.propertyPath == "m_Transition"
                        || m.propertyPath == "m_FontData.m_Font"), buttonPath + " 不应锁死换肤属性");
                }
                var navigation = canvas.transform.Find("Navigation");
                Assert.IsTrue(navigation.Find("EntryPage").gameObject.activeSelf);
                Assert.IsFalse(navigation.Find("Menu").gameObject.activeSelf);
                var catalog = (ChapterCatalog)data.FindProperty("chapterCatalog").objectReferenceValue;
                Assert.AreEqual("Assets/UI/Data/ChapterCatalog.asset", AssetDatabase.GetAssetPath(catalog));
                Assert.IsFalse(navigation.Find("ChapterPanel").gameObject.activeSelf);
                foreach (string templatePath in new[] { "CardTemplates/ChapterCard", "CardTemplates/LevelCard" })
                {
                    var button = navigation.Find(templatePath).GetComponent<Button>();
                    Assert.AreEqual("Assets/UI/Prefabs/" + button.name + ".prefab",
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(button.gameObject));
                    Assert.AreEqual(0, button.onClick.GetPersistentEventCount(), "模板不能写死具体章节或关卡跳转");
                    Assert.IsNotNull(button.transform.Find("Description").GetComponent<Text>());
                    Assert.IsNotNull(button.transform.Find("Availability").GetComponent<Text>());
                    Assert.IsNotNull(button.transform.Find("Number").GetComponent<Text>());
                    var modifications = PrefabUtility.GetPropertyModifications(button.gameObject) ?? new PropertyModification[0];
                    Assert.IsFalse(modifications.Any(m => m.propertyPath == "m_Sprite"
                        || m.propertyPath.StartsWith("m_SpriteState.") || m.propertyPath.StartsWith("m_FontData.")
                        || m.propertyPath == "m_Color"), "卡片样式应由独立 Prefab 管理，场景不能锁死换肤属性");
                }
                foreach (string pagePath in new[] { "LevelsPanel", "ChapterPanel" })
                {
                    var scroll = navigation.Find(pagePath + "/ScrollView").GetComponent<ScrollRect>();
                    Assert.IsTrue(scroll.horizontal);
                    Assert.IsFalse(scroll.vertical);
                    Assert.IsNotNull(scroll.viewport.GetComponent<RectMask2D>());
                    Assert.IsNotNull(scroll.content.GetComponent<HorizontalLayoutGroup>());
                    Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize,
                        scroll.content.GetComponent<ContentSizeFitter>().horizontalFit);
                }
                Assert.Less(navigation.GetSiblingIndex(), canvas.transform.Find("ModalBackdrop").GetSiblingIndex(),
                    "遮罩和弹窗必须显示在页面上方");
                var logo = navigation.Find("Menu/Logo").GetComponent<Image>();
                Assert.IsTrue(logo.preserveAspect);
                Assert.IsFalse(logo.raycastTarget);
                Assert.IsFalse(logo.gameObject.activeSelf, "尚无素材的 Logo 插槽默认隐藏");
                foreach (string artPath in new[] { "Menu/ArtIllustration", "EntryPage/ArtIllustration", "EntryPage/Logo" })
                {
                    var art = navigation.Find(artPath).GetComponent<Image>();
                    Assert.IsFalse(art.gameObject.activeSelf);
                    Assert.IsFalse(art.raycastTarget);
                    Assert.IsTrue(art.preserveAspect);
                    Assert.AreEqual(Color.white, art.color, "美术插槽不应染色");
                }
                Assert.IsNotNull(canvas.GetComponent<MenuResponsiveLayout>());
                Assert.AreEqual(1f, canvas.GetComponent<CanvasScaler>().matchWidthOrHeight);
                foreach (string scope in new[] { "Global", "Friends" })
                {
                    var tab = (RectTransform)leaderboard.transform.Find(scope + "Button");
                    var indicator = (RectTransform)leaderboard.transform.Find(scope + "Indicator");
                    Assert.Less(indicator.sizeDelta.x, tab.sizeDelta.x, "选中下划线应只覆盖当前标签");
                }
                foreach (var slider in canvas.transform.Find("SettingsPanel").GetComponentsInChildren<Slider>(true))
                {
                    var handle = slider.handleRect.GetComponent<Image>();
                    Assert.AreEqual(Image.Type.Simple, handle.type);
                    Assert.IsTrue(handle.preserveAspect, "圆形滑块应保持比例，避免被 Slider 纵向拉长");
                }
                Assert.AreEqual(Image.Type.Sliced, navigation.Find("LevelsPanel").GetComponent<Image>().type);
                Assert.AreEqual(Image.Type.Sliced, canvas.transform.Find("SettingsPanel").GetComponent<Image>().type);
                Assert.AreEqual(Image.Type.Sliced, leaderboard.GetComponent<Image>().type);
            }
            finally
            {
                if (previousSetup.Length > 0 && previousSetup.All(s => !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(previousSetup);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void 三类UI预制体_独立保存且互不继承_内部引用完整()
        {
            string[] paths = { "Assets/UI/Prefabs/MenuButton.prefab", "Assets/UI/Prefabs/ChapterCard.prefab", "Assets/UI/Prefabs/LevelCard.prefab" };
            foreach (string path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(asset, path);
                Assert.AreEqual(PrefabAssetType.Regular, PrefabUtility.GetPrefabAssetType(asset), path);
                var dependencies = AssetDatabase.GetDependencies(path, false);
                foreach (string other in paths.Where(p => p != path))
                    CollectionAssert.DoesNotContain(dependencies, other, "修改 " + other + " 不应影响 " + path);
                var button = asset.GetComponent<Button>();
                Assert.AreSame(asset.GetComponent<Image>(), button.targetGraphic);
                Assert.AreEqual(0, button.onClick.GetPersistentEventCount(), "Prefab 不应绑定具体关卡或场景中的回调");
                Assert.IsNotNull(asset.transform.Find("Label").GetComponent<Text>());
                Assert.IsNotNull(asset.transform.Find("Icon").GetComponent<Image>());
                var feedback = new SerializedObject(asset.GetComponent<MenuButtonFeedback>());
                Assert.AreSame(asset.transform.Find("FocusRing").GetComponent<CanvasGroup>(),
                    feedback.FindProperty("focusRing").objectReferenceValue, path + " 的焦点引用应位于自己的 Prefab 内");
                if (path.EndsWith("MenuButton.prefab"))
                    Assert.IsNull(asset.transform.Find("Description"), "普通按钮应不带章节或关卡专用内容");
                else
                    foreach (string child in new[] { "Number", "Description", "Availability" })
                        Assert.IsNotNull(asset.transform.Find(child).GetComponent<Text>(), path + "/" + child);
            }
        }

        [Test]
        public void 构建设置_菜单先启动且已有游戏关卡启用()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
            Assert.AreEqual(MenuPath, scenes[0].path);
            Assert.AreEqual(1, scenes.Count(s => s.path == MenuPath));
            Assert.AreEqual(1, scenes.Count(s => s.path == "Assets/Scenes/Game.unity"));
        }

        [TestCase(-1L, "--:--.--")]
        [TestCase(0L, "00:00.00")]
        [TestCase(62349L, "01:02.34")]
        [TestCase(3600010L, "60:00.01")]
        public void 排行榜用时_按毫秒显示分秒和百分秒(long milliseconds, string expected)
        {
            Assert.AreEqual(expected, LeaderboardPanel.FormatTime(milliseconds));
        }

        [TestCase(-1f, 0f)]
        [TestCase(0f, 0f)]
        [TestCase(0.35f, 0.35f)]
        [TestCase(2f, 1f)]
        [TestCase(float.NaN, 0.8f)]
        [TestCase(float.PositiveInfinity, 0.8f)]
        public void 音量_限制范围并只修改所属声道(float input, float expected)
        {
            _audioObject = new GameObject("TestAudio");
            _audioObject.SetActive(false);
            var music = _audioObject.AddComponent<AudioSource>();
            var effects = _audioObject.AddComponent<AudioSource>();
            var audio = _audioObject.AddComponent<GameAudio>();
            var data = new SerializedObject(audio);
            data.FindProperty("musicSource").objectReferenceValue = music;
            data.FindProperty("effectsSource").objectReferenceValue = effects;
            data.ApplyModifiedPropertiesWithoutUndo();
            audio.SetMusicVolume(0.65f);
            audio.SetEffectsVolume(0.4f);

            audio.SetMusicVolume(input);
            Assert.AreEqual(expected, music.volume, 0.001f);
            Assert.AreEqual(0.4f, effects.volume, 0.001f, "音乐设置不能影响音效");
            audio.SetMusicVolume(0.65f);
            audio.SetEffectsVolume(input);
            Assert.AreEqual(expected, effects.volume, 0.001f);
            Assert.AreEqual(0.65f, music.volume, 0.001f, "音效设置不能影响音乐");
        }
    }
}
