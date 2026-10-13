using System.Collections;
using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 一局游戏的主控：持有 GameEngine，驱动「准备 → 抽牌 → 玩家行动 → 弃牌 → 判定」的回合流程，
    /// 并把 StepRecord 交给表现层播动画。
    ///
    /// ⚠ 它**不**创建任何对象、**不**碰相机、**不**读屏幕尺寸 —— 那些都是场景作者的事。
    /// 必需引用在 Inspector 里拖好；漏了就报错并停用，不会静默兜底（以前那套自动创建正是「拖组件」的障碍）。
    ///
    /// 场景结构（详细接线清单见 README 第七节）：
    ///   Systems/GameSession  ← 这个组件（board / hand / input 三个引用）
    ///   Anchors/BoardAnchor  ← BoardView
    ///   Anchors/HandAnchor   ← HandView
    ///   Systems/ClickRouter  ← 输入分发
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour, ISceneWiringCheck
    {
        [Header("必需引用")]
        [SerializeField] BoardView board;
        [SerializeField] HandView hand;
        [SerializeField] ClickRouter input;

        [Header("开发兜底规则（场景里挂了 LevelLoader 时以关卡定义为准）")]
        [Tooltip("只有在场景里没有 LevelLoader（没有关卡）时才会用到这一份。正常进关时规则来自 LevelDefinition.rules，这里改了没用。")]
        public Rules Rules = new Rules();

        [Header("随机种子（同一个种子必然复现同一局）")]
        public int Seed = 20240607;

        [Header("播放速度")]
        public float StepDuration = 0.09f;
        public float StepPause = 0.02f;

        /// <summary>关掉之后所有步骤动画瞬时播完。</summary>
        public bool AnimationsEnabled = true;

        public GameEngine Engine { get; private set; }
        public BoardView Board => board;
        public HandView Hand => hand;
        public ClickRouter Input => input;

        /// <summary>
        /// 当前**实际生效**的规则。开局之后是引擎里那一份（由关卡定义生成），
        /// 还没开局时退回场景里的开发兜底。要读规则一律读它，不要读 <see cref="Rules"/>。
        /// </summary>
        public Rules ActiveRules => Engine != null ? Engine.Rules : Rules;

        /// <summary>规则是不是来自关卡定义（false = 场景兜底 / 还没开局）。</summary>
        public bool RulesFromLevel => Engine != null && _levelHost != null;

        /// <summary>当前关卡的来源；没有关卡（开发兜底）时为 null。</summary>
        public ILevelHost LevelHost => _levelHost;

        /// <summary>正在播动画 / 过场，此时不接受操作。</summary>
        public bool Busy => _animations > 0;

        /// <summary>本回合是否处于快进状态（判定阶段自动恢复）。</summary>
        public bool FastForwarding { get; private set; }

        public event System.Action<GameEvent> EventRaised;
        public event System.Action<int> SelectionChanged;
        public event System.Action<GamePhase> PhaseChanged;
        public event System.Action<GameEngine> LevelStarted;
        public event System.Action<bool, string> LevelFinished;

        int _selectedCardId = -1;

        /// <summary>融合牌的两段式选点：先点元素1，再点元素2。</summary>
        public enum FusionPick
        {
            /// <summary>还没点元素1。</summary>
            First = 0,
            /// <summary>已经点了元素1，正在等元素2。</summary>
            Second = 1,
        }

        /// <summary>
        /// 当前选中的**融合牌**处于哪一段（选了元素牌 / 分解牌 / 什么都没选时读它没有意义）。
        /// 表现层与 HUD 靠它显示「请点元素1」还是「请点相邻的元素2」。
        /// </summary>
        public FusionPick FusionStage { get; private set; } = FusionPick.First;

        /// <summary>已经点下的「元素1」的位置（<see cref="FusionStage"/> = First 时是 Pos.None）。</summary>
        public Pos FusionFirstCell { get; private set; } = Pos.None;

        /// <summary>当前选中的牌。</summary>
        public Card SelectedCard => Engine != null ? Engine.FindCard(_selectedCardId) : null;

        /// <summary>当前选中的是不是融合牌。</summary>
        public bool SelectingFusionCard => SelectedCard != null && SelectedCard.IsFusionCard;

        /// <summary>当前选中的是不是分解牌。</summary>
        public bool SelectingDecomposeCard => SelectedCard != null && SelectedCard.IsDecomposeCard;

        /// <summary>当前选中的手牌 id（-1 = 没选）。手牌区与 HUD 都通过它同步选中态。</summary>
        public int SelectedCardId
        {
            get => _selectedCardId;
            set
            {
                if (_selectedCardId == value) return;
                _selectedCardId = value;
                // 换牌 / 取消选中都要把融合的两段式选点重置，否则会带着上一张牌的元素1 继续点。
                FusionStage = FusionPick.First;
                FusionFirstCell = Pos.None;
                SelectionChanged?.Invoke(value);
            }
        }

        Coroutine _turnLoop;
        int _animations;
        bool _begun;
        ILevelHost _levelHost;
        GamePhase _lastPhase = GamePhase.NotStarted;
        readonly List<GameEvent> _log = new List<GameEvent>();
        readonly List<string> _noteQueue = new List<string>();
        string _banner;
        float _bannerUntil;

        public IReadOnlyList<GameEvent> Log => _log;
        public string Banner => Time.time < _bannerUntil ? _banner : null;

        // ---------------------------------------------------------------- 生命周期

        void Awake()
        {
            if (!CheckRequiredRefs()) return;

            input.CardClicked += OnCardClicked;
            input.CellClicked += OnCellClicked;

            // HUD 通过 IGameHud 接入：这里**不认识任何具体 HUD 类型**，所以临时 IMGUI 的 Hud
            // 和以后的 uGUI GameHud 可以并存，删掉任意一个都不需要改这个文件。
            // （IMGUI 不参与 EventSystem，它需要的「面板区域挡点击」由它自己在 Setup 里注册。）
            foreach (var hud in GetComponents<IGameHud>())
                hud.Setup(this);
        }

        /// <summary>
        /// 让**不在** GameSession 物体上的 HUD 也能挂进来（在它自己的 Awake 里调）。
        /// 重复调用、和「同物体自动扫描」撞车都是安全的 —— 接口约定 Setup 必须幂等。
        /// </summary>
        public void RegisterHud(IGameHud hud)
        {
            if (hud != null) hud.Setup(this);
        }

        void Start()
        {
            // 关卡加载器（P2）会在自己的 Awake 里调 Begin()，那时这里就不再自己开局。
            // Unity 保证所有 Awake 先于任何 Start，所以这个先后顺序是确定的。
            if (!_begun)
            {
                Debug.LogWarning("[TapGJ] 场景里没有 LevelLoader，正在用 GameSession 上的开发兜底规则随机铺盘。"
                                 + "要按关卡开局：跑菜单 TapGJ ▸ 在当前场景搭建/补齐骨架，再建一关。");
                var engine = new GameEngine(Rules.Clone(), Seed);
                engine.Start();
                Begin(engine, Seed);
            }
        }

        void OnDestroy()
        {
            if (input == null) return;
            input.CardClicked -= OnCardClicked;
            input.CellClicked -= OnCellClicked;
        }

        /// <summary>
        /// 接管一个「已经开始」的引擎并开始驱动回合（唯一入口）。
        /// 注意：本方法**不会**再调 Engine.Start() —— 开局由调用方负责
        /// （关卡加载器用 engine.Start(setup) 布置关卡，开发路径在 Start/Restart 里先 Start）。
        /// </summary>
        public void Begin(GameEngine engine, int seed)
        {
            if (engine == null || board == null || hand == null || input == null) return;

            _begun = true;
            Engine = engine;
            Seed = seed;

            if (_turnLoop != null) StopCoroutine(_turnLoop);
            _turnLoop = null;
            _animations = 0;
            FastForwarding = false;

            board.Setup(Engine);
            board.InteractionEnabled = false;
            hand.Setup(Engine);

            _log.Clear();
            _banner = null;
            _noteQueue.Clear();
            SelectedCardId = -1;
            _lastPhase = GamePhase.NotStarted;

            board.SnapAll();
            hand.Sync(!InstantPlayback);

            LevelStarted?.Invoke(Engine);
            Note($"开局 seed={seed} ｜ {Engine.Rules.BoardWidth}x{Engine.Rules.BoardHeight} ｜ 每种元素 {Engine.Rules.CreepsPerElement} 个 ｜ "
                 + $"回合上限 {Engine.Rules.RoundLimit} ｜ 胜利条件：{Engine.WinConditionDescription()}");
            Note("操作：出牌阶段点下方手牌 → 再点棋盘目标格；弃牌阶段点手牌即为弃掉；每步都有按钮可推进。");
            if (Engine.CanSummonOnEmptyCell)
                Note("元素牌有两个用途：点绿色高亮的**元素格**发生反应，点绿色高亮的**空格**在该格召唤 1 个该元素的小怪"
                     + "（两种都消耗这张牌、都占本回合出牌次数）。");

            _turnLoop = StartCoroutine(TurnLoop());
        }

        /// <summary>
        /// 关卡加载器在开局前把自己登记进来（见 ILevelHost）。
        /// 登记之后 Restart 就会走「重建整关」，而不是自己 new 一个引擎 ——
        /// 否则重开会丢掉关卡布置和关卡预制体上的机制。
        /// </summary>
        public void SetLevelHost(ILevelHost host) => _levelHost = host;

        public void Restart() => Restart(Seed);

        public void Restart(int seed)
        {
            if (_levelHost != null)
            {
                _levelHost.RebuildLevel(seed);
                return;
            }

            // 开发兜底路径：没有关卡加载器，只能用场景里这份规则随机铺盘。
            var engine = new GameEngine(Rules.Clone(), seed);
            engine.Start();
            Begin(engine, seed);
        }

        public void RestartWithNewSeed() => Restart(Random.Range(1, int.MaxValue));

        // ---------------------------------------------------------------- 下一关（HUD 的按钮）

        /// <summary>当前关卡的名字（HUD 顶栏显示「现在在玩哪一关」）。没有关卡加载器时返回 null。</summary>
        public string CurrentLevelName => _levelHost != null ? _levelHost.CurrentLevelName : null;

        /// <summary>有没有「下一关」可去。没有关卡加载器（开发兜底路径）时一律 false。</summary>
        public bool HasNextLevel => _levelHost != null && _levelHost.HasNextLevel;

        /// <summary>下一关的名字（写在按钮上，点之前就知道要去哪一关）。没有 → null。</summary>
        public string NextLevelName => _levelHost != null ? _levelHost.NextLevelName : null;

        /// <summary>
        /// 切到下一关。真正干活的是关卡加载器（换关卡定义 → 重建关卡预制体与机制 → 开新种子），
        /// 这里只负责把请求转过去、再留一条日志。
        /// ⚠ 日志必须在换关**之后**写：Begin() 会清空待显示队列，之前记的会被丢掉。
        /// </summary>
        public bool GoToNextLevel()
        {
            if (_levelHost == null || !_levelHost.GoToNextLevel()) return false;

            Note($"切到下一关：{CurrentLevelName ?? "（未知关卡）"}");
            return true;
        }

        /// <summary>快进：从下一次步骤起瞬时播完，本回合进入判定阶段后自动恢复正常速度。</summary>
        public void FastForward() => FastForwarding = true;

        bool InstantPlayback => FastForwarding || !AnimationsEnabled;

        // ---------------------------------------------------------------- 每帧

        void Update()
        {
            if (Engine == null) return;

            bool playing = !Busy;
            bool canPlay = playing && Engine.Phase == GamePhase.PlayerAction;

            input.InteractionEnabled = playing;
            board.InteractionEnabled = canPlay;

            // 离开玩家行动阶段时把选中态清掉：否则「选了融合牌但没点元素1 就结束回合」之后，
            // 下一回合会带着一张选中的牌开局（手牌对象还在，但玩家会以为是自己点的）。
            if (!canPlay && (_selectedCardId != -1 || FusionStage != FusionPick.First || !FusionFirstCell.IsNone))
                CancelSelection();

            // 右键 / Esc = 「返回普通出牌状态」：取消选中的牌、清掉元素1 和高亮。
            // 走 Input 而不是 ClickRouter 的事件是因为点空白处也要能取消（不必命中任何碰撞体）。
            // ⚠ 必须写全 UnityEngine.Input：本类的字段 `input`（ClickRouter）会遮蔽 Input 这个类型名。
            if (canPlay)
            {
                bool rightClick = UnityEngine.Input.GetMouseButtonDown(1);
                bool escape = UnityEngine.Input.GetKeyDown(KeyCode.Escape);
                if (rightClick || escape) CancelSelection(rightClick ? "右键：已返回普通出牌状态" : "Esc：已返回普通出牌状态");
            }

            board.RefreshHighlights(HighlightCells(canPlay), FocusCell(canPlay));
            hand.RefreshSelection(SelectedCardId);

            if (Engine.Phase == _lastPhase) return;
            _lastPhase = Engine.Phase;
            PhaseChanged?.Invoke(_lastPhase);
        }

        // ---------------------------------------------------------------- 高亮与选点状态

        /// <summary>
        /// 现在该高亮哪些格子。这是「点击逻辑」的唯一真源：
        ///   · 没选牌 → 不高亮
        ///   · 选了融合牌 · 第一段 → 所有「能和邻居配成复合元素」的元素格
        ///   · 选了融合牌 · 第二段 → 和元素1 配得上的那些格子（点了就融合）
        ///   · 选了分解牌 → 场上所有复合元素
        ///   · 选了元素牌 → 能发生反应的元素格（图1 第 1 条）+ 能召唤的空格（图1 第 2 条）
        /// 元素牌那一批格子由引擎算（<see cref="GameEngine.CellsForElementCard"/>），
        /// 和秘术牌一样不在表现层自己拼。
        /// </summary>
        ICollection<Pos> HighlightCells(bool canPlay)
        {
            if (!canPlay || Engine == null) return EmptyCellSet;

            var card = SelectedCard;
            if (card == null) return EmptyCellSet;

            if (card.IsFusionCard)
            {
                if (!Engine.CanFuseNow) return EmptyCellSet;
                return FusionStage == FusionPick.First
                    ? (ICollection<Pos>)Engine.CellsForFusionFirst()
                    : Engine.CellsForFusionSecond(FusionFirstCell);
            }

            if (card.IsDecomposeCard) return Engine.CellsForDecompose();

            return Engine.CellsForElementCard(card);
        }

        /// <summary>额外加一层「已选中」暖色的格子：融合牌第二段时是元素1 的位置。</summary>
        Pos FocusCell(bool canPlay)
        {
            if (!canPlay || Engine == null) return Pos.None;
            if (!SelectingFusionCard) return Pos.None;
            return FusionStage == FusionPick.Second ? FusionFirstCell : Pos.None;
        }

        static readonly List<Pos> EmptyCellSet = new List<Pos>();

        /// <summary>右键 / Esc：取消选中的牌和融合选点，回到普通出牌状态。</summary>
        public void CancelSelection(string note = null)
        {
            bool hadSomething = _selectedCardId != -1 || FusionStage != FusionPick.First || !FusionFirstCell.IsNone;
            SelectedCardId = -1;
            board?.ClearHighlights();

            if (hadSomething && !string.IsNullOrEmpty(note)) Note(note);
        }

        // ---------------------------------------------------------------- 回合流程

        IEnumerator TurnLoop()
        {
            yield return null; // 先让表现层把第一帧摆好

            while (!Engine.IsOver)
            {
                // ---------- 准备阶段：一只一只小怪推进并播放 ----------
                while (Engine.Phase == GamePhase.Preparation)
                {
                    var step = Engine.AdvancePreparation();
                    if (step == null) continue; // 本回合小怪都走完了，Phase 已切到玩家行动
                    yield return PlayStepRoutine(step);
                    FlushLog(); // 每走一只就刷新日志，方便盯规则
                    if (StepPause > 0f && !InstantPlayback) yield return new WaitForSeconds(StepPause);
                }

                // 抽牌阶段排下的补牌步骤
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());

                FlushLog();
                board.Sync();

                // ---------- 玩家行动阶段：等玩家出牌 / 结束出牌 ----------
                while (Engine.Phase == GamePhase.PlayerAction)
                    yield return null;

                // ---------- 弃牌阶段：等玩家弃牌 / 结束弃牌 ----------
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());
                FlushLog();

                while (Engine.Phase == GamePhase.Discard)
                    yield return null;

                // ---------- 判定阶段 ----------
                if (!InstantPlayback) yield return new WaitForSeconds(0.12f);
                while (Engine.PendingStepCount > 0)
                    yield return PlayStepRoutine(Engine.DequeueStep());

                Engine.ResolveJudgment();
                FlushLog();
                board.Sync();
                FastForwarding = false; // 快进只作用于本回合

                if (Engine.IsOver) break;
                if (!InstantPlayback) yield return new WaitForSeconds(0.25f);
            }

            _turnLoop = null;

            if (Engine.TryTakeResult(out var phase, out var message))
            {
                ShowBanner(phase == GamePhase.Victory ? $"胜  利\n{message}" : $"失  败\n{message}");
                LevelFinished?.Invoke(phase == GamePhase.Victory, message);
            }
        }

        IEnumerator PlayStepRoutine(StepRecord step)
        {
            _animations++;
            board.InteractionEnabled = false;
            yield return board.PlayStep(step, InstantPlayback ? 0f : StepDuration);
            board.Sync();
            hand.Sync(!InstantPlayback);
            _animations--;
        }

        // ---------------------------------------------------------------- 玩家操作

        void OnCellClicked(Pos pos)
        {
            if (Engine == null || Engine.Phase != GamePhase.PlayerAction) return;

            var card = Engine.FindCard(SelectedCardId);
            if (card == null)
            {
                Note($"先在下方手牌区选一张牌，再点目标格子（刚才点了 {pos}）");
                return;
            }

            // 融合牌：两段式选点（元素1 → 元素2），不走普通出牌路径。
            if (card.IsFusionCard)
            {
                HandleFusionClick(card, pos);
                return;
            }

            // 分解牌：一步选点，作用于场上的复合元素。
            if (card.IsDecomposeCard)
            {
                var decomposeRecord = Engine.TryPlayDecomposeCard(card.Id, pos);
                if (decomposeRecord == null)
                {
                    Note($"✗ {Engine.FailReason}");
                    return;
                }

                SelectedCardId = -1;
                StartCoroutine(PlayCardRoutine(decomposeRecord));
                return;
            }

            var record = Engine.TryPlayCard(card.Id, pos);
            if (record == null)
            {
                Note($"✗ {Engine.FailReason}");
                return;
            }

            SelectedCardId = -1;
            StartCoroutine(PlayCardRoutine(record));
        }

        /// <summary>
        /// 融合牌的点击：第一下点「元素1」，第二下点「元素2」。
        /// 可以融合 → 在元素1 的位置生成复合元素；不行 → 只报原因，**不消耗手牌**、选点回退到第一段。
        /// 任何时候右键都能回到普通出牌状态。
        /// </summary>
        void HandleFusionClick(Card card, Pos pos)
        {
            if (!Engine.CanFuseNow)
            {
                Note($"✗ 场上已经有复合元素，融合牌只在场上没有复合元素时才能用");
                return;
            }

            if (FusionStage == FusionPick.First)
            {
                var first = Engine.Board.At(pos);
                if (first == null)
                {
                    Note($"✗ {pos} 上没有元素，融合的第一步要先点一个元素（点右键取消）");
                    return;
                }

                var seconds = Engine.CellsForFusionSecond(pos);
                if (seconds.Count == 0)
                {
                    var partners = FusionRecipes.PartnersOf(first.Element);
                    var names = new List<string>();
                    foreach (var p in partners) names.Add(ElementDefs.Name(p.partner));

                    Note(names.Count == 0
                        ? $"✗ {ElementDefs.Name(first.Element)} 配不成任何复合元素（配方：{GameEngine.RecipeListText()}）"
                        : $"✗ {ElementDefs.Name(first.Element)} 旁边没有能配对的元素（可以配 {string.Join("/", names)}），"
                          + "换一个元素1，或点右键取消");
                    return;
                }

                FusionStage = FusionPick.Second;
                FusionFirstCell = pos;
                Note($"元素1 = {ElementDefs.Name(first.Element)}{pos} → 现在点绿色高亮的元素2"
                     + $"（要相邻、且能配成复合元素；点右键可以退回）");
                return;
            }

            // 第二段：把元素1 和这次点的元素合成复合元素
            var record = Engine.TryPlayFusionCard(card.Id, FusionFirstCell, pos);
            if (record == null)
            {
                Note($"✗ {Engine.FailReason}（可以重新点元素2，或点右键取消）");
                return;
            }

            SelectedCardId = -1;
            StartCoroutine(PlayCardRoutine(record));
        }

        void OnCardClicked(int cardId)
        {
            var card = Engine.FindCard(cardId);
            if (card == null) return;

            switch (Engine.Phase)
            {
                case GamePhase.PlayerAction:
                    bool same = SelectedCardId == cardId;
                    SelectedCardId = same ? -1 : cardId;
                    if (same)
                    {
                        Note("取消选中");
                    }
                    else if (card.IsFusionCard)
                    {
                        Note($"选中 {card.Label} → 先点场上第一个元素（它旁边要有能配对的元素）；再点第二个元素；点右键可以取消");
                    }
                    else if (card.IsDecomposeCard)
                    {
                        Note($"选中 {card.Label} → 点场上绿色高亮的复合元素把它拆掉；点右键可以取消");
                    }
                    else
                    {
                        Note($"选中 {card.Label} → 点绿色高亮的元素格发生反应"
                             + (Engine.CanSummonOnEmptyCell
                                 ? $"；点绿色高亮的空格则在该格召唤 1 个{ElementDefs.Name(card.Element)}元素（点右键取消）"
                                 : "（点右键取消）"));
                    }
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
            FlushLog();
            board.Sync();
            hand.Sync(!InstantPlayback);
        }

        /// <summary>结束出牌阶段 → 进入弃牌阶段。</summary>
        public void EndPlayerRound()
        {
            if (Engine == null || Engine.Phase != GamePhase.PlayerAction) return;
            Engine.EndPlayerPhase();
            FlushLog();
        }

        /// <summary>结束弃牌阶段 → 进入判定阶段。</summary>
        public void EndDiscardRound()
        {
            if (Engine == null || Engine.Phase != GamePhase.Discard) return;
            Engine.EndDiscardPhase();
            FlushLog();
        }

        /// <summary>弃牌阶段点手牌即为弃掉。返回是否成功。</summary>
        public bool TryDiscard(int cardId)
        {
            if (Engine == null || Engine.Phase != GamePhase.Discard) return false;

            if (!Engine.TryDiscardCard(cardId))
            {
                Note($"✗ {Engine.FailReason}");
                return false;
            }

            SelectedCardId = -1;
            FlushLog();
            board.Sync();
            hand.Sync(!InstantPlayback);
            return true;
        }

        // ---------------------------------------------------------------- 日志

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

        void FlushLog()
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
                case GameEventType.Transform: return $"    {e.From} {e.Text}";
                case GameEventType.Vanish: return $"    {e.Text}";
                case GameEventType.Spawn: return $"    {e.To} {e.Text}";
                case GameEventType.SpawnBlocked: return $"    {e.From} {e.Text}";
                case GameEventType.CompositeSpawn: return $"  ◆ {e.To} {e.Text}";
                case GameEventType.CompositeVanish: return $"  ◇ {e.From} {e.Text}";
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

        // ---------------------------------------------------------------- 接线检查

        public void CollectWiringIssues(List<WiringIssue> into)
        {
            CheckWiring(board, hand, input, into);
        }

        /// <summary>纯函数版本，EditMode 测试直接用（不用反射，也不用开场景）。</summary>
        public static void CheckWiring(BoardView board, HandView hand, ClickRouter input, List<WiringIssue> into)
        {
            Wiring.Require(board, nameof(GameSession), nameof(board),
                "把挂了 BoardView 的物体（Anchors/BoardAnchor）拖进来。", into);
            Wiring.Require(hand, nameof(GameSession), nameof(hand),
                "把挂了 HandView 的物体（Anchors/HandAnchor）拖进来。", into);
            Wiring.Require(input, nameof(GameSession), nameof(input),
                "把挂了 ClickRouter 的物体（Systems/ClickRouter）拖进来。", into);
        }

        bool CheckRequiredRefs()
        {
            var issues = new List<WiringIssue>();
            CollectWiringIssues(issues);
            if (issues.Count == 0) return true;

            foreach (var issue in issues)
                Debug.LogError($"[TapGJ] {issue}");

            Debug.LogError("[TapGJ] GameSession 缺少必需引用，已停用。菜单 TapGJ ▸ 校验当前场景 会列出所有问题。");
            enabled = false;
            return false;
        }
    }
}
