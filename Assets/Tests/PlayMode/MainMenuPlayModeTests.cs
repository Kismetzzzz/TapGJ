using System.Collections;
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
    public class MainMenuPlayModeTests
    {
        const string MenuPath = "Assets/Scenes/MainMenu.unity";
        const string MusicKey = "TapGJ.Audio.MusicVolume";
        const string EffectsKey = "TapGJ.Audio.EffectsVolume";
        bool _hadMusic, _hadEffects;
        float _previousMusic, _previousEffects;
        Transform _canvas;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _hadMusic = PlayerPrefs.HasKey(MusicKey);
            _hadEffects = PlayerPrefs.HasKey(EffectsKey);
            _previousMusic = PlayerPrefs.GetFloat(MusicKey);
            _previousEffects = PlayerPrefs.GetFloat(EffectsKey);
            PlayerPrefs.SetFloat(MusicKey, 0.7f);
            PlayerPrefs.SetFloat(EffectsKey, 0.6f);
            if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(MenuPath);
            yield return null;
            _canvas = Object.FindFirstObjectByType<Canvas>().transform;
            Button("EntryPage/EnterButton").onClick.Invoke();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu != null) menu.enabled = false; // 先保存测试值，再恢复用户偏好。
            if (GameAudio.Instance != null) Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            Restore(MusicKey, _hadMusic, _previousMusic);
            Restore(EffectsKey, _hadEffects, _previousEffects);
            PlayerPrefs.Save();
        }

        [UnityTest]
        public IEnumerator 页面流程_按配置显示章节和关卡_返回恢复章节焦点()
        {
            Assert.IsNull(Object.FindFirstObjectByType<GameSession>(), "菜单场景不能启动棋盘");
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            controller.Back();
            Assert.IsTrue(Find("EntryPage").gameObject.activeSelf);
            Assert.IsFalse(Find("Menu").gameObject.activeSelf);
            Assert.AreEqual(Button("EntryPage/EnterButton").gameObject, EventSystem.current.currentSelectedGameObject);
            Button("EntryPage/EnterButton").onClick.Invoke();
            Assert.IsTrue(Find("Menu").gameObject.activeSelf);
            Assert.IsFalse(Find("EntryPage").gameObject.activeSelf);
            Button("Menu/StartButton").onClick.Invoke();
            Assert.IsTrue(Find("LevelsPanel").gameObject.activeSelf);
            Assert.IsFalse(Find("Menu").gameObject.activeSelf);
            Assert.IsFalse(Find("UtilityButtons").gameObject.activeSelf);
            var catalog = controller.Catalog;
            for (int c = 1; c <= catalog.chapters.Length; c++)
            {
                var chapterButton = Button("LevelsPanel/Chapter" + c.ToString("00") + "Button");
                chapterButton.onClick.Invoke();
                string page = "ChapterPanel";
                Assert.IsTrue(Find(page).gameObject.activeSelf);
                Assert.IsFalse(Find("LevelsPanel").gameObject.activeSelf);
                Assert.AreEqual(Button(page + (catalog.chapters[c - 1].levels.Length > 0 ? "/Level01Button" : "/BackButton")).gameObject,
                    EventSystem.current.currentSelectedGameObject);
                yield return null;
                Canvas.ForceUpdateCanvases();
                var backLabel = Button(page + "/BackButton").transform.Find("Label").GetComponent<Text>();
                Assert.AreEqual("返回", backLabel.text);
                Assert.GreaterOrEqual(backLabel.cachedTextGenerator.characterCountVisible, backLabel.text.Length,
                    "返回按钮文字必须实际渲染，不能因按钮高度不足而消失");
                for (int l = 1; l <= catalog.chapters[c - 1].levels.Length; l++)
                {
                    Button(page + "/Level" + l.ToString("00") + "Button").onClick.Invoke();
                    Assert.AreEqual(catalog.chapters[c - 1].title + " · " + catalog.chapters[c - 1].levels[l - 1].title
                        + "待开放。\n目标：" + catalog.chapters[c - 1].levels[l - 1].goal.Replace('\n', ' '),
                        Find(page + "/StatusLabel").GetComponent<Text>().text);
                    Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
                    Assert.IsNull(Object.FindFirstObjectByType<GameSession>(), "未实现关卡不能偷偷进入当前对局");
                }
                Button(page + "/BackButton").onClick.Invoke();
                Assert.IsTrue(Find("LevelsPanel").gameObject.activeSelf);
                Assert.AreEqual(Button("LevelsPanel/Chapter" + c.ToString("00") + "Button").gameObject,
                    EventSystem.current.currentSelectedGameObject);
            }
            Button("LevelsPanel/BackButton").onClick.Invoke();
            Assert.IsTrue(Find("Menu").gameObject.activeSelf);
            Assert.AreEqual(Button("Menu/StartButton").gameObject, EventSystem.current.currentSelectedGameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 更改配置_支持不同章节数_超过五关_空章节_键盘滚动()
        {
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var catalog = controller.Catalog;
            var original = catalog.chapters;
            try
            {
                catalog.chapters = Enumerable.Range(0, 7).Select(c => new ChapterCatalog.Chapter
                {
                    title = "自定义章节 " + c,
                    description = "配置说明 " + c,
                    levels = Enumerable.Range(0, c == 0 ? 0 : 8).Select(l => new ChapterCatalog.Level
                    { title = "自定义关卡 " + l, goal = "自定义目标 " + l }).ToArray()
                }).ToArray();
                controller.OpenLevels();
                Assert.AreEqual(7, Find("LevelsPanel").GetComponentsInChildren<Button>()
                    .Count(b => b.name.StartsWith("Chapter")));
                Button("LevelsPanel/Chapter01Button").onClick.Invoke();
                Assert.AreEqual("本章暂无关卡。", Find("ChapterPanel/StatusLabel").GetComponent<Text>().text);
                Assert.AreEqual(Button("ChapterPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject);
                controller.Back();
                Button("LevelsPanel/Chapter07Button").onClick.Invoke();
                Assert.AreEqual(8, Find("ChapterPanel").GetComponentsInChildren<Button>().Count(b => b.name.StartsWith("Level")));
                var last = Button("ChapterPanel/Level08Button");
                Assert.AreEqual("自定义目标 7", last.transform.Find("Description").GetComponent<Text>().text);
                EventSystem.current.SetSelectedGameObject(last.gameObject);
                yield return null;
                yield return null;
                var scroll = Find("ChapterPanel/ScrollView").GetComponent<ScrollRect>();
                Assert.Greater(scroll.content.rect.width, scroll.viewport.rect.width, "新增关卡应扩展可滚动区域");
                Assert.Less(scroll.content.anchoredPosition.x, 0, "键盘选中末尾关卡应自动滚动到它");
                last.onClick.Invoke();
                Assert.AreEqual("自定义章节 6 · 自定义关卡 7待开放。\n目标：自定义目标 7", Find("ChapterPanel/StatusLabel").GetComponent<Text>().text);
                controller.Back();
                Assert.AreEqual(Button("LevelsPanel/Chapter07Button").gameObject, EventSystem.current.currentSelectedGameObject);
                catalog.chapters = new ChapterCatalog.Chapter[0];
                controller.OpenLevels();
                Assert.AreEqual("暂无章节配置", Find("LevelsPanel/Subtitle").GetComponent<Text>().text);
                Assert.AreEqual(Button("LevelsPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject);
            }
            finally
            {
                catalog.chapters = original;
                controller.OpenLevels();
            }
        }

        [UnityTest]
        public IEnumerator 空配置项_数量准确_返回匹配配置索引_空页面焦点保持可用()
        {
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var catalog = controller.Catalog;
            var original = catalog.chapters;
            try
            {
                catalog.chapters = new[]
                {
                    null,
                    new ChapterCatalog.Chapter
                    {
                        title = "只有一个有效关卡",
                        levels = new[] { null, new ChapterCatalog.Level { title = "配置第二关", goal = "保留完整目标" } }
                    },
                    null,
                    new ChapterCatalog.Chapter { title = "空章节", levels = null }
                };
                controller.OpenLevels();
                Assert.AreEqual("2 个章节 · 1 个关卡 · 可横向滑动", Find("LevelsPanel/Subtitle").GetComponent<Text>().text);
                var chapter = Button("LevelsPanel/Chapter02Button");
                Assert.AreEqual("1 个关卡 · 查看 →", chapter.transform.Find("Availability").GetComponent<Text>().text);
                chapter.onClick.Invoke();
                Assert.AreEqual("02", Find("ChapterPanel/ChapterMarker").GetComponent<Text>().text);
                Assert.AreEqual(Button("ChapterPanel/Level02Button").gameObject, EventSystem.current.currentSelectedGameObject);
                controller.Back();
                Assert.AreEqual(Button("LevelsPanel/Chapter02Button").gameObject, EventSystem.current.currentSelectedGameObject,
                    "空配置项不能导致返回时选择相邻章节");

                Button("LevelsPanel/Chapter04Button").onClick.Invoke();
                EventSystem.current.SetSelectedGameObject(null);
                controller.OpenNotice();
                controller.ClosePanels();
                Assert.AreEqual(Button("ChapterPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject,
                    "空章节关闭弹窗后应选择本页返回按钮");

                catalog.chapters = null;
                controller.OpenLevels();
                Assert.AreEqual("暂无章节配置", Find("LevelsPanel/Subtitle").GetComponent<Text>().text);
                Assert.AreEqual(Button("LevelsPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject);
                controller.OpenNotice();
                controller.ClosePanels();
                Assert.AreEqual(Button("LevelsPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject);
                yield return null;
            }
            finally
            {
                catalog.chapters = original;
                controller.OpenLevels();
            }
        }

        [UnityTest]
        public IEnumerator 卡片键盘导航_可到达末尾返回和试玩_数量减少后重置滚动()
        {
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            var catalog = controller.Catalog;
            var original = catalog.chapters;
            try
            {
                catalog.chapters = Enumerable.Range(0, 9).Select(c => new ChapterCatalog.Chapter
                {
                    title = "章节 " + c,
                    levels = Enumerable.Range(0, 9).Select(l => new ChapterCatalog.Level
                    { title = "关卡 " + l, goal = "完整目标 " + new string('金', 400) }).ToArray()
                }).ToArray();
                controller.OpenLevels();
                var firstChapter = Button("LevelsPanel/Chapter01Button");
                var lastChapter = Button("LevelsPanel/Chapter09Button");
                Assert.AreEqual(Button("LevelsPanel/BackButton"), firstChapter.navigation.selectOnLeft);
                Assert.AreEqual(Button("LevelsPanel/PrototypeButton"), lastChapter.navigation.selectOnRight);
                for (int i = 1; i < 9; i++) MoveSelectedRight();
                Assert.AreEqual(lastChapter.gameObject, EventSystem.current.currentSelectedGameObject);
                yield return null;
                AssertCardInViewport(Find("LevelsPanel/ScrollView").GetComponent<ScrollRect>(), lastChapter);

                lastChapter.onClick.Invoke();
                var lastLevel = Button("ChapterPanel/Level09Button");
                for (int i = 1; i < 9; i++) MoveSelectedRight();
                Assert.AreEqual(lastLevel.gameObject, EventSystem.current.currentSelectedGameObject);
                yield return null;
                AssertCardInViewport(Find("ChapterPanel/ScrollView").GetComponent<ScrollRect>(), lastLevel);
                lastLevel.onClick.Invoke();
                Assert.AreEqual(catalog.chapters[8].levels[8].goal, lastLevel.transform.Find("Description").GetComponent<Text>().text);
                var status = Find("ChapterPanel/StatusLabel").GetComponent<Text>();
                StringAssert.StartsWith("章节 8 · 关卡 8待开放。\n目标：完整目标 ", status.text);
                StringAssert.EndsWith("…", status.text);
                Assert.LessOrEqual(status.preferredHeight, status.rectTransform.rect.height + .1f,
                    "长目标的页底摘要应根据实际区域截取，不能被裁切");
                MoveSelectedRight();
                Assert.AreEqual(Button("ChapterPanel/BackButton").gameObject, EventSystem.current.currentSelectedGameObject);
                controller.Back();
                lastChapter = Button("LevelsPanel/Chapter09Button");
                Assert.AreEqual(lastChapter.gameObject, EventSystem.current.currentSelectedGameObject);
                AssertCardInViewport(Find("LevelsPanel/ScrollView").GetComponent<ScrollRect>(), lastChapter);

                catalog.chapters = new[] { catalog.chapters[0] };
                controller.OpenLevels();
                var scroll = Find("LevelsPanel/ScrollView").GetComponent<ScrollRect>();
                Assert.AreEqual(Button("LevelsPanel/Chapter01Button").gameObject, EventSystem.current.currentSelectedGameObject);
                AssertCardInViewport(scroll, Button("LevelsPanel/Chapter01Button"));
                Assert.AreEqual(0, scroll.horizontalNormalizedPosition, .001f);
            }
            finally
            {
                catalog.chapters = original;
                controller.OpenLevels();
            }
        }

        [UnityTest]
        public IEnumerator 设置与公告_遮挡背景_关闭恢复当前页面和焦点()
        {
            var navigation = Find("Navigation").GetComponent<CanvasGroup>();
            var settingsButton = Button("UtilityButtons/SettingsButton");
            EventSystem.current.SetSelectedGameObject(settingsButton.gameObject);
            settingsButton.onClick.Invoke();
            Assert.IsTrue(Find("SettingsPanel").gameObject.activeSelf);
            Assert.IsFalse(navigation.interactable);
            Assert.IsFalse(navigation.blocksRaycasts);
            Assert.AreEqual(Find("SettingsPanel/MusicSlider").gameObject, EventSystem.current.currentSelectedGameObject);
            Button("Menu/StartButton").onClick.Invoke();
            Assert.IsFalse(Find("LevelsPanel").gameObject.activeSelf, "弹窗打开时不能激活背景入口");
            Button("ModalBackdrop").onClick.Invoke();
            Assert.IsFalse(Find("SettingsPanel").gameObject.activeSelf);
            Assert.IsTrue(navigation.blocksRaycasts);
            Assert.AreEqual(settingsButton.gameObject, EventSystem.current.currentSelectedGameObject);

            Object.FindFirstObjectByType<MainMenuController>().Back();
            var noticeButton = Button("UtilityButtons/NoticeButton");
            EventSystem.current.SetSelectedGameObject(noticeButton.gameObject);
            noticeButton.onClick.Invoke();
            Assert.IsTrue(Find("NoticePanel").gameObject.activeSelf);
            Assert.IsTrue(Find("EntryPage").gameObject.activeSelf);
            Button("NoticePanel/CloseButton").onClick.Invoke();
            Assert.IsFalse(Find("NoticePanel").gameObject.activeSelf);
            Assert.AreEqual(noticeButton.gameObject, EventSystem.current.currentSelectedGameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 排行榜_打开关闭恢复焦点_无服务时不伪造登录和成绩()
        {
            var entry = Button("Menu/LeaderboardButton");
            var panel = Find("LeaderboardPanel").gameObject;
            var menu = Find("Navigation").GetComponent<CanvasGroup>();
            EventSystem.current.SetSelectedGameObject(entry.gameObject);
            entry.onClick.Invoke();
            Assert.IsTrue(panel.activeSelf);
            Assert.IsFalse(menu.interactable);
            Assert.IsFalse(menu.blocksRaycasts);
            Assert.AreEqual(Button("LeaderboardPanel/AccountCard/LoginButton").gameObject,
                EventSystem.current.currentSelectedGameObject);
            Assert.IsTrue(Find("LeaderboardPanel/EmptyState").gameObject.activeSelf);
            Assert.IsFalse(Find("LeaderboardPanel/ScrollView/Viewport/Content/Row01").gameObject.activeSelf);

            Button("LeaderboardPanel/AccountCard/LoginButton").onClick.Invoke();
            Assert.AreEqual("尚未登录 TapTap", Find("LeaderboardPanel/AccountCard/AccountLabel").GetComponent<Text>().text);
            StringAssert.Contains("暂不可用", Find("LeaderboardPanel/StatusLabel").GetComponent<Text>().text);
            Button("LeaderboardPanel/RefreshButton").onClick.Invoke();
            Assert.AreEqual("--:--.--", Find("LeaderboardPanel/AccountCard/PersonalBest").GetComponent<Text>().text);

            Button("LeaderboardPanel/CloseButton").onClick.Invoke();
            Assert.IsFalse(panel.activeSelf);
            Assert.IsTrue(menu.interactable);
            Assert.AreEqual(entry.gameObject, EventSystem.current.currentSelectedGameObject);

            entry.onClick.Invoke();
            Button("UtilityButtons/SettingsButton").onClick.Invoke();
            Assert.IsFalse(panel.activeSelf);
            Assert.IsTrue(Find("SettingsPanel").gameObject.activeSelf);
            Button("ModalBackdrop").onClick.Invoke();
            Assert.AreEqual(entry.gameObject, EventSystem.current.currentSelectedGameObject,
                "在弹窗之间切换后仍应返回原始菜单入口");
            yield return null;
        }

        [UnityTest]
        public IEnumerator 排行榜_绑定前十名_切换榜单清空旧数据_反复打开不重复回调()
        {
            var panel = Find("LeaderboardPanel").GetComponent<LeaderboardPanel>();
            int loginRequests = 0;
            panel.LoginRequested += () => loginRequests++;
            Button("Menu/LeaderboardButton").onClick.Invoke();
            Button("LeaderboardPanel/CloseButton").onClick.Invoke();
            Button("Menu/LeaderboardButton").onClick.Invoke();
            Button("LeaderboardPanel/AccountCard/LoginButton").onClick.Invoke();
            Assert.AreEqual(1, loginRequests);

            var entries = new LeaderboardPanel.Entry[11];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new LeaderboardPanel.Entry
                { Rank = i + 1, PlayerName = "测试玩家" + i, ElapsedMilliseconds = 62349 + i * 1000 };
            panel.ShowAccount("我", 62349, 3);
            panel.ShowEntries(entries);
            var content = Find("LeaderboardPanel/ScrollView/Viewport/Content");
            Assert.AreEqual(10, content.childCount);
            foreach (Transform row in content) Assert.IsTrue(row.gameObject.activeSelf);
            Assert.AreEqual("01:02.34", content.Find("Row01/Duration").GetComponent<Text>().text);
            Assert.AreEqual("10", content.Find("Row10/Rank").GetComponent<Text>().text);
            Assert.IsFalse(Find("LeaderboardPanel/EmptyState").gameObject.activeSelf);
            Assert.IsFalse(Button("LeaderboardPanel/AccountCard/LoginButton").interactable);
            Assert.AreEqual(Button("LeaderboardPanel/CloseButton"), Button("LeaderboardPanel/GlobalButton").navigation.selectOnUp);
            Assert.AreEqual(Button("LeaderboardPanel/GlobalButton"), Button("LeaderboardPanel/CloseButton").navigation.selectOnDown);

            Button("LeaderboardPanel/FriendsButton").onClick.Invoke();
            Assert.AreEqual(LeaderboardPanel.Scope.Friends, panel.SelectedScope);
            Assert.IsTrue(Find("LeaderboardPanel/FriendsIndicator").gameObject.activeSelf);
            Assert.IsFalse(Find("LeaderboardPanel/GlobalIndicator").gameObject.activeSelf);
            foreach (Transform row in content) Assert.IsFalse(row.gameObject.activeSelf);
            Assert.IsTrue(Find("LeaderboardPanel/EmptyState").gameObject.activeSelf);

            panel.ShowAccount(null, 62349, 3);
            Assert.AreEqual("--:--.--", Find("LeaderboardPanel/AccountCard/PersonalBest").GetComponent<Text>().text);
            Assert.AreEqual("暂无排名", Find("LeaderboardPanel/AccountCard/PersonalRank").GetComponent<Text>().text);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 音频设置_独立生效并在重新加载后保留()
        {
            Button("UtilityButtons/SettingsButton").onClick.Invoke();
            var music = Find("SettingsPanel/MusicSlider").GetComponent<Slider>();
            var effects = Find("SettingsPanel/EffectsSlider").GetComponent<Slider>();
            Assert.AreEqual(0.7f, music.value, 0.001f);
            Assert.AreEqual(0.6f, effects.value, 0.001f);
            music.value = 0f;
            effects.value = 0.35f;
            Assert.AreEqual(0f, GameAudio.Instance.transform.Find("Music").GetComponent<AudioSource>().volume);
            Assert.AreEqual(0.35f, GameAudio.Instance.transform.Find("Effects").GetComponent<AudioSource>().volume, 0.001f);
            Assert.AreEqual("0%", Find("SettingsPanel/MusicValue").GetComponent<Text>().text);
            Assert.AreEqual("35%", Find("SettingsPanel/EffectsValue").GetComponent<Text>().text);
            Button("SettingsPanel/DoneButton").onClick.Invoke();
            Assert.AreEqual(0f, PlayerPrefs.GetFloat(MusicKey));
            Assert.AreEqual(0.35f, PlayerPrefs.GetFloat(EffectsKey), 0.001f);

            Object.Destroy(GameAudio.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(MenuPath);
            yield return null;
            _canvas = Object.FindFirstObjectByType<Canvas>().transform;
            Button("UtilityButtons/SettingsButton").onClick.Invoke();
            Assert.AreEqual(0f, Find("SettingsPanel/MusicSlider").GetComponent<Slider>().value);
            Assert.AreEqual(0.35f, Find("SettingsPanel/EffectsSlider").GetComponent<Slider>().value, 0.001f);
            Assert.AreEqual(1, Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None).Length);
        }

        [UnityTest]
        public IEnumerator 替换美术_按钮素材和文字样式在面板切换后保留()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(2f, 2f, 2f, 2f));
            try
            {
                var button = Button("Menu/StartButton");
                var image = button.GetComponent<Image>();
                var label = button.transform.Find("Label").GetComponent<Text>();
                image.sprite = sprite;
                image.color = Color.white;
                label.color = Color.yellow;
                label.fontSize = 27;

                Button("Menu/StartButton").onClick.Invoke();
                Button("LevelsPanel/BackButton").onClick.Invoke();
                Button("UtilityButtons/SettingsButton").onClick.Invoke();
                Button("SettingsPanel/DoneButton").onClick.Invoke();
                yield return null;

                Assert.AreSame(sprite, image.sprite, "菜单逻辑不能覆盖美术 Sprite");
                Assert.AreEqual(Color.white, image.color, "菜单逻辑不能覆盖 Image 着色");
                Assert.AreEqual(Color.yellow, label.color);
                Assert.AreEqual(27, label.fontSize);
                Assert.AreEqual("开始游戏", label.text);
                Assert.IsTrue(button.IsInteractable());
            }
            finally
            {
                Object.Destroy(sprite);
                Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator 替换卡片模板美术_动态复制和重建后保持素材与文字样式()
        {
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(2f, 2f, 2f, 2f));
            var templates = new[]
            {
                Button("CardTemplates/ChapterCard"), Button("CardTemplates/LevelCard")
            };
            var originalSprites = templates.Select(t => t.GetComponent<Image>().sprite).ToArray();
            var originalImageColors = templates.Select(t => t.GetComponent<Image>().color).ToArray();
            var originalLabelColors = templates.Select(t => t.transform.Find("Label").GetComponent<Text>().color).ToArray();
            var originalFontSizes = templates.Select(t => t.transform.Find("Label").GetComponent<Text>().fontSize).ToArray();
            var originalFonts = templates.Select(t => t.transform.Find("Label").GetComponent<Text>().font).ToArray();
            var originalSizes = templates.Select(t => t.GetComponent<RectTransform>().sizeDelta).ToArray();
            var originalPositions = templates.Select(t => t.GetComponent<RectTransform>().anchoredPosition).ToArray();
            try
            {
                foreach (var template in templates)
                {
                    template.GetComponent<Image>().sprite = sprite;
                    template.GetComponent<Image>().color = Color.white;
                    var label = template.transform.Find("Label").GetComponent<Text>();
                    label.color = Color.yellow;
                    label.fontSize = 27;
                    template.GetComponent<RectTransform>().sizeDelta += new Vector2(16, 8);
                    template.GetComponent<RectTransform>().anchoredPosition = new Vector2(3, 0);
                }
                var controller = Object.FindFirstObjectByType<MainMenuController>();
                for (int rebuild = 0; rebuild < 2; rebuild++)
                {
                    controller.OpenLevels();
                    var chapter = Button("LevelsPanel/Chapter01Button");
                    Assert.AreSame(sprite, chapter.GetComponent<Image>().sprite);
                    Assert.AreEqual(Color.white, chapter.GetComponent<Image>().color);
                    Assert.AreEqual(Color.yellow, chapter.transform.Find("Label").GetComponent<Text>().color);
                    Assert.AreEqual(27, chapter.transform.Find("Label").GetComponent<Text>().fontSize);
                    Assert.AreSame(originalFonts[0], chapter.transform.Find("Label").GetComponent<Text>().font);
                    Assert.AreEqual(originalSizes[0] + new Vector2(16, 8), chapter.GetComponent<RectTransform>().sizeDelta);
                    Assert.AreEqual(new Vector2(3, 0), chapter.GetComponent<RectTransform>().anchoredPosition);
                    chapter.onClick.Invoke();
                    var level = Button("ChapterPanel/Level01Button");
                    Assert.AreSame(sprite, level.GetComponent<Image>().sprite);
                    Assert.AreEqual(Color.white, level.GetComponent<Image>().color);
                    Assert.AreEqual(Color.yellow, level.transform.Find("Label").GetComponent<Text>().color);
                    Assert.AreEqual(27, level.transform.Find("Label").GetComponent<Text>().fontSize);
                    Assert.AreSame(originalFonts[1], level.transform.Find("Label").GetComponent<Text>().font);
                    Assert.AreEqual(originalSizes[1] + new Vector2(16, 8), level.GetComponent<RectTransform>().sizeDelta);
                    Assert.AreEqual(new Vector2(3, 0), level.GetComponent<RectTransform>().anchoredPosition);
                    yield return null;
                }
            }
            finally
            {
                for (int i = 0; i < templates.Length; i++)
                {
                    templates[i].GetComponent<Image>().sprite = originalSprites[i];
                    templates[i].GetComponent<Image>().color = originalImageColors[i];
                    var label = templates[i].transform.Find("Label").GetComponent<Text>();
                    label.color = originalLabelColors[i];
                    label.fontSize = originalFontSizes[i];
                    templates[i].GetComponent<RectTransform>().sizeDelta = originalSizes[i];
                    templates[i].GetComponent<RectTransform>().anchoredPosition = originalPositions[i];
                }
                Object.Destroy(sprite);
                Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator 试玩入口_进入已有游戏场景且保留音量()
        {
            Button("Menu/StartButton").onClick.Invoke();
            Assert.AreEqual("MainMenu", SceneManager.GetActiveScene().name);
            Button("LevelsPanel/PrototypeButton").onClick.Invoke();
            yield return WaitForScene("Game");
            Assert.IsNotNull(Object.FindFirstObjectByType<GameSession>());
            Assert.IsNotNull(GameAudio.Instance);
            Assert.AreEqual(0.7f, GameAudio.Instance.MusicVolume, 0.001f);

            yield return SceneManager.LoadSceneAsync(MenuPath);
            yield return null;
            _canvas = Object.FindFirstObjectByType<Canvas>().transform;
            Assert.IsNull(Object.FindFirstObjectByType<GameSession>());
            Assert.AreEqual(1, Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None).Length);
            Button("EntryPage/EnterButton").onClick.Invoke();
            Button("Menu/StartButton").onClick.Invoke();
            Button("LevelsPanel/PrototypeButton").onClick.Invoke();
            yield return WaitForScene("Game");
            Assert.IsNotNull(Object.FindFirstObjectByType<GameSession>());
        }

        Transform Find(string path) => _canvas.Find(path) ?? _canvas.Find("Navigation/" + path);

        static void MoveSelectedRight()
        {
            var movement = new AxisEventData(EventSystem.current) { moveDir = MoveDirection.Right, moveVector = Vector2.right };
            ExecuteEvents.Execute(EventSystem.current.currentSelectedGameObject, movement, ExecuteEvents.moveHandler);
        }

        static void AssertCardInViewport(ScrollRect scroll, Button button)
        {
            Canvas.ForceUpdateCanvases();
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, button.transform);
            Assert.GreaterOrEqual(bounds.min.x, scroll.viewport.rect.xMin - 1f, "选中的卡片左边不能被裁切");
            Assert.LessOrEqual(bounds.max.x, scroll.viewport.rect.xMax + 1f, "选中的卡片右边不能被裁切");
            Assert.That(scroll.horizontalNormalizedPosition, Is.InRange(0f, 1f), "自动滚动不能超过内容边界");
        }
        Button Button(string path)
        {
            var direct = Find(path);
            if (direct != null) return direct.GetComponent<Button>();
            string[] parts = path.Split('/');
            return Find(parts[0]).GetComponentsInChildren<Button>().Single(b => b.name == parts[parts.Length - 1]);
        }

        static IEnumerator WaitForScene(string name)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (SceneManager.GetActiveScene().name != name && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name, "场景加载应在 10 秒内完成");
            yield return null;
        }

        static void Restore(string key, bool existed, float value)
        {
            if (existed) PlayerPrefs.SetFloat(key, value);
            else PlayerPrefs.DeleteKey(key);
        }
    }
}
