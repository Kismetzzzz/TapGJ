using System.Collections.Generic;
using TapGJ.Core;
using TapGJ.Flow;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>现有局内界面与关卡对局的连接；规则和动画仍由对局控制器驱动。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleGameplayHud : MonoBehaviour, IGameHud
    {
        [SerializeField] GameSession session;
        [SerializeField] BattleUiController ui;
        [SerializeField] BattleBoardViewport viewport;
        [SerializeField] GameObject resultPanel;
        [SerializeField] Text resultHeading;
        [SerializeField] Text resultMessage;
        [SerializeField] Button restartButton;
        [SerializeField] Button menuButton;
        [SerializeField] Button resultRestartButton;
        [SerializeField] Button resultMenuButton;
        [SerializeField] Button nextLevelButton;
        readonly List<string> _notes = new List<string>();
        GameSession _boundSession;
        ElementAbacusView _abacus;
        float _elapsed, _savedTimeScale = 1, _noteUntil;
        bool _ownsPause;
        string _lastNote;

        public GameSession Session => session;
        public BattleUiController Ui => ui;
        public BattleBoardViewport Viewport => viewport;

        void OnEnable()
        {
            ui.CardSelected.AddListener(SelectCard);
            ui.ElementSelected.AddListener(SelectElement);
            ui.NextRoundRequested.AddListener(Advance);
            ui.DiscardRequested.AddListener(Discard);
            ui.SettingsChanged += SetPaused;
            restartButton.onClick.AddListener(Restart);
            menuButton.onClick.AddListener(ReturnToMenu);
            resultRestartButton.onClick.AddListener(Restart);
            resultMenuButton.onClick.AddListener(ReturnToMenu);
            nextLevelButton.onClick.AddListener(NextLevel);
            if (session != null) session.RegisterHud(this);
        }

        public void Setup(GameSession value)
        {
            if (_boundSession == value) return;
            UnbindSession();
            session = _boundSession = value;
            if (value == null) return;
            value.LevelStarted += OnLevelStarted;
            value.LevelFinished += OnLevelFinished;
            value.CardConsumed += OnCardConsumed;
            if (value.Engine != null) OnLevelStarted(value.Engine);
        }

        void OnDisable()
        {
            ui.CardSelected.RemoveListener(SelectCard);
            ui.ElementSelected.RemoveListener(SelectElement);
            ui.NextRoundRequested.RemoveListener(Advance);
            ui.DiscardRequested.RemoveListener(Discard);
            ui.SettingsChanged -= SetPaused;
            restartButton.onClick.RemoveListener(Restart);
            menuButton.onClick.RemoveListener(ReturnToMenu);
            resultRestartButton.onClick.RemoveListener(Restart);
            resultMenuButton.onClick.RemoveListener(ReturnToMenu);
            nextLevelButton.onClick.RemoveListener(NextLevel);
            SetPaused(false);
            UnbindSession();
        }

        void UnbindSession()
        {
            if (_boundSession != null)
            {
                _boundSession.LevelStarted -= OnLevelStarted;
                _boundSession.LevelFinished -= OnLevelFinished;
                _boundSession.CardConsumed -= OnCardConsumed;
            }
            _boundSession = null;
        }

        void OnLevelStarted(GameEngine engine)
        {
            _elapsed = 0;
            _lastNote = null;
            _notes.Clear();
            ui.CloseSettings();
            SetPaused(false);
            resultPanel.SetActive(false);
            ui.SetModalBlocked(false);
            ui.ClearSelection();
            // 重开沿用相同 ID 时也要重新绑定手牌，避免上局的视觉状态残留。
            ui.ResetHand();
            _abacus = ui.GetComponentInChildren<ElementAbacusView>(true);
            if (_abacus != null) _abacus.Bind(engine);
            viewport.Setup(session);
            Refresh();
        }

        void OnLevelFinished(bool victory, string message)
        {
            resultHeading.text = victory ? "通关成功" : "本局结束";

            // 通关后如果有剧情/过渡文本（LevelDefinition.outroText，由 LevelLoader 写进 LevelOutro），
            // 接在胜负说明后面一起显示 —— 这就是「过渡关卡」把玩家引到下一章的地方。
            string outro = victory && TapGJ.Flow.LevelOutro.HasText ? TapGJ.Flow.LevelOutro.Current : null;
            resultMessage.text = string.IsNullOrEmpty(outro) ? message : message + "\n\n" + outro;

            nextLevelButton.gameObject.SetActive(victory && session.HasNextLevel);
            resultPanel.SetActive(true);
            ui.SetModalBlocked(true);
            ui.SetActionsEnabled(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resultRestartButton.gameObject);
        }

        float OnCardConsumed() => ui.SetHand(VisibleHand());

        IReadOnlyList<Card> VisibleHand()
        {
            if (_abacus == null) return session.Engine.Hand;
            _abacus.Sync(session.Engine);
            return _abacus.HandForDisplay(session.Engine.Hand);
        }

        void LateUpdate()
        {
            if (session == null || session.Engine == null) return;
            if (!session.Engine.IsOver && !session.Paused) _elapsed += Time.unscaledDeltaTime;
            _notes.Clear();
            session.DrainNotes(_notes);
            if (_notes.Count > 0)
            {
                _lastNote = _notes[_notes.Count - 1];
                _noteUntil = Time.unscaledTime + 3;
            }
            Refresh();
        }

        void Refresh()
        {
            var engine = session.Engine;
            ui.SetHand(VisibleHand());
            ui.SetSelectedCardId(session.SelectedCardId);
            ui.SetGoalProgress(engine.GetGoalProgress());
            ui.SetRound(engine.Round, engine.Rules.RoundLimit);
            ui.SetElapsedSeconds(_elapsed);
            bool actionable = !session.Busy && !session.Paused && !engine.IsOver;
            bool player = engine.Phase == GamePhase.PlayerAction;
            bool discard = engine.Phase == GamePhase.Discard;
            ui.SetGameplayActions(actionable && (player || discard), actionable && (player || discard),
                actionable && discard && engine.DiscardsRemaining > 0,
                player ? "结束出牌" : discard ? "结束弃牌" : "正在推进…");
            string hint = engine.IsOver ? "本局已结束" : session.Paused ? "已暂停" :
                discard ? $"弃牌阶段 · 剩余 {engine.DiscardsRemaining} 次 · 选牌后点击弃牌" :
                !player ? GameEngine.PhaseName(engine.Phase) : session.SelectedCard == null ? "选择手牌，再点击棋盘目标格" :
                session.SelectingFusionCard ? session.FusionStage == GameSession.FusionPick.First
                    ? "融合牌：先选择第一个元素" : "融合牌：选择高亮的第二个元素" :
                session.SelectingDecomposeCard ? "分解牌：点击高亮的复合元素" : $"{session.SelectedCard.Label}：点击高亮格子出牌";
            // 提示行的两条插队规则：出牌失败要立刻说（"✗"），喂瓶/满瓶也要立刻说（"瓶"）——
            // 第三章整章都在盯瓶子，而这一行是 uGUI 局内界面唯一能实时写字的地方。
            if (_lastNote != null && Time.unscaledTime < _noteUntil
                && (_lastNote.Contains("✗") || _lastNote.Contains("瓶")))
                hint = _lastNote;
            ui.SetSelectionHint(hint);
        }

        void SelectCard(int slot)
        {
            if (slot < 0) session.CancelSelection();
            else if (slot < ui.Cards.Count) session.SelectHandCard(ui.Cards[slot].CardId);
            Refresh();
        }

        void SelectElement(int index)
        {
            foreach (var card in ui.Cards)
                if (card.gameObject.activeSelf && card.Kind == CardKind.ElementCard && (int)card.Element == index)
                {
                    if (session.SelectedCardId != card.CardId) session.SelectHandCard(card.CardId);
                    Refresh();
                    return;
                }
            Refresh();
        }

        void Advance()
        {
            if (session.Engine.Phase == GamePhase.PlayerAction) session.EndPlayerRound();
            else if (session.Engine.Phase == GamePhase.Discard) session.EndDiscardRound();
            Refresh();
        }

        void Discard(int slot)
        {
            if (slot >= 0 && slot < ui.Cards.Count) session.TryDiscard(ui.Cards[slot].CardId);
            Refresh();
        }

        void SetPaused(bool paused)
        {
            if (session != null) session.Paused = paused;
            if (paused && !_ownsPause)
            {
                _savedTimeScale = Time.timeScale;
                Time.timeScale = 0;
                _ownsPause = true;
            }
            else if (!paused && _ownsPause)
            {
                Time.timeScale = _savedTimeScale;
                _ownsPause = false;
            }
        }

        public void Restart()
        {
            ui.CloseSettings();
            SetPaused(false);
            session.Restart();
        }

        void NextLevel() => session.GoToNextLevel();

        void ReturnToMenu()
        {
            ui.CloseSettings();
            SetPaused(false);
            SceneFlow.LoadMenu();
        }
    }
}
