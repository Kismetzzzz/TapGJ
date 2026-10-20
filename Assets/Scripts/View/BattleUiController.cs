using System;
using System.Collections.Generic;
using System.Globalization;
using TapGJ.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>独立局内 UI：显示由外部传入的状态，预览模式可单独运行。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleUiController : MonoBehaviour
    {
        [Header("独立预览（接入玩法时关闭）")]
        [SerializeField] bool previewMode = true;
        [SerializeField, Min(1)] int roundLimit = 15;

        [Header("显示")]
        [SerializeField] Text objectiveLabel;
        [SerializeField] Text progressLabel;
        [SerializeField] Image progressFill;
        [SerializeField] Text timerLabel;
        [SerializeField] Text roundLabel;
        [SerializeField] Text selectionLabel;
        [SerializeField] RectTransform boardContent;

        [Header("操作")]
        [SerializeField] Button[] elementButtons;
        [SerializeField] CanvasGroup[] elementSelections;
        [SerializeField] BattleUiCard[] cards;
        [SerializeField] Button nextRoundButton;
        [SerializeField] Image endPlayerPhaseArtwork;
        [SerializeField] Sprite endDiscardPhaseSprite;
        [SerializeField] Button discardButton;
        [SerializeField] Button settingsButton;
        [SerializeField] Button settingsBackdrop;
        [SerializeField] Button closeSettingsButton;
        [SerializeField] GameObject settingsPanel;
        [SerializeField] CanvasGroup controls;

        [Header("玩法接入事件（卡牌参数为槽位索引，五行参数为枚举值）")]
        [SerializeField] UnityEvent nextRoundRequested = new UnityEvent();
        [SerializeField] UnityEvent<int> discardRequested = new UnityEvent<int>();
        [SerializeField] UnityEvent<int> cardSelected = new UnityEvent<int>();
        [SerializeField] UnityEvent<int> elementSelected = new UnityEvent<int>();

        UnityAction[] _cardActions, _elementActions;
        GameObject _returnFocus;
        Sprite _endPlayerPhaseSprite;
        float _elapsed;
        float _handLayoutReadyAt;
        bool _timerRunning = true, _actionsEnabled = true;
        bool _advanceEnabled = true, _discardEnabled = true;
        int _handCount = -1;
        int _displayedSecond = -1;
        public int Round { get; private set; } = 1;
        public int SelectedCardIndex { get; private set; } = -1;
        public int SelectedElementIndex { get; private set; } = -1;
        public RectTransform BoardContent => boardContent;
        public IReadOnlyList<BattleUiCard> Cards => cards;
        public bool SettingsOpen => settingsPanel.activeSelf;
        public event Action<bool> SettingsChanged;
        public UnityEvent NextRoundRequested => nextRoundRequested;
        public UnityEvent<int> DiscardRequested => discardRequested;
        public UnityEvent<int> CardSelected => cardSelected;
        public UnityEvent<int> ElementSelected => elementSelected;

        void OnEnable()
        {
            BindCardActions();
            _elementActions = new UnityAction[elementButtons.Length];
            for (int i = 0; i < elementButtons.Length; i++)
            {
                int index = i;
                _elementActions[i] = () => SelectElement(index);
                elementButtons[i].onClick.AddListener(_elementActions[i]);
            }
            nextRoundButton.onClick.AddListener(RequestNextRound);
            discardButton.onClick.AddListener(RequestDiscard);
            settingsButton.onClick.AddListener(OpenSettings);
            settingsBackdrop.onClick.AddListener(CloseSettings);
            closeSettingsButton.onClick.AddListener(CloseSettings);
            SetAdvanceLabel(nextRoundButton.GetComponentInChildren<Text>().text);
            SetRound(Round);
            SetElapsedSeconds(_elapsed);
            RefreshActions();
            if (previewMode) ArrangeHand();
        }

        void OnDisable()
        {
            UnbindCardActions();
            for (int i = 0; i < elementButtons.Length; i++) elementButtons[i].onClick.RemoveListener(_elementActions[i]);
            nextRoundButton.onClick.RemoveListener(RequestNextRound);
            discardButton.onClick.RemoveListener(RequestDiscard);
            settingsButton.onClick.RemoveListener(OpenSettings);
            settingsBackdrop.onClick.RemoveListener(CloseSettings);
            closeSettingsButton.onClick.RemoveListener(CloseSettings);
        }

        void BindCardActions()
        {
            _cardActions = new UnityAction[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                _cardActions[i] = () => SelectCard(index);
                cards[i].Button.onClick.AddListener(_cardActions[i]);
            }
        }

        void UnbindCardActions()
        {
            for (int i = 0; i < cards.Length; i++) cards[i].Button.onClick.RemoveListener(_cardActions[i]);
        }

        void Update()
        {
            if (previewMode && _timerRunning && !settingsPanel.activeSelf) SetElapsedSeconds(_elapsed + Time.unscaledDeltaTime);
            if (Input.GetButtonDown("Cancel"))
            {
                if (settingsPanel.activeSelf) CloseSettings();
                else ClearSelection();
            }
        }

        public void SetObjective(string objective, int completed, int target)
        {
            target = Mathf.Max(0, target);
            completed = Mathf.Clamp(completed, 0, target);
            objectiveLabel.text = objective ?? "";
            progressLabel.text = target > 0 ? $"任务进度  {completed} / {target}" : "任务进度  —";
            progressFill.fillAmount = target > 0 ? completed / (float)target : 0;
        }

        public static string FormatTime(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0;
            long whole = (long)Math.Min((double)int.MaxValue, Math.Max(0d, seconds));
            return (whole / 60).ToString("00", CultureInfo.InvariantCulture) + ":"
                + (whole % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public void SetElapsedSeconds(float seconds)
        {
            _elapsed = float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0 : Mathf.Clamp(seconds, 0, int.MaxValue);
            int whole = (int)Math.Min(int.MaxValue, (double)_elapsed);
            if (whole == _displayedSecond) return;
            _displayedSecond = whole;
            timerLabel.text = FormatTime(_elapsed);
        }

        public void SetTimerRunning(bool running) => _timerRunning = running;
        public void SetRound(int round, int limit)
        {
            roundLimit = Mathf.Max(1, limit);
            SetRound(round);
        }

        public void SetGoalProgress(GoalProgress progress)
        {
            objectiveLabel.text = progress.Description ?? "";
            progressLabel.text = progress.Label ?? "";
            progressFill.fillAmount = Mathf.Clamp01(progress.Fill);
        }

        public void SetSelectionHint(string hint) => selectionLabel.text = hint ?? "";
        public void ResetHand()
        {
            _handCount = -1;
            _handLayoutReadyAt = 0;
            foreach (var slot in cards) slot.ClearBinding();
            foreach (var effect in GetComponentsInChildren<CardDissolveEffect>(true)) BoardView.Kill(effect.gameObject);
        }

        public void SetGameplayActions(bool select, bool advance, bool discard, string advanceLabel)
        {
            _advanceEnabled = advance;
            _discardEnabled = discard;
            _actionsEnabled = select;
            SetAdvanceLabel(advanceLabel);
            RefreshActions();
        }

        void SetAdvanceLabel(string value)
        {
            var label = nextRoundButton.GetComponentInChildren<Text>();
            label.text = value;
            if (endPlayerPhaseArtwork == null) return;
            if (_endPlayerPhaseSprite == null) _endPlayerPhaseSprite = endPlayerPhaseArtwork.sprite;
            var sprite = value == "结束出牌" ? _endPlayerPhaseSprite :
                value == "结束弃牌" ? endDiscardPhaseSprite : null;
            bool showArtwork = sprite != null;
            if (showArtwork)
            {
                endPlayerPhaseArtwork.sprite = sprite;
                var rect = endPlayerPhaseArtwork.rectTransform;
                float height = Mathf.Max(1, ((RectTransform)nextRoundButton.transform).rect.height - 10);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(((RectTransform)nextRoundButton.transform).rect.width, height);
                endPlayerPhaseArtwork.preserveAspect = true;
            }
            label.enabled = !showArtwork;
            endPlayerPhaseArtwork.gameObject.SetActive(showArtwork);
            var background = nextRoundButton.GetComponent<Image>();
            background.enabled = !showArtwork;
            nextRoundButton.targetGraphic = showArtwork ? endPlayerPhaseArtwork : background;
        }

        public void SetModalBlocked(bool blocked) => controls.interactable = controls.blocksRaycasts = !blocked;

        /// <summary>按真实手牌刷新；只给已经从对局移除的牌播放消散。</summary>
        public float SetHand(IReadOnlyList<Card> hand)
        {
            bool changed = _handCount != hand.Count;
            for (int i = 0; !changed && i < hand.Count; i++) changed = cards[i].CardId != hand[i].Id;
            if (!changed) return Mathf.Max(0, _handLayoutReadyAt - Time.unscaledTime);
            bool firstLayout = _handCount < 0;
            int selectedId = SelectedCardIndex < 0 ? -1 : cards[SelectedCardIndex].CardId;
            var live = new HashSet<int>();
            foreach (var card in hand) live.Add(card.Id);
            var retained = new Dictionary<int, BattleUiCard>();
            var available = new Queue<BattleUiCard>();
            foreach (var slot in cards)
            {
                if (slot.CardId > 0 && live.Contains(slot.CardId)) retained.Add(slot.CardId, slot);
                else
                {
                    if (slot.CardId > 0 && slot.gameObject.activeSelf)
                    {
                        if (slot.PlayDissolve() != null)
                            _handLayoutReadyAt = Time.unscaledTime + CardDissolveEffect.Duration;
                    }
                    slot.ClearBinding();
                    slot.SetSelected(false);
                    available.Enqueue(slot);
                }
            }
            // 卡牌 ID 对应同一个界面对象；槽位变化只移动它，不把别的卡面换进来。
            UnbindCardActions();
            var ordered = new BattleUiCard[Mathf.Max(cards.Length, hand.Count)];
            for (int i = 0; i < hand.Count; i++)
            {
                if (!retained.TryGetValue(hand[i].Id, out var slot))
                {
                    slot = available.Count > 0 ? available.Dequeue() :
                        Instantiate(cards[0], cards[0].transform.parent, false);
                    slot.ClearBinding();
                    slot.Bind(hand[i]);
                }
                slot.gameObject.SetActive(true);
                ordered[i] = slot;
            }
            for (int i = hand.Count; i < ordered.Length; i++)
            {
                ordered[i] = available.Dequeue();
                ordered[i].gameObject.SetActive(false);
            }
            cards = ordered;
            BindCardActions();
            SelectedCardIndex = -1;
            _handCount = hand.Count;
            // 消散中的卡牌仍占据原位置，结束后再让其他手牌补位。
            LayoutHand(hand.Count, firstLayout, Mathf.Max(0, _handLayoutReadyAt - Time.unscaledTime));
            SetSelectedCardId(selectedId);
            return Mathf.Max(0, _handLayoutReadyAt - Time.unscaledTime);
        }

        public void ArrangeHand() => LayoutHand(_handCount < 0 ? cards.Length : _handCount, true);

        void LayoutHand(int count, bool immediate, float delay = 0)
        {
            if (count == 0) return;
            var parent = (RectTransform)cards[0].transform.parent;
            var size = cards[0].ArtworkSize;
            float scale = Mathf.Min(.8f, (parent.rect.width - 32) / (size.x * (1 + 1.22f * (count - 1))),
                (parent.rect.height - 40) / (size.y + cards[0].SelectionLift));
            float height = size.y * scale;
            float step = size.x * scale * 1.22f;
            float start = parent.rect.width * .5f - (count - 1) * step * .5f;
            for (int i = 0; i < count; i++)
            {
                float fan = count == 1 ? 0 : (i / (float)(count - 1) - .5f) * 2;
                var position = new Vector2(start + i * step,
                    -parent.rect.height + height * .5f + 10 + (1 - fan * fan) * 2);
                cards[i].SetHandPose(position, -fan * 3f, scale, i, immediate, delay);
            }
        }

        public void SetSelectedCardId(int cardId)
        {
            SelectedCardIndex = -1;
            for (int i = 0; i < cards.Length; i++)
            {
                bool selected = cardId > 0 && cards[i].CardId == cardId && cards[i].gameObject.activeSelf;
                cards[i].SetSelected(selected);
                if (selected) SelectedCardIndex = i;
            }
            SetElementSelection(SelectedCardIndex >= 0 && cards[SelectedCardIndex].Kind == CardKind.ElementCard
                ? (int)cards[SelectedCardIndex].Element : -1);
            RefreshActions();
        }
        public void SetRound(int round)
        {
            Round = Mathf.Clamp(round, 1, Mathf.Max(1, roundLimit));
            roundLabel.text = $"第 {Round:00} 回合 / {roundLimit:00}";
        }

        public void SetActionsEnabled(bool enabled)
        {
            _actionsEnabled = enabled;
            RefreshActions();
        }

        void RefreshActions()
        {
            nextRoundButton.interactable = _actionsEnabled && _advanceEnabled;
            discardButton.interactable = _actionsEnabled && _discardEnabled && SelectedCardIndex >= 0;
            foreach (var card in cards) card.Button.interactable = _actionsEnabled;
            foreach (var button in elementButtons) button.interactable = _actionsEnabled;
        }

        public void SelectCard(int index)
        {
            if (!_actionsEnabled || settingsPanel.activeSelf || index < 0 || index >= cards.Length
                || !cards[index].gameObject.activeSelf) return;
            SelectedCardIndex = SelectedCardIndex == index ? -1 : index;
            for (int i = 0; i < cards.Length; i++) cards[i].SetSelected(i == SelectedCardIndex);
            SetElementSelection(SelectedCardIndex >= 0 && cards[SelectedCardIndex].Kind == CardKind.ElementCard
                ? (int)cards[SelectedCardIndex].Element : -1);
            selectionLabel.text = SelectedCardIndex < 0 ? "选择一张手牌" :
                $"已选择 · {cards[SelectedCardIndex].Label}";
            RefreshActions();
            cardSelected.Invoke(SelectedCardIndex);
        }

        public void ClearSelection()
        {
            bool wasSelected = SelectedCardIndex >= 0;
            SelectedCardIndex = -1;
            foreach (var card in cards) card.SetSelected(false);
            SetElementSelection(-1);
            selectionLabel.text = "选择一张手牌";
            RefreshActions();
            if (wasSelected) cardSelected.Invoke(-1);
        }

        public void SelectElement(int index)
        {
            if (!_actionsEnabled || settingsPanel.activeSelf || index < 0 || index >= elementButtons.Length) return;
            SetElementSelection(index);
            elementSelected.Invoke(index);
        }

        void SetElementSelection(int index)
        {
            SelectedElementIndex = index;
            for (int i = 0; i < elementSelections.Length; i++) elementSelections[i].alpha = i == index ? 1 : 0;
        }

        void RequestNextRound()
        {
            if (!_actionsEnabled || settingsPanel.activeSelf) return;
            if (previewMode) { SetRound(Round + 1); ClearSelection(); }
            nextRoundRequested.Invoke();
        }

        void RequestDiscard()
        {
            if (!_actionsEnabled || settingsPanel.activeSelf || SelectedCardIndex < 0) return;
            int index = SelectedCardIndex;
            if (previewMode) ConsumeCard(index);
            discardRequested.Invoke(index);
        }

        /// <summary>外部玩法确认出牌或弃牌成功后，用同一入口播放消散。</summary>
        public void ConsumeCard(int index)
        {
            if (index < 0 || index >= cards.Length || !cards[index].gameObject.activeSelf) return;
            cards[index].PlayDissolve();
            if (SelectedCardIndex == index) ClearSelection();
        }

        public void OpenSettings()
        {
            if (settingsPanel.activeSelf) return;
            _returnFocus = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            controls.interactable = controls.blocksRaycasts = false;
            settingsBackdrop.gameObject.SetActive(true);
            settingsPanel.SetActive(true);
            SettingsChanged?.Invoke(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(closeSettingsButton.gameObject);
        }

        public void CloseSettings()
        {
            if (!settingsPanel.activeSelf) return;
            settingsBackdrop.gameObject.SetActive(false);
            settingsPanel.SetActive(false);
            SettingsChanged?.Invoke(false);
            controls.interactable = controls.blocksRaycasts = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(
                _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : settingsButton.gameObject);
        }
    }
}
