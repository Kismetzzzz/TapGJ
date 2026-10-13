using System;
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
        float _elapsed;
        bool _timerRunning = true, _actionsEnabled = true;
        int _displayedSecond = -1;
        public int Round { get; private set; } = 1;
        public int SelectedCardIndex { get; private set; } = -1;
        public int SelectedElementIndex { get; private set; } = -1;
        public RectTransform BoardContent => boardContent;
        public UnityEvent NextRoundRequested => nextRoundRequested;
        public UnityEvent<int> DiscardRequested => discardRequested;
        public UnityEvent<int> CardSelected => cardSelected;
        public UnityEvent<int> ElementSelected => elementSelected;

        void OnEnable()
        {
            _cardActions = new UnityAction[cards.Length];
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                _cardActions[i] = () => SelectCard(index);
                cards[i].Button.onClick.AddListener(_cardActions[i]);
            }
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
            SetRound(Round);
            SetElapsedSeconds(_elapsed);
            RefreshActions();
        }

        void OnDisable()
        {
            for (int i = 0; i < cards.Length; i++) cards[i].Button.onClick.RemoveListener(_cardActions[i]);
            for (int i = 0; i < elementButtons.Length; i++) elementButtons[i].onClick.RemoveListener(_elementActions[i]);
            nextRoundButton.onClick.RemoveListener(RequestNextRound);
            discardButton.onClick.RemoveListener(RequestDiscard);
            settingsButton.onClick.RemoveListener(OpenSettings);
            settingsBackdrop.onClick.RemoveListener(CloseSettings);
            closeSettingsButton.onClick.RemoveListener(CloseSettings);
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
            nextRoundButton.interactable = _actionsEnabled;
            discardButton.interactable = _actionsEnabled && SelectedCardIndex >= 0;
            foreach (var card in cards) card.Button.interactable = _actionsEnabled;
            foreach (var button in elementButtons) button.interactable = _actionsEnabled;
        }

        public void SelectCard(int index)
        {
            if (!_actionsEnabled || settingsPanel.activeSelf || index < 0 || index >= cards.Length
                || !cards[index].gameObject.activeSelf) return;
            SelectedCardIndex = SelectedCardIndex == index ? -1 : index;
            for (int i = 0; i < cards.Length; i++) cards[i].SetSelected(i == SelectedCardIndex);
            selectionLabel.text = SelectedCardIndex < 0 ? "选择一张手牌" :
                $"已选择 · {ElementDefs.Name(cards[SelectedCardIndex].Element)}";
            RefreshActions();
            cardSelected.Invoke(SelectedCardIndex);
        }

        public void ClearSelection()
        {
            bool wasSelected = SelectedCardIndex >= 0;
            SelectedCardIndex = -1;
            foreach (var card in cards) card.SetSelected(false);
            selectionLabel.text = "选择一张手牌";
            RefreshActions();
            if (wasSelected) cardSelected.Invoke(-1);
        }

        public void SelectElement(int index)
        {
            if (!_actionsEnabled || settingsPanel.activeSelf || index < 0 || index >= elementButtons.Length) return;
            SelectedElementIndex = index;
            for (int i = 0; i < elementSelections.Length; i++) elementSelections[i].alpha = i == index ? 1 : 0;
            elementSelected.Invoke(index);
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
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(closeSettingsButton.gameObject);
        }

        public void CloseSettings()
        {
            if (!settingsPanel.activeSelf) return;
            settingsBackdrop.gameObject.SetActive(false);
            settingsPanel.SetActive(false);
            controls.interactable = controls.blocksRaycasts = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(
                _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : settingsButton.gameObject);
        }
    }
}
