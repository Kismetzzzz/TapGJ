using System.Collections.Generic;
using System.Globalization;
using TapGJ.Flow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>管理已保存的页面，按章节配置复制卡片模板。</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("游戏入口（Build Settings 中的场景路径）")]
        [SerializeField] string gameplayScenePath = "Assets/Scenes/Game.unity";

        [Header("页面流程")]
        [SerializeField] GameObject entryPage;
        [SerializeField] GameObject mainMenuPage;
        [SerializeField] GameObject utilityButtons;
        [SerializeField] Button enterButton;
        [Header("章节与关卡（数量由配置数组决定）")]
        [SerializeField] ChapterCatalog chapterCatalog;
        [SerializeField] GameObject chapterPanel;
        [SerializeField] Button chapterCardTemplate;
        [SerializeField] Button levelCardTemplate;
        [SerializeField] ScrollRect chaptersScroll;
        [SerializeField] ScrollRect levelsScroll;
        [SerializeField] Text chaptersSummaryLabel;
        [SerializeField] Text chapterTitleLabel;
        [SerializeField] Text chapterMarkerLabel;
        [SerializeField] Text chapterStatusLabel;
        [SerializeField] Button closeChapterButton;

        [Header("主菜单")]
        [SerializeField] CanvasGroup menuGroup;
        [SerializeField] Button startButton;
        [SerializeField] Button leaderboardButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button userButton;
        [SerializeField] Button noticeButton;
        [SerializeField] Button quitButton;
        [SerializeField] Text statusLabel;

        [Header("面板")]
        [SerializeField] Button backdropButton;
        [SerializeField] GameObject levelsPanel;
        [SerializeField] GameObject settingsPanel;
        [SerializeField] GameObject noticePanel;
        [SerializeField] LeaderboardPanel leaderboardPanel;
        [SerializeField] Button playLevelButton;
        [SerializeField] Button closeLevelsButton;
        [SerializeField] Button closeSettingsButton;
        [SerializeField] Button closeLeaderboardButton;
        [SerializeField] Button closeNoticeButton;
        [SerializeField] Button settingsBackButton;
        [SerializeField] Button settingsHelpButton;
        [SerializeField] Button settingsAboutButton;
        [SerializeField] Text noticeTitleLabel;
        [SerializeField] Text noticeBodyLabel;
        [SerializeField] GameObject noticeArtwork;
        [SerializeField] GameObject informationArtwork;

        [Header("音频设置")]
        [SerializeField] GameAudio audioSettings;
        [SerializeField] Slider musicSlider;
        [SerializeField] Slider effectsSlider;
        [SerializeField] Text musicValueLabel;
        [SerializeField] Text effectsValueLabel;

        GameObject _returnFocus;
        bool _loading;
        int _selectedChapter = -1;
        readonly List<Button> _chapterButtons = new List<Button>();
        readonly List<int> _chapterIndices = new List<int>();
        readonly List<Button> _levelButtons = new List<Button>();
        GameObject _lastSelected;
        bool _noticeReturnsToSettings;
        GameObject _settingsNoticeFocus;
        string _noticeTitle, _noticeBody;

        public ChapterCatalog Catalog => chapterCatalog;

        GameAudio Audio => GameAudio.Instance != null ? GameAudio.Instance : audioSettings;

        void OnEnable()
        {
            _noticeTitle = noticeTitleLabel.text;
            _noticeBody = noticeBodyLabel.text;
            enterButton.onClick.AddListener(EnterMenu);
            startButton.onClick.AddListener(OpenLevels);
            leaderboardButton.onClick.AddListener(OpenLeaderboard);
            userButton.onClick.AddListener(OpenLeaderboard);
            noticeButton.onClick.AddListener(OpenNotice);
            settingsButton.onClick.AddListener(OpenSettings);
            quitButton.onClick.AddListener(QuitGame);
            backdropButton.onClick.AddListener(ClosePanels);
            playLevelButton.onClick.AddListener(StartGame);
            closeLevelsButton.onClick.AddListener(Back);
            closeSettingsButton.onClick.AddListener(ClosePanels);
            settingsBackButton.onClick.AddListener(ClosePanels);
            settingsHelpButton.onClick.AddListener(OpenSettingsHelp);
            settingsAboutButton.onClick.AddListener(OpenSettingsAbout);
            closeLeaderboardButton.onClick.AddListener(ClosePanels);
            closeNoticeButton.onClick.AddListener(ClosePanels);
            closeChapterButton.onClick.AddListener(Back);
            musicSlider.onValueChanged.AddListener(ChangeMusicVolume);
            effectsSlider.onValueChanged.AddListener(ChangeEffectsVolume);
            BuildChapterCards();
            ClosePanels();
            ShowPage(entryPage, enterButton.gameObject);
        }

        void Start() => Select(enterButton.gameObject);

        void OnDisable()
        {
            enterButton.onClick.RemoveListener(EnterMenu);
            startButton.onClick.RemoveListener(OpenLevels);
            leaderboardButton.onClick.RemoveListener(OpenLeaderboard);
            userButton.onClick.RemoveListener(OpenLeaderboard);
            noticeButton.onClick.RemoveListener(OpenNotice);
            settingsButton.onClick.RemoveListener(OpenSettings);
            quitButton.onClick.RemoveListener(QuitGame);
            backdropButton.onClick.RemoveListener(ClosePanels);
            playLevelButton.onClick.RemoveListener(StartGame);
            closeLevelsButton.onClick.RemoveListener(Back);
            closeSettingsButton.onClick.RemoveListener(ClosePanels);
            settingsBackButton.onClick.RemoveListener(ClosePanels);
            settingsHelpButton.onClick.RemoveListener(OpenSettingsHelp);
            settingsAboutButton.onClick.RemoveListener(OpenSettingsAbout);
            closeLeaderboardButton.onClick.RemoveListener(ClosePanels);
            closeNoticeButton.onClick.RemoveListener(ClosePanels);
            closeChapterButton.onClick.RemoveListener(Back);
            musicSlider.onValueChanged.RemoveListener(ChangeMusicVolume);
            effectsSlider.onValueChanged.RemoveListener(ChangeEffectsVolume);
            if (Audio != null) Audio.SaveSettings();
        }

        void Update()
        {
            if (Input.GetButtonDown("Cancel")) Back();
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected != _lastSelected)
            {
                _lastSelected = selected;
                if (levelsPanel.activeSelf) RevealSelected(chaptersScroll, selected);
                else if (chapterPanel.activeSelf) RevealSelected(levelsScroll, selected);
            }
        }

        public void EnterMenu()
        {
            if (_loading || !menuGroup.interactable) return;
            ShowPage(mainMenuPage, startButton.gameObject);
        }

        public void OpenLevels()
        {
            if (_loading || !menuGroup.interactable) return;
            BuildChapterCards();
            ShowPage(levelsPanel, ChapterFocus());
        }

        public void OpenChapter(int index)
        {
            if (_loading || !menuGroup.interactable || chapterCatalog == null || chapterCatalog.chapters == null
                || index < 0 || index >= chapterCatalog.chapters.Length || chapterCatalog.chapters[index] == null) return;
            _selectedChapter = index;
            var chapter = chapterCatalog.chapters[index];
            chapterTitleLabel.text = CardTitle(chapter.title, "未命名章节") + " · 关卡选择";
            chapterMarkerLabel.text = (index + 1).ToString("00");
            ClearCards(_levelButtons);
            if (chapter.levels != null)
                for (int i = 0; i < chapter.levels.Length; i++)
                {
                    if (chapter.levels[i] == null) continue;
                    int levelIndex = i;
                    var level = chapter.levels[i];
                    var button = CreateCard(levelCardTemplate, levelsScroll.content, "Level" + (i + 1).ToString("00") + "Button",
                        CardTitle(level.title, "未命名关卡"), level.goal, chapterCatalog.Badge(index, i), i);
                    button.onClick.AddListener(() => PreviewLevel(levelIndex));
                    _levelButtons.Add(button);
                }
            chapterStatusLabel.text = _levelButtons.Count == 0
                ? "本章暂无关卡。"
                : chapterCatalog.HasUnlockedLevel(index)
                    ? "选择关卡查看目标；点「开始」进入。"
                    : "选择关卡查看目标；关卡内容待开放。";
            ConfigureCardNavigation(_levelButtons, closeChapterButton);
            ShowPage(chapterPanel, _levelButtons.Count > 0 ? _levelButtons[0].gameObject : closeChapterButton.gameObject);
        }

        public void PreviewLevel(int levelIndex)
        {
            if (_loading || !menuGroup.interactable || _selectedChapter < 0
                || !chapterPanel.activeSelf || chapterCatalog == null || chapterCatalog.chapters == null
                || _selectedChapter >= chapterCatalog.chapters.Length) return;
            var chapter = chapterCatalog.chapters[_selectedChapter];
            if (chapter == null || chapter.levels == null || levelIndex < 0 || levelIndex >= chapter.levels.Length
                || chapter.levels[levelIndex] == null) return;
            var level = chapter.levels[levelIndex];
            if (level.definition != null)
            {
                if (!chapterCatalog.IsUnlocked(_selectedChapter, levelIndex))
                {
                    var previous = chapterCatalog.PreviousBound(_selectedChapter, levelIndex);
                    chapterStatusLabel.text = previous == null
                        ? "这一关还没开放。"
                        : $"还没解锁：先通关「{CardTitle(previous.title, "上一关")}」。";
                    return;
                }

                var definition = level.definition;
                if (string.IsNullOrWhiteSpace(definition.sceneName)
                    || !Application.CanStreamedLevelBeLoaded(definition.sceneName))
                {
                    chapterStatusLabel.text = "关卡暂不可用，请选择其他关卡。";
                    Debug.LogError($"[TapGJ] 关卡「{definition.levelId}」的场景未加入 Build Settings：{definition.sceneName}", this);
                    return;
                }

                ClosePanels();
                if (Audio != null) Audio.SaveSettings();
                _loading = true;
                menuGroup.interactable = false;
                chapterStatusLabel.text = "正在进入关卡…";
                SceneFlow.LoadLevel(definition);
                return;
            }
            string heading = CardTitle(chapter.title, "未命名章节") + " · "
                + CardTitle(level.title, "未命名关卡") + "待开放。";
            chapterStatusLabel.text = GoalStatus(heading, level.goal);
        }

        string GoalStatus(string heading, string goal)
        {
            if (string.IsNullOrWhiteSpace(goal)) return heading;
            string summary = goal.Trim().Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            string prefix = heading + "\n目标：";
            var settings = chapterStatusLabel.GetGenerationSettings(new Vector2(chapterStatusLabel.GetPixelAdjustedRect().size.x, 0f));
            float availableHeight = chapterStatusLabel.rectTransform.rect.height * chapterStatusLabel.pixelsPerUnit;
            bool Fits(string value) => chapterStatusLabel.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings)
                <= availableHeight + .1f;
            if (Fits(prefix + summary)) return prefix + summary;
            // ponytail: 页底只放能容纳的摘要，完整目标保留在卡片；以后有详情页时再迁移。
            if (!Fits(prefix + "…")) return heading;
            var characters = StringInfo.ParseCombiningCharacters(summary);
            int low = 0, high = characters.Length;
            while (low < high)
            {
                int count = (low + high + 1) / 2;
                int end = count < characters.Length ? characters[count] : summary.Length;
                if (Fits(prefix + summary.Substring(0, end) + "…")) low = count;
                else high = count - 1;
            }
            return prefix + summary.Substring(0, low < characters.Length ? characters[low] : summary.Length) + "…";
        }

        void ShowPage(GameObject page, GameObject firstSelected)
        {
            entryPage.SetActive(page == entryPage);
            mainMenuPage.SetActive(page == mainMenuPage);
            levelsPanel.SetActive(page == levelsPanel);
            utilityButtons.SetActive(page == entryPage || page == mainMenuPage);
            chapterPanel.SetActive(page == chapterPanel);
            statusLabel.text = "";
            ScrollRect scroll = page == levelsPanel ? chaptersScroll : page == chapterPanel ? levelsScroll : null;
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                scroll.StopMovement();
                scroll.horizontalNormalizedPosition = 0;
            }
            Select(firstSelected);
            if (scroll != null) RevealSelected(scroll, firstSelected);
            _lastSelected = firstSelected;
        }

        public void Back()
        {
            if (_loading) return;
            if (settingsPanel.activeSelf || leaderboardPanel.gameObject.activeSelf || noticePanel.activeSelf)
            {
                ClosePanels();
                return;
            }
            if (chapterPanel.activeSelf) OpenLevels();
            else if (levelsPanel.activeSelf) EnterMenu();
            else if (mainMenuPage.activeSelf) ShowPage(entryPage, enterButton.gameObject);
        }

        public void OpenNotice()
        {
            if (_loading) return;
            _noticeReturnsToSettings = false;
            noticeTitleLabel.text = _noticeTitle;
            noticeBodyLabel.text = _noticeBody;
            ShowNoticeArtwork(true);
            OpenPanel(noticePanel, closeNoticeButton.gameObject);
        }

        void ShowNoticeArtwork(bool announcement)
        {
            noticeArtwork.SetActive(announcement);
            informationArtwork.SetActive(!announcement);
            noticeTitleLabel.enabled = !announcement;
        }

        void OpenSettingsHelp() => OpenSettingsNotice("帮助", "拖动「音效」或「音乐」滑块调整音量，滑到最左侧即可静音。\n\n点击左上角返回、底部「返回游戏」或按 Esc，保存设置并返回原页面。", settingsHelpButton);

        void OpenSettingsAbout() => OpenSettingsNotice("关于", "元素丕定\n\n五行元素卡牌对战\n相生相克，尽在一局之间。", settingsAboutButton);

        void OpenSettingsNotice(string title, string body, Button origin)
        {
            if (_loading || !settingsPanel.activeSelf) return;
            _noticeReturnsToSettings = true;
            _settingsNoticeFocus = origin.gameObject;
            noticeTitleLabel.text = title;
            noticeBodyLabel.text = body;
            ShowNoticeArtwork(false);
            OpenPanel(noticePanel, closeNoticeButton.gameObject);
        }

        public void OpenSettings()
        {
            if (_loading) return;
            musicSlider.SetValueWithoutNotify(Audio.MusicVolume);
            effectsSlider.SetValueWithoutNotify(Audio.EffectsVolume);
            RefreshVolumeLabels();
            OpenPanel(settingsPanel, musicSlider.gameObject);
        }

        public void OpenLeaderboard()
        {
            if (_loading || !leaderboardButton.interactable) return;
            OpenPanel(leaderboardPanel.gameObject, leaderboardPanel.FirstSelectedButton.gameObject);
        }

        void OpenPanel(GameObject panel, GameObject firstSelected)
        {
            if (menuGroup.interactable)
                _returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (settingsPanel.activeSelf) Audio.SaveSettings();
            settingsPanel.SetActive(panel == settingsPanel);
            leaderboardPanel.gameObject.SetActive(panel == leaderboardPanel.gameObject);
            noticePanel.SetActive(panel == noticePanel);
            backdropButton.gameObject.SetActive(true);
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
            Select(firstSelected);
        }

        public void ClosePanels()
        {
            if (_loading) return;
            if (_noticeReturnsToSettings && noticePanel.activeSelf)
            {
                _noticeReturnsToSettings = false;
                OpenSettings();
                if (CanSelect(_settingsNoticeFocus)) Select(_settingsNoticeFocus);
                _settingsNoticeFocus = null;
                return;
            }
            if (settingsPanel.activeSelf && Audio != null) Audio.SaveSettings();
            settingsPanel.SetActive(false);
            leaderboardPanel.gameObject.SetActive(false);
            noticePanel.SetActive(false);
            backdropButton.gameObject.SetActive(false);
            menuGroup.interactable = true;
            menuGroup.blocksRaycasts = true;
            GameObject fallback = entryPage.activeSelf ? enterButton.gameObject :
                mainMenuPage.activeSelf ? startButton.gameObject :
                levelsPanel.activeSelf ? ChapterFocus() :
                chapterPanel.activeSelf && _levelButtons.Count > 0 ? _levelButtons[0].gameObject : closeChapterButton.gameObject;
            Select(CanSelect(_returnFocus) ? _returnFocus : fallback);
            _returnFocus = null;
        }

        void ChangeMusicVolume(float value)
        {
            Audio.SetMusicVolume(value);
            RefreshVolumeLabels();
        }

        void ChangeEffectsVolume(float value)
        {
            Audio.SetEffectsVolume(value);
            RefreshVolumeLabels();
        }

        void RefreshVolumeLabels()
        {
            musicValueLabel.text = $"{Mathf.RoundToInt(Audio.MusicVolume * 100f)}%";
            effectsValueLabel.text = $"{Mathf.RoundToInt(Audio.EffectsVolume * 100f)}%";
        }

        public void StartGame()
        {
            if (_loading || !menuGroup.interactable) return;
            int sceneIndex = SceneUtility.GetBuildIndexByScenePath(gameplayScenePath);
            if (sceneIndex < 0 || !Application.CanStreamedLevelBeLoaded(sceneIndex))
            {
                statusLabel.text = "关卡暂不可用，请检查构建场景。";
                Debug.LogError($"[TapGJ] 游戏场景未加入 Build Settings：{gameplayScenePath}", this);
                return;
            }

            ClosePanels();
            Audio.SaveSettings();
            _loading = true;
            menuGroup.interactable = false;
            statusLabel.text = "正在进入游戏…";
            SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
        }

        public void QuitGame()
        {
            if (_loading) return;
            Audio.SaveSettings();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void BuildChapterCards()
        {
            ClearCards(_chapterButtons);
            _chapterIndices.Clear();
            int levelCount = 0;
            if (chapterCatalog != null && chapterCatalog.chapters != null)
                for (int i = 0; i < chapterCatalog.chapters.Length; i++)
                {
                    var chapter = chapterCatalog.chapters[i];
                    if (chapter == null) continue;
                    int chapterIndex = i;
                    int chapterLevelCount = 0;
                    if (chapter.levels != null)
                        foreach (var level in chapter.levels) if (level != null) chapterLevelCount++;
                    levelCount += chapterLevelCount;
                    var button = CreateCard(chapterCardTemplate, chaptersScroll.content, "Chapter" + (i + 1).ToString("00") + "Button",
                        CardTitle(chapter.title, "未命名章节"), chapter.description,
                        chapterLevelCount > 0 ? $"{chapterLevelCount} 个关卡 · 查看 →" : "暂无关卡 · 查看 →", i, chapter.artwork);
                    button.onClick.AddListener(() => OpenChapter(chapterIndex));
                    _chapterButtons.Add(button);
                    _chapterIndices.Add(chapterIndex);
                }
            chaptersSummaryLabel.text = _chapterButtons.Count > 0
                ? $"{_chapterButtons.Count} 个章节 · {levelCount} 个关卡 · 可横向滑动" : "暂无章节配置";
            ConfigureCardNavigation(_chapterButtons, closeLevelsButton, playLevelButton);
        }

        GameObject ChapterFocus()
        {
            if (_chapterButtons.Count == 0) return closeLevelsButton.gameObject;
            int visibleIndex = _chapterIndices.IndexOf(_selectedChapter);
            return _chapterButtons[visibleIndex >= 0 ? visibleIndex : 0].gameObject;
        }

        static string CardTitle(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        static void ConfigureCardNavigation(List<Button> cards, Button back, Button extra = null)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = i > 0 ? cards[i - 1] : back,
                    selectOnRight = i + 1 < cards.Count ? cards[i + 1] : extra != null ? extra : back,
                    selectOnUp = back,
                    selectOnDown = extra != null ? extra : back
                };
            }
            var first = cards.Count > 0 ? cards[0] : extra;
            var last = cards.Count > 0 ? cards[cards.Count - 1] : extra;
            back.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = extra != null ? extra : last,
                selectOnRight = first,
                selectOnUp = last,
                selectOnDown = first
            };
            if (extra != null)
                extra.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = cards.Count > 0 ? last : back,
                    selectOnRight = back,
                    selectOnUp = cards.Count > 0 ? first : back,
                    selectOnDown = back
                };
        }

        static Button CreateCard(Button template, RectTransform content, string name, string title, string goal, string availability, int index,
            Sprite artwork = null)
        {
            var templateRect = template.GetComponent<RectTransform>();
            var slot = new GameObject(name + "Slot", typeof(RectTransform), typeof(LayoutElement));
            slot.layer = template.gameObject.layer;
            slot.transform.SetParent(content, false);
            var layout = slot.GetComponent<LayoutElement>();
            layout.preferredWidth = templateRect.sizeDelta.x;
            layout.preferredHeight = templateRect.sizeDelta.y;
            var button = Instantiate(template, slot.transform, false);
            button.name = name;
            button.transform.Find("Label").GetComponent<Text>().text = title;
            button.transform.Find("Description").GetComponent<Text>().text = goal;
            button.transform.Find("Availability").GetComponent<Text>().text = availability;
            var number = button.transform.Find("Number");
            if (number != null) number.GetComponent<Text>().text = (index + 1).ToString("00");
            var illustration = button.transform.Find("Artwork");
            if (illustration != null && artwork != null)
            {
                var image = illustration.GetComponent<Image>();
                image.sprite = artwork;
                image.SetNativeSize();
                illustration.gameObject.SetActive(true);
                button.GetComponent<Image>().enabled = false;
                var hitArea = (RectTransform)button.transform.Find("ArtworkHitArea");
                hitArea.sizeDelta = image.rectTransform.sizeDelta;
                hitArea.gameObject.SetActive(true);
                foreach (var binding in button.GetComponentsInChildren<UiFontBinding>(true)) binding.Refresh();
                foreach (Transform child in button.transform)
                    if (child != illustration && child != hitArea && child.name != "FocusRing") child.gameObject.SetActive(false);
                var focus = (RectTransform)button.transform.Find("FocusRing");
                focus.anchorMin = focus.anchorMax = focus.pivot = new Vector2(.5f, .5f);
                focus.anchoredPosition = image.rectTransform.anchoredPosition;
                focus.sizeDelta = image.rectTransform.sizeDelta + Vector2.one * 6;
            }
            button.gameObject.SetActive(true);
            return button;
        }

        static void ClearCards(List<Button> buttons)
        {
            foreach (var button in buttons)
            {
                if (button == null) continue;
                button.transform.parent.gameObject.SetActive(false);
                Destroy(button.transform.parent.gameObject);
            }
            buttons.Clear();
        }

        static void RevealSelected(ScrollRect scroll, GameObject selected)
        {
            if (selected == null || !selected.transform.IsChildOf(scroll.content)) return;
            Canvas.ForceUpdateCanvases();
            float hiddenWidth = scroll.content.rect.width - scroll.viewport.rect.width;
            if (hiddenWidth <= 0) return;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport, selected.transform);
            float shift = bounds.min.x < scroll.viewport.rect.xMin ? scroll.viewport.rect.xMin - bounds.min.x :
                bounds.max.x > scroll.viewport.rect.xMax ? scroll.viewport.rect.xMax - bounds.max.x : 0;
            if (Mathf.Abs(shift) > .01f)
            {
                scroll.StopMovement();
                scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition - shift / hiddenWidth);
            }
        }

        static bool CanSelect(GameObject target)
        {
            if (target == null || !target.activeInHierarchy) return false;
            var selectable = target.GetComponent<Selectable>();
            return selectable != null && selectable.IsInteractable();
        }

        static void Select(GameObject target)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(CanSelect(target) ? target : null);
        }
    }
}
