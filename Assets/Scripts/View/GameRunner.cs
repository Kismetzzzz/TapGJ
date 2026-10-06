using System.Collections;
using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 编辑器里按 Play 就能跑的主控：程序化搭好相机 / 棋盘 / HUD，然后驱动回合流程。
    /// 场景里不需要预制体、素材或手工连线。
    ///
    /// 也可以把这个组件手动摆进场景（菜单 TapGJ ▸ 在场景里创建游戏主控）：
    ///   GameRunner  → 规则、随机种子、播放速度都在这个组件的 Inspector 上
    ///   └─ Board    → 可选的子物体（BoardView）；不摆也行，运行时会自动建
    ///   Main Camera → 可选；不摆也行，运行时会自动建
    /// HUD 是 IMGUI，挂在 GameRunner 上画，不需要场景对象。
    /// 场景里没有 GameRunner 时运行时会自动创建一个，所以两种用法都成立。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameRunner : MonoBehaviour
    {
        public static GameRunner Instance { get; private set; }

        public GameEngine Engine { get; private set; }
        public BoardView Board { get; private set; }
        public HandView Hand { get; private set; }
        public Hud Hud { get; private set; }
        public Camera Cam { get; private set; }

        [Header("规则（改这里就能调平衡，不用改代码）")]
        public Rules Rules = new Rules();

        [Header("随机种子（同一个种子必然复现同一局）")]
        public int Seed = 20240607;

        [Header("准备阶段播放速度")]
        public float StepDuration = 0.09f;
        public float StepPause = 0.02f;

        /// <summary>正在播放动画 / 过场，此时不接受棋盘点击。</summary>
        public bool Busy => _animations > 0;
        public bool PointerOverHud { get; private set; }

        public event System.Action<GameEvent> EventRaised;

        Coroutine _turnLoop;
        int _animations;
        readonly List<GameEvent> _log = new List<GameEvent>();
        readonly List<string> _noteQueue = new List<string>();
        string _banner;
        float _bannerUntil;

        public IReadOnlyList<GameEvent> Log => _log;
        public string Banner => Time.time < _bannerUntil ? _banner : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            // 场景里已经手动摆好 GameRunner（或被自启动创建过）就不再插手，
            // 这样「自己在场景里搭」和「空场景直接 Play」两种用法都成立。
            if (FindFirstObjectByType<GameRunner>() != null) return;

            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            new GameObject("GameRunner").AddComponent<GameRunner>();
        }

        void Awake()
        {
            Instance = this;

            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            Cam.orthographic = true;
            Cam.orthographicSize = 6f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            Cam.transform.position = new Vector3(0f, 0f, -10f);

            // Board 可以是场景里已经摆好的子物体（自己加了 BoardView），也可以是运行时新建的
            Board = CreateBoardIfMissing();
            Board.CellClicked += OnCellClicked;

            // 手牌区：屏幕下方一块独立区域
            var handGo = new GameObject("Hand");
            handGo.transform.SetParent(transform, false);
            Hand = handGo.AddComponent<HandView>();
            Hand.CardClicked += OnHandCardClicked;

            // HUD 同理
            Hud = GetComponent<Hud>();
            if (Hud == null) Hud = gameObject.AddComponent<Hud>();
            Hud.Setup(this);

            Restart();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 找一个已经摆在场景里的 BoardView（优先子物体），没有就新建一个。
        /// 这样「Board 是场景里的实体 GameObject」这件事对使用者是可见、可改的。
        /// </summary>
        BoardView CreateBoardIfMissing()
        {
            var existing = GetComponentInChildren<BoardView>(true);
            if (existing != null)
            {
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                return existing;
            }

            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(transform, false);
            return boardGo.AddComponent<BoardView>();
        }

        // ------------------------------------------------------------ 开局 / 重开

        public void Restart() => Restart(Seed);

        public void Restart(int seed)
        {
            Seed = seed;
            if (_turnLoop != null) StopCoroutine(_turnLoop);
            _turnLoop = null;
            _animations = 0;

            Engine = new GameEngine(Rules, seed);
            Board.Setup(Engine);
            Board.InteractionEnabled = false;
            Hand.Setup(Engine);

            _log.Clear();
            _banner = null;
            _noteQueue.Clear();
            Hud.SelectedCardId = -1;

            Engine.Start();
            Board.SnapAll();
            Hand.Sync();
            LayoutViews();
            Note($"开局 seed={seed} ｜ {Rules.BoardWidth}x{Rules.BoardHeight} ｜ 每种元素 {Rules.CreepsPerElement} 个 ｜ "
                 + $"回合上限 {Rules.RoundLimit} ｜ 胜利条件：{Engine.WinConditionDescription()}");
            Note("操作：出牌阶段点下方手牌 → 再点棋盘目标格；弃牌阶段点手牌即为弃掉；每步都有按钮可推进。");

            _turnLoop = StartCoroutine(TurnLoop());
        }

        public void RestartWithNewSeed()
        {
            Restart(Random.Range(1, int.MaxValue));
        }

        // ------------------------------------------------------------ 屏幕分区

        /// <summary>
        /// 屏幕分区（比例，左上角为原点），对应设计稿：
        ///   上方中央 = 棋盘网格，下方 = 手牌区，右侧 = HUD 面板。
        /// </summary>
        public static class Zones
        {
            public static readonly Rect Board = new Rect(0.04f, 0.01f, 0.62f, 0.70f);
            public static readonly Rect Hand = new Rect(0.04f, 0.73f, 0.62f, 0.24f);
        }

        void LateUpdate()
        {
            LayoutViews();
        }

        /// <summary>
        /// 摆放三个区域。
        /// 关键：棋盘尺寸只由相机的 orthographicSize 决定（LayoutPixels 传 fitToZone:false 只做定位），
        /// 否则相机缩一次、棋盘再缩一次，格子会被放大到糊满屏幕。
        /// </summary>
        void LayoutViews()
        {
            if (Cam == null || Board == null || Hand == null || Hud == null) return;

            float aspect = Cam.aspect <= 0.01f ? 16f / 9f : Cam.aspect;

            // 右侧留给 HUD
            float panelFraction = Mathf.Clamp((Hud.EffectivePanelWidth + 12f) / Mathf.Max(1f, Screen.width), 0.15f, 0.6f);
            var boardZone = ClipRight(Zones.Board, 1f - panelFraction);
            var handZone = ClipRight(Zones.Hand, 1f - panelFraction);

            // 相机可视范围：半高 = orthographicSize，半宽 = orthographicSize × aspect。
            // 棋盘世界高度要占满 boardZone.height 这么多屏幕高度：
            //   2 × orthoSize × boardZone.height ≥ 棋盘世界高度
            float boardWorldHeight = Board.BoardHeightWorld;
            float boardWorldWidth = Board.BoardWidthWorld;

            float sizeFromHeight = boardWorldHeight * 0.5f / Mathf.Max(0.05f, boardZone.height);
            float sizeFromWidth = boardWorldWidth * 0.5f / Mathf.Max(0.05f, boardZone.width * aspect);
            Cam.orthographicSize = Mathf.Max(3f, sizeFromHeight, sizeFromWidth) * 1.04f;

            // 棋盘：只定位，不再缩放
            var boardCenterPx = new Vector3(
                (boardZone.x + boardZone.width * 0.5f) * Screen.width,
                (1f - (boardZone.y + boardZone.height * 0.5f)) * Screen.height, 0f);
            var boardWorld = ScreenToWorld(boardCenterPx);
            Board.transform.position = new Vector3(boardWorld.x, boardWorld.y, 0f);
            Board.ApplyLayoutScale(1f);

            // 手牌区：同一个相机坐标下按像素框摆放
            Hand.LayoutPixels(
                handZone.x * Screen.width,
                handZone.y * Screen.height,
                handZone.width * Screen.width,
                handZone.height * Screen.height,
                Cam);
        }

        /// <summary>把某个分区裁到「右侧留给 HUD」的宽度以内。</summary>
        static Rect ClipRight(Rect zone, float maxRight)
        {
            if (zone.xMax <= maxRight) return zone;
            float w = Mathf.Max(0.1f, maxRight - zone.x);
            return new Rect(zone.x, zone.y, w, zone.height);
        }

        Vector3 ScreenToWorld(Vector3 screenPoint)
        {
            screenPoint.z = -Cam.transform.position.z;
            return Cam.ScreenToWorldPoint(screenPoint);
        }

        // ------------------------------------------------------------ 每帧

        void Update()
        {
            bool playing = Engine != null && !Busy;
            Board.InteractionEnabled = playing && Engine.Phase == GamePhase.PlayerAction;
            Board.RefreshHighlights(Engine != null && Engine.Phase == GamePhase.PlayerAction
                ? Engine.FindCard(Hud.SelectedCardId)
                : null);
            Hand.RefreshSelection(Hud.SelectedCardId);
            PointerOverHud = Hud.PanelPixels.Contains(new Vector2(Input.mousePosition.x, Input.mousePosition.y));

            if (playing) HandleClick();
        }

        /// <summary>
        /// 统一处理点击：手牌优先（它在棋盘下方，不会重叠，但先判更稳），
        /// 然后是棋盘格子。HUD 面板上的按钮走 IMGUI，不经过这里。
        /// </summary>
        void HandleClick()
        {
            if (!Input.GetMouseButtonDown(0)) return;
            if (PointerOverHud) return;

            var world = ScreenToWorld(Input.mousePosition);
            var hit = Physics2D.OverlapPoint(new Vector2(world.x, world.y));
            if (hit == null) return;

            var card = hit.GetComponentInParent<CardView>();
            if (card != null)
            {
                card.Click();
                return;
            }

            var cell = hit.GetComponentInParent<CellView>();
            if (cell != null && Board.InteractionEnabled)
                Board.NotifyCellClicked(cell.Pos);
        }

        // ------------------------------------------------------------ 回合流程

        IEnumerator TurnLoop()
        {
            yield return null; // 先让布局算一帧

            while (!Engine.IsOver)
            {
                // ---------- 准备阶段：一只一只小怪推进并播放 ----------
                // 最后一只走完时，引擎会顺手把抽牌阶段结算掉，并把补到的牌排成一个步骤，
                // 所以下面这段会把「小怪走完 + 抽到的牌」一起播出来。
                while (Engine.Phase == GamePhase.Preparation)
                {
                    var step = Engine.AdvancePreparation();
                    if (step == null) continue; // 本回合小怪都走完了，Phase 已切到玩家行动
                    yield return PlayStepRoutine(step);
                    FlushLogToHud(); // 每走一只就刷新日志，方便盯规则
                    if (StepPause > 0f) yield return new WaitForSeconds(StepPause);
                }

                // 把抽牌阶段排下的补牌步骤播完（如果还有）
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());

                FlushLogToHud();
                Board.Sync();

                // ---------- 玩家行动阶段：等玩家出牌 / 结束出牌 ----------
                while (Engine.Phase == GamePhase.PlayerAction)
                    yield return null;

                // ---------- 弃牌阶段：等玩家弃牌 / 结束弃牌 ----------
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());
                FlushLogToHud();

                while (Engine.Phase == GamePhase.Discard)
                    yield return null;

                // ---------- 判定阶段 ----------
                yield return new WaitForSeconds(0.12f);
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());

                Engine.ResolveJudgment();
                FlushLogToHud();
                Board.Sync();

                if (Engine.IsOver) break;
                yield return new WaitForSeconds(0.25f);
            }

            if (Engine.TryTakeResult(out var phase, out var message))
                ShowBanner(phase == GamePhase.Victory ? $"胜  利\n{message}" : $"失  败\n{message}");

            _turnLoop = null;
        }

        IEnumerator PlayStepRoutine(StepRecord step)
        {
            _animations++;
            Board.InteractionEnabled = false;
            yield return Board.PlayStep(step, StepDuration);
            Board.Sync();
            Hand.Sync(); // 抽牌/弃牌/出牌都会改手牌
            _animations--;
        }

        void FlushLogToHud()
        {
            var engineLog = Engine.Log;
            while (_log.Count < engineLog.Count)
            {
                var e = engineLog[_log.Count];
                _log.Add(e);
                Note(FormatEvent(e));
                EventRaised?.Invoke(e);
            }
        }

        static string FormatEvent(GameEvent e)
        {
            switch (e.Type)
            {
                case GameEventType.Message: return e.Text;
                case GameEventType.Move: return $"    {ElementDefs.Name(e.Element)}{e.To} 移动";
                case GameEventType.Bump: return $"    {ElementDefs.Name(e.Element)}{e.From} 同元素相遇 → 禁止移动";
                case GameEventType.Vanish: return $"    {e.Text}";
                case GameEventType.Spawn: return $"    {e.To} {e.Text}";
                case GameEventType.SpawnBlocked: return $"    {e.From} {e.Text}";
                case GameEventType.GainCard: return $"  ★ {e.Text}";
                case GameEventType.Win: return $"  {e.Text}";
                case GameEventType.Lose: return $"  {e.Text}";
                default: return e.Text;
            }
        }

        void ShowBanner(string text)
        {
            _banner = text;
            _bannerUntil = Time.time + 8f;
        }

        // ------------------------------------------------------------ 玩家操作

        void OnCellClicked(Pos pos)
        {
            if (Engine.Phase != GamePhase.PlayerAction) return;

            var card = Engine.FindCard(Hud.SelectedCardId);
            if (card == null)
            {
                Note($"先在下方手牌区（或右侧列表）选一张牌，再点目标格子（刚才点了 {pos}）");
                return;
            }

            var record = Engine.TryPlayCard(card.Id, pos);
            if (record == null)
            {
                Note($"✗ {Engine.FailReason}");
                return;
            }

            Hud.SelectedCardId = -1;
            StartCoroutine(PlayCardRoutine(record));
        }

        /// <summary>点下方手牌区的一张牌。出牌阶段＝选中，弃牌阶段＝直接弃掉。</summary>
        void OnHandCardClicked(int cardId)
        {
            var card = Engine.FindCard(cardId);
            if (card == null) return;

            switch (Engine.Phase)
            {
                case GamePhase.PlayerAction:
                    bool same = Hud.SelectedCardId == cardId;
                    Hud.SelectedCardId = same ? -1 : cardId;
                    Note(same ? "取消选中" : $"选中 {card.Label} → 再点棋盘上绿色高亮的目标格");
                    break;

                case GamePhase.Discard:
                    TryDiscard(cardId);
                    break;

                default:
                    Note($"当前是{GameEngine.PhaseName(Engine.Phase)}，还不能用手牌");
                    break;
            }
        }

        IEnumerator PlayCardRoutine(StepRecord record)
        {
            yield return PlayStepRoutine(record);
            FlushLogToHud();
            Board.Sync();
            Hand.Sync();
        }

        /// <summary>结束出牌阶段 → 进入弃牌阶段。</summary>
        public void EndPlayerRound()
        {
            if (Engine.Phase != GamePhase.PlayerAction) return;
            Engine.EndPlayerPhase();
            FlushLogToHud();
        }

        /// <summary>结束弃牌阶段 → 进入判定阶段。</summary>
        public void EndDiscardRound()
        {
            if (Engine.Phase != GamePhase.Discard) return;
            Engine.EndDiscardPhase();
            FlushLogToHud();
        }

        /// <summary>弃牌阶段点手牌即为弃掉。返回是否成功。</summary>
        public bool TryDiscard(int cardId)
        {
            if (Engine.Phase != GamePhase.Discard) return false;

            if (!Engine.TryDiscardCard(cardId))
            {
                Note($"✗ {Engine.FailReason}");
                return false;
            }

            Hud.SelectedCardId = -1;
            FlushLogToHud();
            Board.Sync();
            Hand.Sync();
            return true;
        }

        public void Note(string text)
        {
            _noteQueue.Add($"[R{Engine?.Round ?? 0}] {text}");
        }

        /// <summary>HUD 用：取走待显示的提示（每帧调用一次）。</summary>
        public void DrainNotes(List<string> into)
        {
            for (int i = 0; i < _noteQueue.Count; i++)
            {
                into.Add(_noteQueue[i]);
                if (into.Count > 400) into.RemoveAt(0);
            }
            _noteQueue.Clear();
        }
    }
}
