using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 元素小瓶子：与五行元素一一对应，贯穿整个流程。
    /// 场上每消失一个某元素的小怪，对应瓶子 +1；满瓶（上限 5）自动产出一张对应元素牌并清空。
    /// </summary>
    public sealed class Bottle
    {
        public Element Element { get; }
        readonly int[] _counts = new int[ElementDefs.Count];
        /// <summary>每个元素各自「因为瓶满而产出过多少张牌」——第三章的教学目标全靠它。</summary>
        readonly int[] _granted = new int[ElementDefs.Count];

        /// <summary>
        /// 容量。**不是只读的**：回合事件（「玉翁敛息」）会临时把它改小、判定阶段再改回来。
        /// 读「本关的基准容量」请读 <see cref="Rules.BottleCapacity"/>，读「本回合实际生效的容量」读这个 ——
        /// HUD 画进度条必须读这个，否则事件一改容量它就会骗人。
        /// </summary>
        public int Capacity { get; private set; }

        /// <summary>本局瓶子一共产出过多少张牌（**不分元素**的总数）。分元素的用 <see cref="CardsGranted"/>。</summary>
        public int TotalCardsGranted { get; private set; }

        internal Bottle(int capacity)
        {
            Capacity = capacity < 1 ? 1 : capacity;
        }

        public int Count(Element e) => _counts[(int)e];

        /// <summary>这个元素的瓶子**已经产出过多少张对应元素牌**（第三章「生成 N 张火牌」这类目标用它）。</summary>
        public int CardsGranted(Element e) => _granted[(int)e];

        /// <summary>改容量（事件用）。只改数字，不结账 —— 要「先结账再改」用引擎的 ApplyBottleCapacity。</summary>
        internal void SetCapacity(int capacity) => Capacity = capacity < 1 ? 1 : capacity;

        /// <summary>计数 +1，返回是否满瓶产出卡牌。</summary>
        internal bool Add(Element e)
        {
            _counts[(int)e]++;
            if (_counts[(int)e] < Capacity) return false;
            _counts[(int)e] = 0;
            Grant(e, 1);
            return true;
        }

        /// <summary>
        /// 「结账」：按 <paramref name="capacity"/> 把瓶里攒够的份数换成卡牌，返回产出张数，余数留在瓶里。
        /// 4/3 → 1 张 + 余 1。这是玉翁敛息事件触发那一刻的行为（见 GameEngine.ApplyBottleCapacity）。
        /// </summary>
        internal int DrainAt(Element e, int capacity)
        {
            if (capacity < 1) return 0;

            int i = (int)e;
            if (_counts[i] < capacity) return 0;

            int cards = _counts[i] / capacity;
            _counts[i] %= capacity;
            Grant(e, cards);
            return cards;
        }

        /// <summary>记账：总数 + 分元素各记一份（两条产出路径都走这里）。</summary>
        void Grant(Element e, int cards)
        {
            if (cards <= 0) return;
            TotalCardsGranted += cards;
            _granted[(int)e] += cards;
        }

        public string DebugLine()
        {
            var parts = new List<string>();
            foreach (var e in ElementDefs.All)
                parts.Add($"{ElementDefs.Name(e)}{_counts[(int)e]}/{Capacity}");
            return string.Join("  ", parts);
        }
    }

    /// <summary>
    /// 纯 C# 的规则引擎，不引用 UnityEngine，可被 EditMode 测试直接 new 出来跑。
    ///
    /// 一回合流程（图1）：
    ///   准备阶段  → 每只小怪随机方向走 0~3 格，遇不同元素发生反应，遇同元素禁止移动
    ///   玩家行动  → 小怪静止。玩家出元素牌有两个用途（图1 第 1、2 条）：
    ///               打在元素格上 → 按图2 发生反应；
    ///               打在空格上   → 在该格召唤 1 个牌面元素的小怪（<see cref="EmptyCellCardMode"/> 可关）
    ///               玩家自行结束回合
    ///   判定阶段  → 判胜负，未达成则进入下一回合
    /// </summary>
    public sealed partial class GameEngine
    {
        public Rules Rules { get; }
        public Board Board { get; }
        public ReactionTable Reactions { get; }
        public Bottle Bottle { get; }

        readonly Random _rng;
        /// <summary>「这一回合还没解锁秘术牌」时用的空数组（避免每次抽牌都分配一个新数组）。</summary>
        static readonly CardKind[] NoSpecialCards = new CardKind[0];
        readonly List<Card> _hand = new List<Card>();
        readonly Dictionary<int, Creep> _creeps = new Dictionary<int, Creep>();
        readonly Queue<StepRecord> _steps = new Queue<StepRecord>();
        readonly List<StepRecord> _history = new List<StepRecord>();
        readonly List<GameEvent> _log = new List<GameEvent>();
        readonly Dictionary<Element, int> _removed = new Dictionary<Element, int>();
        readonly Dictionary<Element, int> _spawned = new Dictionary<Element, int>();
        /// <summary>键是「变之前」的元素：金变水记在金的账上。</summary>
        readonly Dictionary<Element, int> _converted = new Dictionary<Element, int>();
        /// <summary>玩家打出元素牌在空格上召唤出来的数量（图1 第 2 条），和相生生成分开记。</summary>
        readonly Dictionary<Element, int> _summoned = new Dictionary<Element, int>();

        int _nextUid = 1;
        int _nextCardId = 1;
        /// <summary>本回合已经行动过的小怪 uid。每回合清空，保证一只小怪一回合只走一次。</summary>
        readonly HashSet<int> _movedThisRound = new HashSet<int>();
        /// <summary>本回合抽牌阶段补到的牌数（用于日志/统计）。</summary>
        int _cardsDrawnThisRound;
        /// <summary>本回合弃牌阶段已弃的牌数。</summary>
        int _discardsUsed;
        /// <summary>本回合玩家行动阶段已打出的牌数。</summary>
        int _cardsPlayedThisRound;
        /// <summary>全局递增的步骤编号，用于事件/回放的顺序。</summary>
        int _stepIndex;

        public int Seed { get; }
        public int Round { get; private set; }
        public GamePhase Phase { get; private set; } = GamePhase.NotStarted;
        public bool DefeatByRoundLimit { get; private set; }
        public bool ResultReported { get; private set; }

        /// <summary>玩家行动阶段是否已经结束（弃牌阶段起为 true）。</summary>
        public bool PlayerPhaseEnded { get; private set; }

        /// <summary>弃牌阶段是否已经结束（判定阶段起为 true）。</summary>
        public bool DiscardPhaseEnded { get; private set; }

        /// <summary>本回合抽牌阶段实际补到的牌数。</summary>
        public int CardsDrawnThisRound => _cardsDrawnThisRound;

        /// <summary>本回合已经弃掉的牌数。</summary>
        public int DiscardsUsed => _discardsUsed;

        /// <summary>本回合还能弃几张。</summary>
        public int DiscardsRemaining => Math.Max(0, Rules.MaxDiscardsPerRound - _discardsUsed);

        /// <summary>本回合已经打出的牌数。</summary>
        public int CardsPlayedThisRound => _cardsPlayedThisRound;

        /// <summary>本回合还能出几张牌（未设上限时返回一个很大的数）。</summary>
        public int PlaysRemaining => Rules.MaxCardsPerRound <= 0
            ? int.MaxValue
            : Math.Max(0, Rules.MaxCardsPerRound - _cardsPlayedThisRound);

        public IReadOnlyList<Card> Hand => _hand;
        public IReadOnlyList<GameEvent> Log => _log;
        public IEnumerable<Creep> Creeps => _creeps.Values;
        public int CreepCount => Board.CreepCount();
        public bool AwaitingStepPlayback => _steps.Count > 0;

        /// <summary>本局累计各元素被相克消耗 / 出牌相生生成 / 相生转换掉的数量，用于观察平衡性。</summary>
        public IReadOnlyDictionary<Element, int> RemovedCounts => _removed;
        public IReadOnlyDictionary<Element, int> SpawnedCounts => _spawned;

        /// <summary>有多少只该元素**被相生变成了别的元素**（键是变之前的元素）。</summary>
        public IReadOnlyDictionary<Element, int> ConvertedCounts => _converted;

        public int TotalRemoved { get; private set; }
        public int TotalSpawned { get; private set; }

        /// <summary>本局累计发生的相生转换次数。</summary>
        public int TotalTransformed { get; private set; }

        /// <summary>本局累计「打出元素牌在空格上召唤」的次数（图1 第 2 条，和相生生成分开统计）。</summary>
        public int TotalSummoned { get; private set; }

        /// <summary>每个元素被玩家用牌召唤出来的次数。</summary>
        public IReadOnlyDictionary<Element, int> SummonedCounts => _summoned;

        /// <summary>出生失败的次数（反应本该生成新元素但四周无空格）。</summary>
        public int SpawnBlockedCount { get; private set; }

        public GameEngine(Rules rules = null, int seed = 12345)
        {
            Rules = (rules ?? new Rules()).Clone();
            Rules.Validate();

            Seed = seed;
            _rng = new Random(seed);
            Board = new Board(Rules.BoardWidth, Rules.BoardHeight);
            Reactions = new ReactionTable();
            Bottle = new Bottle(Rules.BottleCapacity);
        }

        // ---------------------------------------------------------------- 开局

        /// <summary>初始化棋盘：五种元素各 CreepsPerElement 个均匀随机撒在棋盘上；玩家发初始手牌；进入第 1 回合准备阶段。</summary>
        public void Start() => Start(null);

        /// <summary>
        /// 初始化一局。setup 传 null 走现有的随机铺盘；传了就走关卡布置（固定格子 + 墙 + 起手牌）。
        /// </summary>
        public void Start(LevelSetup setup)
        {
            Board.ClearAll();
            _creeps.Clear();
            _hand.Clear();
            _steps.Clear();
            _history.Clear();
            _log.Clear();
            _removed.Clear();
            _spawned.Clear();
            _converted.Clear();
            _summoned.Clear();
            _nextUid = 1;
            _nextCardId = 1;
            _movedThisRound.Clear();
            ResetCompositeState();
            _cardsDrawnThisRound = 0;
            _discardsUsed = 0;
            _cardsPlayedThisRound = 0;
            _stepIndex = 0;
            Round = 0;
            DefeatByRoundLimit = false;
            ResultReported = false;
            PlayerPhaseEnded = false;
            DiscardPhaseEnded = false;
            TotalRemoved = 0;
            TotalSpawned = 0;
            TotalTransformed = 0;
            TotalSummoned = 0;
            SpawnBlockedCount = 0;
            ResetEventState();

            if (setup == null)
            {
                // 均匀生成：把 64 格洗牌，前 20 格按「每种可用元素 CreepsPerElement 个」铺开。
                // 用 Rules.PoolElements 而不是 ElementDefs.All —— 这就是「本关只出现这几种元素」。
                var cells = new List<Pos>(Rules.CellCount);
                for (int y = 0; y < Board.Height; y++)
                    for (int x = 0; x < Board.Width; x++)
                        cells.Add(new Pos(x, y));
                Board.Shuffle(cells, _rng);

                int index = 0;
                foreach (var element in Rules.PoolElements)
                {
                    for (int i = 0; i < Rules.CreepsPerElement; i++)
                    {
                        if (index >= cells.Count) break;
                        var creep = new Creep(_nextUid++, element, cells[index++]);
                        _creeps[creep.Uid] = creep;
                        Board.Put(creep);
                    }
                }

                for (int i = 0; i < Rules.InitialHandSize; i++) DrawRandomCard();
            }
            else
            {
                ApplyLevelSetup(setup);
            }

            // 初始复合元素和「布局 / 随机铺盘」无关：布局留空时 LevelSetup 是 null，这里也要照摆。
            ApplyInitialComposites(Rules.InitialComposites);

            // 关卡机制在第 1 回合开始之前先跑 OnLevelStart（这时棋盘和手牌都已经就位）。
            FireLevelStart();
            BeginRound();

            // 第一次事件从第 1 回合之后开始排（默认落在第 4~7 回合）。必须放在 BeginRound 之后：
            // BeginRound 里第 1 回合的「轮到事件了吗」检查要看到 _nextEventRound == 0（不触发）。
            ScheduleFirstEventIfNeeded();
        }

        /// <summary>按关卡布置铺盘：墙、固定元素、起手牌（没给起手牌就随机抽）。</summary>
        void ApplyLevelSetup(LevelSetup setup)
        {
            int w = Board.Width;
            int h = Board.Height;

            for (int i = 0; i < w * h; i++)
            {
                if (setup.Blocked == null || i >= setup.Blocked.Length || !setup.Blocked[i]) continue;
                Board.SetBlocked(new Pos(i % w, i / w), true);
            }

            if (setup.Cells != null)
            {
                for (int i = 0; i < w * h && i < setup.Cells.Length; i++)
                {
                    var element = setup.Cells[i];
                    if (!element.HasValue) continue;

                    var pos = new Pos(i % w, i / w);
                    if (Board.IsBlocked(pos)) continue; // 不变量兜底：元素不能站在墙上（解析层不会产出这种，但保险）

                    var creep = new Creep(_nextUid++, element.Value, pos);
                    _creeps[creep.Uid] = creep;
                    Board.Put(creep);
                }
            }

            if (setup.InitialHand != null && setup.InitialHand.Length > 0)
                foreach (var e in setup.InitialHand) DrawCard(e);
            else
                for (int i = 0; i < Rules.InitialHandSize; i++) DrawRandomCard();
        }

        /// <summary>
        /// 按关卡布置摆初始复合元素（图2 的复合元素也可以在开局就摆在场上，
        /// 这样「场上已经有复合元素 → 融合牌用不出去」这条规则一开始就成立）。
        ///
        /// ⚠ 总开关 <see cref="Rules.UseCompositeElements"/> 没勾时**一个都不摆**：
        /// 配置校验会把「配了初始复合元素却没勾开关」明确报出来，不会静默生效。
        /// 与元素冲突的格子（已被占 / 墙）会被跳过并记一条事件，不抛异常。
        /// </summary>
        void ApplyInitialComposites(CompositePlacement[] placements)
        {
            if (placements == null) return;

            if (!Rules.UseCompositeElements)
            {
                if (placements.Length > 0)
                {
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"本关配了 {placements.Length} 个初始复合元素，但「复合元素」总开关没勾，全部跳过",
                    });
                }
                return;
            }

            foreach (var entry in placements)
            {
                var pos = new Pos(entry.X, entry.Y);
                if (!Board.InBounds(pos))
                {
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"初始复合元素坐标 {pos} 越界，已跳过",
                    });
                    continue;
                }
                if (Board.IsBlocked(pos) || !Board.IsEmpty(pos))
                {
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"初始复合元素「{CompositeDefs.Name(entry.Kind)}」的位置 {pos} 已被占用或是墙格，已跳过",
                    });
                    continue;
                }

                var composite = new Composite(_nextUid++, entry.Kind, pos);
                _composites[composite.Uid] = composite;
                Board.PutComposite(composite);
            }
        }

        public void BeginRound()
        {
            Round++;
            Phase = GamePhase.Preparation;
            PlayerPhaseEnded = false;
            DiscardPhaseEnded = false;
            _discardsUsed = 0;
            _cardsPlayedThisRound = 0;
            _cardsDrawnThisRound = 0;
            _movedThisRound.Clear(); // 新回合：所有存活的小怪重新获得一次移动机会
            _compositeProducedThisRound.Clear(); // 复合元素每回合产生一次
            _compositeProducedCountThisRound = 0;
            SuppressCreepMovement = false; // 静滞结界只在它自己那一回合有效
            PushEvent(new GameEvent { Type = GameEventType.Message, Text = $"—— 第 {Round} 回合：准备阶段 ——" });

            // 回合事件先于关卡机制：事件是「本回合的环境」，环境变了之后关卡机制再照常运作。
            // 两者都在小怪移动之前 —— 所以「静滞结界」真能定住本回合所有小怪（包括机制刚生成的）。
            if (_nextEventRound > 0 && Round == _nextEventRound) FireRoundEvent();

            FireRoundStart(); // 机制可以在这里生成元素 / 加墙，新生成的小怪本回合就会走
        }

        void SetPhase(GamePhase phase)
        {
            if (Phase == phase) return;
            Phase = phase;
            PushEvent(new GameEvent { Type = GameEventType.Message, Text = $"阶段切换：{PhaseName(phase)}" });
        }

        public static string PhaseName(GamePhase p)
        {
            switch (p)
            {
                case GamePhase.NotStarted: return "未开始";
                case GamePhase.Preparation: return "准备阶段";
                case GamePhase.Draw: return "抽牌阶段";
                case GamePhase.PlayerAction: return "玩家行动阶段";
                case GamePhase.Discard: return "弃牌阶段";
                case GamePhase.Judgment: return "判定阶段";
                case GamePhase.Victory: return "游戏结束（胜利）";
                case GamePhase.Defeat: return "游戏结束（失败）";
                default: return p.ToString();
            }
        }

        // ------------------------------------------------------------ 准备阶段

        /// <summary>
        /// 推进一个准备阶段步骤。
        /// 顺序是「**先让复合元素产生一次**，再让小怪移动」——这样复合元素生出来的元素
        /// 本回合就能正常参与移动/相遇；也因为回合开始就先跑，玩家看到的是「回合开始→复合元素生效」。
        /// 本回合所有小怪都走完后，本次调用会自动把阶段切到「玩家行动阶段」并返回 null。
        /// </summary>
        public StepRecord AdvancePreparation()
        {
            if (Phase != GamePhase.Preparation) return null;

            if (HasPendingCompositeProduction())
                return AdvanceCompositeProduction();

            // 静滞结界：本回合小怪全部定住 —— 不移动就不会相遇、不会反应。
            // 复合元素的「产生」不看这个开关（它是产出元素，不是元素之间的相互作用），所以放在上面。
            if (SuppressCreepMovement)
            {
                FinishPreparationAndDraw();
                return null;
            }

            var creep = NextIdleCreep();
            if (creep == null)
            {
                FinishPreparationAndDraw();
                return null;
            }

            var record = ResolveCreepStep(creep);
            record.Index = _stepIndex++;
            Enqueue(record);

            // 最后一只走完就立刻补牌并切到玩家行动阶段，不需要调用方再空推一次
            if (NextIdleCreep() == null) FinishPreparationAndDraw();
            return record;
        }

        /// <summary>
        /// 准备阶段结束 → 抽牌阶段 → 玩家行动阶段。
        /// 抽牌是自动的：把手牌补到上限（手牌已满就一张都不抽），玩家不需要操作。
        /// 补到的牌会排成一个步骤，让表现层能一张张播出来。
        /// </summary>
        void FinishPreparationAndDraw()
        {
            SetPhase(GamePhase.Draw);
            ResolveDrawPhase();
            EnterPlayerPhase();
        }

        /// <summary>
        /// 抽牌阶段：把手牌补到 <see cref="Rules.HandLimit"/>。
        /// 每张牌的元素/种类由 <see cref="DrawRandomCard"/> 按牌池与权重决定（所以秘术牌的比例可控）。
        /// 返回本次抽到的牌数（0 表示本来就满了）。
        /// </summary>
        public int ResolveDrawPhase()
        {
            int before = _hand.Count;
            int target = Rules.HandLimit;
            var drawn = new List<Card>();

            while (_hand.Count < target)
            {
                var card = DrawRandomCard();
                drawn.Add(card);
            }

            int gained = _hand.Count - before;
            if (gained > 0) _cardsDrawnThisRound += gained;

            PushEvent(new GameEvent
            {
                Type = GameEventType.Message,
                Text = gained > 0
                    ? $"抽牌阶段：抽 {gained} 张（{before} → {_hand.Count} / 上限 {target}）"
                    : $"抽牌阶段：手牌已满（{before} / 上限 {target}），不抽牌",
            });

            if (gained > 0)
            {
                var record = new StepRecord { Index = _stepIndex++, IsDrawPhase = true };
                foreach (var card in drawn)
                {
                    var e = new GameEvent
                    {
                        Type = GameEventType.DrawCard,
                        Element = card.Element,
                        Text = $"抽到 {card.Label}",
                    };
                    record.Events.Add(e);
                    PushEvent(e);
                }
                foreach (var e in record.Events) e.StepIndex = record.Index;
                Enqueue(record);
            }

            return gained;
        }

        void EnterPlayerPhase()
        {
            if (Phase == GamePhase.PlayerAction) return;
            SetPhase(GamePhase.PlayerAction);
            PushEvent(new GameEvent { Type = GameEventType.Message, Text = "小怪停止移动，玩家可以出牌" });
        }

        Creep NextIdleCreep()
        {
            // 按 uid 顺序推进，保证同一种子下结果完全可复现。
            var list = new List<Creep>(_creeps.Values);
            list.Sort((a, b) => a.Uid.CompareTo(b.Uid));
            foreach (var c in list)
            {
                if (!c.Alive) continue;
                if (_movedThisRound.Contains(c.Uid)) continue;
                return c;
            }
            return null;
        }

        StepRecord ResolveCreepStep(Creep creep)
        {
            var record = new StepRecord
            {
                Uid = creep.Uid,
                Element = creep.Element,
                From = creep.Pos,
                To = creep.Pos,
            };
            record.Path.Add(creep.Pos);

            int budget = NextRandom(Rules.MaxMoveSteps + 1);
            record.Distance = budget;

            Pos dir = RandomDirection();
            Pos cursor = creep.Pos;
            Pos travelledFrom = creep.Pos;

            for (int step = 0; step < budget; step++)
            {
                var next = new Pos(cursor.X + dir.X, cursor.Y + dir.Y);

                // 撞到棋盘边界或墙，本次移动结束
                if (!Board.IsWalkable(next)) break;

                // ⚠ 这里必须用 IsEmpty 而不是 At() == null：
                //   复合元素占着的格子上 At() 也返回 null，只看 At() 会以为那是空格，
                //   于是小怪会**直接走进复合元素那一格**（表现层看不出，逻辑层会抛异常）。
                if (!Board.IsEmpty(next))
                {
                    var other = Board.At(next);
                    if (other == null)
                    {
                        // 那格是复合元素：走不进去，也不发生任何元素反应
                        record.Events.Add(new GameEvent
                        {
                            Type = GameEventType.Bump,
                            From = cursor,
                            To = next,
                            Element = creep.Element,
                            Uid = creep.Uid,
                            Text = $"被复合元素挡住，本次移动结束",
                        });
                        break;
                    }

                    if (other.Element == creep.Element)
                    {
                        // 同种元素相遇：禁止此次移动，双方留在原地
                        record.Events.Add(new GameEvent
                        {
                            Type = GameEventType.Bump,
                            From = cursor,
                            To = next,
                            Element = creep.Element,
                            Uid = creep.Uid,
                            Text = $"同种{ElementDefs.Name(creep.Element)}相遇，禁止移动",
                        });
                    }
                    else
                    {
                        // 不同元素相遇：发生反应。相克可能会把拦路的那只清掉，
                        // 于是主动方可以顺势占住那一格并接着把剩余步数走完。
                        bool mayContinue = ResolveEncounter(creep, other, next, record);

                        if (!creep.Alive)
                        {
                            record.To = record.Path.Count > 0 ? record.Path[record.Path.Count - 1] : creep.Pos;
                            break;
                        }

                        if (!mayContinue) break; // 相生：格子还被占着，走不动了
                        cursor = creep.Pos;      // 相克且主动方赢：已经站到新格子上，继续
                        continue;
                    }
                    break;
                }

                // 空格：正常前进
                Board.Clear(cursor);
                creep.Pos = next;
                Board.Put(creep);
                cursor = next;
                record.Path.Add(cursor);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Move,
                    From = travelledFrom,
                    To = cursor,
                    Element = creep.Element,
                    Uid = creep.Uid,
                    Text = $"移动 {Board.Manhattan(travelledFrom, cursor)} 格",
                });
            }

            record.To = creep.Pos;
            record.CreepWentAway = !creep.Alive;
            foreach (var e in record.Events) e.StepIndex = _stepIndex;
            _movedThisRound.Add(creep.Uid);
            return record;
        }

        /// <summary>统一随机入口：沙盒/测试可以注入固定序列。</summary>
        int NextRandom(int maxExclusive) => _overrideRng != null ? _overrideRng.Next(maxExclusive) : _rng.Next(maxExclusive);

        /// <summary>Core 不引用 UnityEngine，进度条的钳制自己写。</summary>
        static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

        Pos RandomDirection()
        {
            switch (NextRandom(4))
            {
                case 0: return new Pos(0, 1);
                case 1: return new Pos(1, 0);
                case 2: return new Pos(0, -1);
                default: return new Pos(-1, 0);
            }
        }

        /// <summary>
        /// 触发方(trigger) 撞上 目标(target)。按图2 的表结算（详见 ReactionTable 的注释）：
        ///   相生（原表「X元素+1」句式）→ 输的一方**就地变成**赢家元素，格子占用不变；
        ///   相克（原表「该X元素消失」句式）→ 输的一方**直接消失**，格子空出来。
        /// 谁输由表决定（def.Winner 是赢家，另一方就是输家），不能写死成「主动方一定输」。
        /// </summary>
        /// <returns>
        /// true = 主动方赢了相克、已经占住让出来的那一格，调用方可以让它接着走完剩余步数。
        /// </returns>
        bool ResolveEncounter(Creep trigger, Creep target, Pos meetingPoint, StepRecord record)
        {
            // 快照要在结算之前取：相生会把输家的元素就地改掉，事后就读不到「谁遇上了谁」了。
            int triggerUid = trigger.Uid;
            int targetUid = target.Uid;
            var triggerElement = trigger.Element;
            var targetElement = target.Element;

            var def = Reactions.Get(triggerElement, targetElement);

            if (def.Rule == ReactionRule.NoReaction)
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Bump,
                    From = trigger.Pos,
                    To = meetingPoint,
                    Element = triggerElement,
                    Uid = triggerUid,
                    Text = $"同种{ElementDefs.Name(triggerElement)}相遇，禁止移动",
                });
                FireEncounter(Encounter(triggerUid, triggerElement, targetUid, targetElement, def.Rule, meetingPoint,
                    false, triggerElement, triggerElement));
                return false;
            }

            var winnerIsTrigger = def.Winner == triggerElement;
            var loser = winnerIsTrigger ? target : trigger;
            var loserElement = loser.Element;
            bool winnerClaimsCell = false;

            if (def.Rule == ReactionRule.BothVanish)
            {
                // 可选规则：双方都消失，没有任何赢家，也不发生相生转换
                RemoveCreep(trigger, record, meetingPoint);
                RemoveCreep(target, record, meetingPoint);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = meetingPoint,
                    Element = triggerElement,
                    Uid = triggerUid,
                    Text = $"{ElementDefs.Name(triggerElement)} × {ElementDefs.Name(targetElement)} → 双方都消失",
                });
            }
            else if (def.Rule == ReactionRule.Transform)
            {
                // 相生：输家变成赢家元素。uid / 位置 / 存活状态全都不变，只换元素。
                loser.Element = def.Winner;

                TotalTransformed++;
                _converted[loserElement] = ConvertedOf(loserElement) + 1;

                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Transform,
                    From = loser.Pos,
                    To = loser.Pos,
                    Element = loserElement,
                    Uid = loser.Uid,
                    Text = $"{ElementDefs.Name(triggerElement)} × {ElementDefs.Name(targetElement)} → "
                           + $"{ElementDefs.Name(loserElement)}变为{ElementDefs.Name(def.Winner)}（相生）",
                });
                // 格子没空出来，主动方走不动
            }
            else
            {
                // 相克：输家直接消失
                RemoveCreep(loser, record, meetingPoint);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = meetingPoint,
                    Element = loserElement,
                    Uid = loser.Uid,
                    Text = $"{ElementDefs.Name(triggerElement)} × {ElementDefs.Name(targetElement)} → "
                           + $"{ElementDefs.Name(loserElement)}消失（{ElementDefs.Name(def.Winner)}克之）",
                });

                // 赢家是撞上去的那一方时，它顺势占住让出来的那一格（等于往前走了一步）
                if (winnerIsTrigger && Board.IsEmpty(meetingPoint))
                {
                    Board.Clear(trigger.Pos);
                    trigger.Pos = meetingPoint;
                    Board.Put(trigger);
                    record.Path.Add(meetingPoint);
                    record.To = meetingPoint;
                    winnerClaimsCell = true;
                }
            }

            // 结算完再通知机制：它看到的棋盘已经是最新状态，但快照里是「相遇时的两种元素」。
            FireEncounter(Encounter(triggerUid, triggerElement, targetUid, targetElement, def.Rule, meetingPoint,
                false, def.Rule == ReactionRule.BothVanish ? triggerElement : def.Winner, loserElement));
            return winnerClaimsCell;
        }

        /// <summary>组装相遇快照。元素一律显式传入，避免读到相生之后已经被改掉的值。</summary>
        static EncounterInfo Encounter(int triggerUid, Element triggerElement, int targetUid, Element targetElement,
            ReactionRule rule, Pos cell, bool isPlayerCard, Element winner, Element loser)
        {
            return new EncounterInfo(triggerUid, targetUid, triggerElement, targetElement,
                rule, winner, loser, cell, isPlayerCard);
        }

        /// <summary>
        /// 找出生格：只找「紧邻反应点」的空格（正交 + 斜角，共 8 格）。
        /// 图2 与图1 都写的是「在相邻空格 / 元素+1」，所以不做全盘搜索——
        /// 周围真的被塞满时就该记一次出生失败，而不是飞到棋盘另一头。
        /// </summary>
        Pos SpawnCellFor(Pos reactionPoint)
        {
            return Board.FindSpawnCell(reactionPoint, _rng, Pos.None, 1);
        }

        void SpawnCreep(Element element, Pos cell, StepRecord record, Pos reactionOrigin)
        {
            // 本关不用这个元素 → 不生成，但要说清楚原因（和「周围没空格」不是一回事）。
            // 只在关卡把元素池限制成子集时才会走到这里；默认池是「全部」，行为与以前完全一致。
            if (!Rules.ElementPool.Has(element))
            {
                SpawnBlockedCount++;
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.SpawnBlocked,
                    From = reactionOrigin,
                    Element = element,
                    Text = $"{ElementDefs.Name(element)}元素本应生成，但本关可用元素只有「{Rules.ElementPool.Names()}」",
                });
                return;
            }

            // 不变量：棋盘每格最多一个元素。兜底再确认一次，绝不允许盖在别人身上。
            if (!cell.IsNone && !Board.IsEmpty(cell))
                cell = Board.FindSpawnCell(reactionOrigin, _rng, Pos.None, 7);

            if (cell.IsNone || !Board.IsEmpty(cell))
            {
                SpawnBlockedCount++;
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.SpawnBlocked,
                    From = reactionOrigin,
                    Element = element,
                    Text = $"{ElementDefs.Name(element)}元素本应生成，但周围没有空格",
                });
                return;
            }

            var creep = CreateCreepAt(element, cell);

            TotalSpawned++;
            _spawned[element] = SpawnedCounts.TryGetValue(element, out var n) ? n + 1 : 1;

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Spawn,
                From = reactionOrigin,
                To = cell,
                Element = element,
                Uid = creep.Uid,
                Text = $"生成 1 个{ElementDefs.Name(element)}元素",
            });
        }

        /// <summary>
        /// 真正把一只新小怪放上棋盘：建对象、进 uid 表、占格。
        /// **不记账、不发事件** —— 三种来源（反应生成 / 复合元素产出 / 玩家召唤）各自决定怎么记账。
        /// 调用方保证 <paramref name="cell"/> 是空格（<see cref="Board.Put"/> 会挡住墙和复合元素）。
        /// </summary>
        Creep CreateCreepAt(Element element, Pos cell)
        {
            var creep = new Creep(_nextUid++, element, cell);
            _creeps[creep.Uid] = creep;
            Board.Put(creep);
            return creep;
        }

        void RemoveCreep(Creep creep, StepRecord record, Pos where) => RemoveCreep(creep, record, where, true);

        /// <summary>
        /// 让一只小怪从棋盘上消失。
        /// <paramref name="feedBottle"/> = false 用于「被消耗掉」的场合（融合牌的素材）——
        /// 那些元素不是被相克湮灭的，按图2 的设定不该喂小瓶子。
        /// </summary>
        void RemoveCreep(Creep creep, StepRecord record, Pos where, bool feedBottle)
        {
            if (!creep.Alive) return;

            creep.Alive = false;
            Board.Clear(creep.Pos);
            _creeps.Remove(creep.Uid);

            // 本关没有元素瓶（Rules.BottleMode = Disabled）时：不喂瓶、不产牌、也不发那两条日志。
            // ⚠ 只影响「瓶子」这一层：元素照样从棋盘消失，TotalRemoved / RemovedCounts 照常记。
            bool feedsBottle = feedBottle && Rules.BottlesEnabled;

            // 场上消失一个元素 → 对应瓶子 +1；满瓶自动产出一张对应元素牌。
            if (feedsBottle && Bottle.Add(creep.Element))
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.GainCard,
                    Element = creep.Element,
                    Text = $"{ElementDefs.Name(creep.Element)}瓶子满，获得 1 张{ElementDefs.Name(creep.Element)}牌",
                });
                DrawCard(creep.Element);
            }

            if (feedsBottle)
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.BottleFill,
                    From = where,
                    Element = creep.Element,
                    Text = $"{ElementDefs.Name(creep.Element)}瓶 +1",
                });
            }

            TotalRemoved++;
            _removed[creep.Element] = RemovedOf(creep.Element) + 1;
        }

        // -------------------------------------------------------- 玩家行动阶段

        /// <summary>
        /// 打出一张元素牌（默认 CardEffectMode.CardAsVirtualElement）：
        /// 牌面元素被当作一个「虚拟小怪」，与目标格上的元素按图2 的表发生反应，
        /// 消失/生成的结果直接作用在棋盘上（棋盘元素计数变化）。
        ///
        /// 目标是**空格**时走另一条路（图1 第 2 条）：在该格召唤 1 个牌面元素的小怪，
        /// 见 <see cref="TrySummonCard"/>；墙格 / 复合元素格两种都用不了。
        /// 返回 null 表示这次操作不合法，原因见 FailReason。
        /// </summary>
        public StepRecord TryPlayCard(int cardId, Pos target)
        {
            FailReason = null;
            if (Phase != GamePhase.PlayerAction)
            {
                FailReason = "当前不是玩家行动阶段";
                return null;
            }

            if (Rules.MaxCardsPerRound > 0 && _cardsPlayedThisRound >= Rules.MaxCardsPerRound)
            {
                FailReason = $"本回合已经出了 {_cardsPlayedThisRound} 张牌（上限 {Rules.MaxCardsPerRound}）";
                return null;
            }

            var card = FindCard(cardId);
            if (card == null)
            {
                FailReason = "手牌里没有这张牌";
                return null;
            }

            // 秘术牌（融合牌/分解牌）不走这条路径：它们没有牌面元素，也不和元素发生反应。
            // 走错了要明确报出来，而不是默默拿占位元素去算反应。
            if (!card.IsElementCard)
            {
                FailReason = card.IsFusionCard
                    ? "融合牌要这样用：先点这张牌，再依次点场上相邻的两个元素（点右键可以取消）"
                    : "分解牌要这样用：先点这张牌，再点场上要拆掉的复合元素（点右键可以取消）";
                return null;
            }

            // 本关可用元素之外的牌不能打：否则相生会把池外元素变到棋盘上，
            // 「本关只出现这些元素」这个约定就保不住了。配置正确时永远走不到这里
            // （CardPool 必须是 ElementPool 的子集，Rules.CollectIssues 会报）。
            if (!Rules.ElementPool.Has(card.Element))
            {
                FailReason = $"本关可用元素只有「{Rules.ElementPool.Names()}」，"
                             + $"{ElementDefs.Name(card.Element)}牌在本关用不了";
                return null;
            }

            if (!Board.InBounds(target))
            {
                FailReason = $"目标格 {target} 不在棋盘内（棋盘 {Board.Width}×{Board.Height}）";
                return null;
            }

            // 图1 玩家行动阶段的第 2 条：空格也能用牌 —— 在该格召唤牌面元素的小怪。
            // 判定放在最前面，因为「打在空格上」和「打在墙 / 复合元素上」要给完全不同的说法。
            if (Board.IsEmpty(target)) return TrySummonCard(card, target);

            if (Board.IsBlocked(target))
            {
                FailReason = $"{target} 是墙格，不能出牌";
                return null;
            }

            if (Board.HasComposite(target))
            {
                FailReason = $"{target} 上是复合元素，元素牌打不动它（要用分解牌）";
                return null;
            }

            var victim = Board.At(target);
            if (victim == null)
            {
                // 兜底：既不是空格、又不是墙 / 复合元素，那格一定有小怪。真走到这里说明棋盘不变量坏了。
                FailReason = "该格没有元素小怪，无法发生反应";
                return null;
            }

            var plan = PlanCardEffect(victim, card);
            if (plan == null)
            {
                FailReason = BuildFailReason(victim, card);
                return null;
            }

            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsPlayerCard = true,
                Element = card.Element,
                From = Pos.None,
                To = target,
                CreepWentAway = plan.Value.TargetVanishes,
            };

            _hand.Remove(card);
            _cardsPlayedThisRound++;
            var playedElement = victim.Element;
            ApplyCardEffect(plan.Value, victim, card, target, record);

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);

            // 出牌也是一次「元素相遇」，机制照样要收到通知（相生转换计数之类全靠它）。
            FireEncounter(CardEncounter(card, victim, playedElement, plan.Value, target));
            return record;
        }

        /// <summary>
        /// 本关允不允许把元素牌打在空格上召唤（图1 第 2 条，见 <see cref="EmptyCellCardMode"/>）。
        /// 表现层用它决定「空格要不要一起高亮」。
        /// </summary>
        public bool CanSummonOnEmptyCell => Rules.EmptyCellCard == EmptyCellCardMode.Summon;

        /// <summary>
        /// 图1 玩家行动阶段的第 2 条：「玩家可以对空的棋盘格使用卡牌，可以在该格召唤该元素小怪」。
        ///
        /// 和打在元素格上的区别（这几条是玩法约定）：
        ///   · **不发生任何反应** —— 只是在该格多出一只牌面元素的小怪，不通知关卡机制（没有相遇）
        ///   · **一样消耗这张牌、一样占每回合出牌上限**（它就是一次正常出牌）
        ///   · **不喂小瓶子**（瓶子只认「元素从棋盘上消失」，召唤是凭空多一只）
        ///   · 只有空格可以：墙格、复合元素占着的格子都会被 <see cref="TryPlayCard"/> 提前拦下
        /// </summary>
        StepRecord TrySummonCard(Card card, Pos cell)
        {
            if (!CanSummonOnEmptyCell)
            {
                FailReason = $"该格没有元素小怪，无法发生反应"
                             + $"（当前规则「{nameof(Rules.EmptyCellCard)} = {Rules.EmptyCellCard}」不允许在空格上召唤）";
                return null;
            }

            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsPlayerCard = true,
                IsSummon = true,
                Element = card.Element,
                From = Pos.None,
                To = cell,
            };

            _hand.Remove(card);
            _cardsPlayedThisRound++;

            var creep = CreateCreepAt(card.Element, cell);
            TotalSummoned++;
            _summoned[card.Element] = SummonedOf(card.Element) + 1;

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Spawn,
                From = cell,
                To = cell,
                Element = card.Element,
                Uid = creep.Uid,
                Text = $"打出{ElementDefs.Name(card.Element)}牌 → 召唤 1 个{ElementDefs.Name(card.Element)}元素",
            });

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);
            return record;
        }

        /// <summary>
        /// 选中一张元素牌时该高亮哪些格子：「该点哪里」的唯一真源（表现层不要自己算）。
        /// 这批格子 = 能按图2 发生反应的元素格（图1 第 1 条）+ 能召唤的空格（图1 第 2 条）。
        ///
        /// 池外的牌打不出去（<see cref="TryPlayCard"/> 会拒），所以一张都不高亮。
        /// 「本回合出牌次数用完了」不在这里判 —— 和 <see cref="CanPlayCardOn"/> 保持一致，
        /// 高亮只回答「这个目标是合法目标」，阶段 / 次数由调用方把关。
        /// </summary>
        public List<Pos> CellsForElementCard(Card card)
        {
            var result = new List<Pos>();
            if (card == null || !card.IsElementCard) return result;
            if (!Rules.ElementPool.Has(card.Element)) return result;

            foreach (var creep in Board.Creeps())
                if (CanPlayCardOn(card, creep)) result.Add(creep.Pos);

            if (!CanSummonOnEmptyCell) return result;

            for (int y = 0; y < Board.Height; y++)
                for (int x = 0; x < Board.Width; x++)
                {
                    var pos = new Pos(x, y);
                    if (Board.IsEmpty(pos)) result.Add(pos);
                }

            return result;
        }

        /// <summary>这张牌能不能打在某个空格上召唤（表现层单点判断用；批量用 <see cref="CellsForElementCard"/>）。</summary>
        public bool CanSummonCardAt(Card card, Pos cell)
        {
            if (card == null || !card.IsElementCard) return false;
            if (!CanSummonOnEmptyCell) return false;
            if (!Rules.ElementPool.Has(card.Element)) return false;
            return Board.IsEmpty(cell);
        }

        /// <summary>把一次出牌的结算计划翻译成相遇快照。</summary>
        static EncounterInfo CardEncounter(Card card, Creep victim, Element victimElement, CardPlan plan, Pos target)
        {
            if (plan.TargetVanishes)
            {
                return Encounter(-1, card.Element, victim.Uid, victimElement, ReactionRule.Annihilate, target, true,
                    card.Element, victimElement);
            }

            // 相生：输的一方变成赢家元素。目标变掉了 = 牌赢了；目标没变 = 目标赢了。
            bool cardWins = plan.TargetBecomes != victimElement;
            return cardWins
                ? Encounter(-1, card.Element, victim.Uid, victimElement, ReactionRule.Transform, target, true,
                    card.Element, victimElement)
                : Encounter(-1, card.Element, victim.Uid, victimElement, ReactionRule.Transform, target, true,
                    victimElement, card.Element);
        }

        /// <summary>一次出牌的结算计划（由 PlanCardEffect 算出，具体生效在 ApplyCardEffect）。</summary>
        struct CardPlan
        {
            /// <summary>目标最终的元素（相生时会被改掉）。</summary>
            public Element TargetBecomes;
            /// <summary>目标是否直接消失（相克且牌面元素赢）。</summary>
            public bool TargetVanishes;
            /// <summary>是否额外生成一只、以及生成什么元素（相生的「+1」）。</summary>
            public Element? Spawn;
        }

        /// <summary>
        /// 算出这次出牌的结果；返回 null 表示这次出牌不合法（打出去场上什么都不会变）。
        ///
        /// 牌 = 一个「虚拟元素」主动撞上目标格上的元素，按图2 同一张表结算：
        ///   · 相克（「该X元素消失」）—— **只有牌面元素克制目标**时才有意义：
        ///       目标直接消失，不生成任何东西。
        ///       反过来（目标克制牌）时牌打出去场上毫无变化，所以判定为不合法。
        ///   · 相生（「X元素+1」）—— 输的一方变成赢家元素，并且赢家元素在目标旁边**+1**：
        ///       牌面元素赢 → 目标就地变成牌面元素，旁边再生成一个牌面元素；
        ///       目标赢     → 目标不变，牌自己变成目标元素落到旁边（即生成 1 个目标元素）。
        ///     两种情况都会在目标周围多出一只，这正是原表写的「+1」。
        /// </summary>
        CardPlan? PlanCardEffect(Creep victim, Card card)
        {
            switch (Rules.CardEffect)
            {
                case CardEffectMode.SameElementOnly:
                    if (victim.Element != card.Element) return null;
                    return new CardPlan { TargetBecomes = victim.Element, TargetVanishes = true, Spawn = null };

                case CardEffectMode.SameElementSpawnOne:
                    if (victim.Element != card.Element) return null;
                    return new CardPlan { TargetBecomes = victim.Element, TargetVanishes = true, Spawn = victim.Element };

                default: // CardAsVirtualElement
                {
                    if (victim.Element == card.Element) return null; // 同元素之间不发生反应

                    var def = Reactions.Get(victim.Element, card.Element);
                    if (def.Rule == ReactionRule.NoReaction) return null;

                    if (def.Rule == ReactionRule.BothVanish)
                        return new CardPlan { TargetBecomes = victim.Element, TargetVanishes = true, Spawn = null };

                    var cardWins = def.Winner == card.Element;

                    if (def.Rule == ReactionRule.Annihilate)
                    {
                        // 相克：牌面元素赢 → 目标消失；牌面元素输 → 这张牌不该能打出去
                        if (!cardWins) return null;
                        return new CardPlan { TargetBecomes = victim.Element, TargetVanishes = true, Spawn = null };
                    }

                    // 相生：输的一方变成赢家元素，且赢家元素在旁边 +1
                    return cardWins
                        ? new CardPlan { TargetBecomes = card.Element, TargetVanishes = false, Spawn = card.Element }
                        : new CardPlan { TargetBecomes = victim.Element, TargetVanishes = false, Spawn = victim.Element };
                }
            }
        }

        void ApplyCardEffect(CardPlan plan, Creep victim, Card card, Pos target, StepRecord record)
        {
            var victimElement = victim.Element;

            if (plan.TargetVanishes)
            {
                RemoveCreep(victim, record, target);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = target,
                    Element = victimElement,
                    Uid = victim.Uid,
                    Text = $"打出{ElementDefs.Name(card.Element)}牌 → {ElementDefs.Name(victimElement)}元素消失（相克）",
                });
            }
            else if (plan.TargetBecomes != victimElement)
            {
                // 相生：目标就地变成牌面元素，uid / 位置不变（表现层只需要换颜色和字）
                victim.Element = plan.TargetBecomes;

                TotalTransformed++;
                _converted[victimElement] = ConvertedOf(victimElement) + 1;

                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Transform,
                    From = target,
                    To = target,
                    Element = victimElement,
                    Uid = victim.Uid,
                    Text = $"打出{ElementDefs.Name(card.Element)}牌 → "
                           + $"{ElementDefs.Name(victimElement)}变为{ElementDefs.Name(plan.TargetBecomes)}（相生）",
                });
            }

            // 相生的「+1」：在目标旁边多出一只赢家元素
            if (plan.Spawn == null) return;

            SpawnCreep(plan.Spawn.Value, SpawnCellFor(target), record, target);
        }

        /// <summary>这次出牌不合法时，给玩家一句能看懂的提示（列出这张目标格真正能用的牌）。</summary>
        string BuildFailReason(Creep victim, Card card)
        {
            if (Rules.CardEffect != CardEffectMode.CardAsVirtualElement)
                return $"{ElementDefs.Name(card.Element)}牌只能作用于{ElementDefs.Name(card.Element)}元素，目标格是{ElementDefs.Name(victim.Element)}";

            // 反推：哪些牌打在这个目标上真的会发生事情
            var valid = new List<string>();
            foreach (var e in ElementDefs.All)
            {
                if (e == victim.Element) continue; // 同元素之间不发生反应，不能出牌

                var d = Reactions.Get(victim.Element, e);
                if (d.Rule == ReactionRule.NoReaction) continue;

                var cardWins = d.Winner == e;
                if (d.Rule == ReactionRule.Annihilate && !cardWins) continue; // 相克里牌面元素输 → 打出去没变化

                string effect;
                if (d.Rule == ReactionRule.Annihilate)
                    effect = $"{ElementDefs.Name(victim.Element)}直接消失";
                else if (d.Rule == ReactionRule.BothVanish)
                    effect = "双方都消失";
                else if (cardWins)
                    effect = $"{ElementDefs.Name(victim.Element)}变为{ElementDefs.Name(e)}，并生成 1 个{ElementDefs.Name(e)}";
                else
                    effect = $"目标不变，生成 1 个{ElementDefs.Name(victim.Element)}";

                valid.Add($"{ElementDefs.Name(e)}牌 → {effect}");
            }

            if (valid.Count == 0)
                return $"没有能作用于{ElementDefs.Name(victim.Element)}的牌";

            return $"这张牌打在该目标上不构成反应；可用的牌（{ElementDefs.Name(victim.Element)}目标）：{string.Join("、", valid)}";
        }

        public string FailReason { get; private set; } = null;

        public Card FindCard(int id)
        {
            foreach (var c in _hand)
                if (c.Id == id) return c;
            return null;
        }

        /// <summary>
        /// 从本关的**牌池**（Rules.CardPool）里随机取一种元素（元素牌用）。
        /// 池子空的情况下 Validate 已经把它修成「全部」，所以这里拿得到东西。
        /// </summary>
        Element RandomElement()
        {
            var pool = Rules.PoolCards;
            if (pool.Length == 0) return ElementDefs.All[0];
            return pool[NextRandom(pool.Length)];
        }

        /// <summary>
        /// 抽一张牌：先按**权重**决定这一张是元素牌还是哪种秘术牌，再决定具体内容。
        /// 这是「秘术牌比例可控」的实现点 —— 权重在 <see cref="Rules"/> 上（元素/融合/分解各一个）。
        /// 元素牌池为空时秘术牌仍然照常抽得出来（反之亦然）。
        ///
        /// 秘术牌还有一道**回合门**：<see cref="Rules.SpecialCardUnlockRound"/> 之前的回合
        /// 只发元素牌（教学关用「第 1 回合先只看，第 2 回合再给分解牌」就是这个）。
        /// </summary>
        Card DrawRandomCard()
        {
            var specials = Rules.SpecialCardsAvailableAtRound(Round)
                ? Rules.DrawableSpecialCards()
                : NoSpecialCards;
            bool hasElements = Rules.PoolCards.Length > 0 && Rules.ElementCardWeight > 0;

            int elementWeight = hasElements ? Math.Max(0, Rules.ElementCardWeight) : 0;
            int total = elementWeight;
            for (int i = 0; i < specials.Length; i++) total += WeightOf(specials[i]);

            if (total <= 0)
            {
                // 牌堆里一张能抽的牌都没有（配置错到极点）：退回原来的行为，至少不会崩。
                return DrawCard(RandomElement());
            }

            int roll = NextRandom(total);
            if (roll < elementWeight) return DrawCard(RandomElement());

            roll -= elementWeight;
            for (int i = 0; i < specials.Length; i++)
            {
                int w = WeightOf(specials[i]);
                if (roll < w) return DrawSpecialCard(specials[i]);
                roll -= w;
            }

            // 理论上到不了这里；兜底给最后一种秘术牌。
            return DrawSpecialCard(specials[specials.Length - 1]);
        }

        /// <summary>一种秘术牌的抽牌权重。</summary>
        int WeightOf(CardKind kind) => Rules.DrawWeightOf(kind);

        Card DrawCard(Element element)
        {
            var card = new Card(_nextCardId++, element);
            _hand.Add(card);
            return card;
        }

        /// <summary>
        /// 抽一张指定的**元素牌**（机制给牌、测试摆手牌都走它）。
        /// ⚠ 与 <see cref="DrawSpecialCard"/> 分开命名：枚举之间可以隐式转换，
        /// 两个同名的 `DrawCard` 重载会互相抢。
        /// </summary>
        public Card DrawElementCard(Element element) => DrawCard(element);

        /// <summary>
        /// 抽一张指定的**秘术牌**（机制给牌、测试摆手牌都走它）。
        ///
        /// ⚠ 方法名故意不叫 `DrawCard(CardKind)`：C# 的枚举之间可以隐式转换，
        /// `DrawCard(Element)` 与 `DrawCard(CardKind)` 两个重载会互相抢，
        /// `DrawCard(CardKind.DecomposeCard)` 有被解析成 `DrawCard(Element.Earth)` 的风险。
        /// 名字分开就不会踩这个坑（同理也没有 `ForceHandForTest(IEnumerable&lt;CardKind&gt;)`）。
        /// </summary>
        public Card DrawSpecialCard(CardKind kind)
        {
            var card = new Card(_nextCardId++, kind);
            _hand.Add(card);
            return card;
        }

        /// <summary>玩家点击「结束出牌」（进入弃牌阶段）。</summary>
        public void EndPlayerPhase()
        {
            if (Phase != GamePhase.PlayerAction) return;
            PlayerPhaseEnded = true;
            SetPhase(GamePhase.Discard);
            PushEvent(new GameEvent
            {
                Type = GameEventType.Message,
                Text = $"弃牌阶段：最多可弃 {Rules.MaxDiscardsPerRound} 张，手牌 {_hand.Count} / 上限 {Rules.HandLimit}",
            });
        }

        /// <summary>
        /// 弃牌阶段：弃掉一张手牌。返回 false 表示这次弃牌不合法
        /// （不在弃牌阶段 / 没这张牌 / 本回合弃牌次数用完）。
        /// 图1 写「可以全部弃完」，所以不强制弃到上限。
        /// </summary>
        public bool TryDiscardCard(int cardId)
        {
            FailReason = null;

            if (Phase != GamePhase.Discard)
            {
                FailReason = "当前不是弃牌阶段";
                return false;
            }

            if (_discardsUsed >= Rules.MaxDiscardsPerRound)
            {
                FailReason = $"本回合已经弃了 {_discardsUsed} 张（上限 {Rules.MaxDiscardsPerRound}）";
                return false;
            }

            var card = FindCard(cardId);
            if (card == null)
            {
                FailReason = "手牌里没有这张牌";
                return false;
            }

            _hand.Remove(card);
            _discardsUsed++;

            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsDrawPhase = false,
                IsPlayerCard = false,
                Element = card.Element,
            };
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.DiscardCard,
                Element = card.Element,
                Text = $"弃掉 {card.Label}（本回合 {_discardsUsed}/{Rules.MaxDiscardsPerRound}）",
            });
            record.Events[0].StepIndex = record.Index;
            PushEvent(record.Events[0]);
            Enqueue(record);
            return true;
        }

        /// <summary>玩家点击「结束弃牌」（进入判定阶段）。</summary>
        public void EndDiscardPhase()
        {
            if (Phase != GamePhase.Discard) return;
            DiscardPhaseEnded = true;
            SetPhase(GamePhase.Judgment);
        }

        // ------------------------------------------------------------ 判定阶段

        /// <summary>这张牌能不能打在这只小怪上（表现层用它做高亮提示，测试用它做断言）。</summary>
        public bool CanPlayCardOn(Card card, Creep target)
        {
            if (card == null || target == null || !target.Alive) return false;
            return PlanCardEffect(target, card) != null;
        }

        /// <summary>
        /// 把当前回合一次性跑完（准备 → 抽牌 → 行动 → 弃牌 → 判定），不经过表现层。
        /// 批量跑批 / 编辑器里快速验证规则时用。注意：它不会出牌，也不弃牌。
        /// </summary>
        public void SimulateRound()
        {
            int guard = 0;
            int limit = Rules.CellCount * 4 + 8;
            while (Phase == GamePhase.Preparation && guard++ < limit) AdvancePreparation();
            if (Phase == GamePhase.PlayerAction) EndPlayerPhase();
            if (Phase == GamePhase.Discard) EndDiscardPhase();
            ResolveJudgment();
        }

        /// <summary>
        /// 在判定阶段结算胜负。返回 true 表示本局仍在继续（已进入下一回合准备阶段）。
        /// 回合上限按图1 的原文「回合数达到回合上限 15」判定：第 15 回合结束仍未胜利即失败。
        /// </summary>
        public bool ResolveJudgment()
        {
            if (Phase != GamePhase.Judgment) return false;

            // 「本回合」到这里结束：先让事件撤销它的临时效果（例如瓶子容量改回本关的值），再判胜负。
            RevertRoundEvents();

            if (CheckWinCondition(out var reason))
            {
                SetPhase(GamePhase.Victory);
                PushEvent(new GameEvent { Type = GameEventType.Win, Text = reason });
                return false;
            }

            // 关卡机制可以提前判负（例如「水元素被杀光就输」），比回合上限更早生效。
            if (CheckExtraLose(out var loseReason))
            {
                SetPhase(GamePhase.Defeat);
                PushEvent(new GameEvent { Type = GameEventType.Lose, Text = loseReason });
                return false;
            }

            if (Round >= Rules.RoundLimit)
            {
                DefeatByRoundLimit = true;
                SetPhase(GamePhase.Defeat);
                PushEvent(new GameEvent { Type = GameEventType.Lose, Text = $"回合数达到回合上限 {Rules.RoundLimit}，游戏失败" });
                return false;
            }

            PushEvent(new GameEvent
            {
                Type = GameEventType.Message,
                Text = $"未达成胜利条件（{WinConditionDescription()}），进入下一回合",
            });
            BeginRound();
            return true;
        }

        /// <summary>一步到位：结束玩家阶段 → 判定。方便测试与快速跑批。</summary>
        public bool FinishRound()
        {
            if (Phase == GamePhase.PlayerAction) EndPlayerPhase();
            return ResolveJudgment();
        }

        /// <summary>
        /// 判定胜负（内置目标 + 关卡机制的额外条件）。
        ///
        /// 目标和机制的关系：
        ///   · 目标是 Custom → 内置判定**完全不参与**，胜负只由 ILevelRule 决定。
        ///     （不这么做的话，一个自定义目标的关卡会因为 Goal 字段的默认值「只剩水元素」而提前获胜。）
        ///   · 其余目标     → 内置目标先判；不满足时才问机制，也就是机制只能「加赢」。
        /// </summary>
        public bool CheckWinCondition(out string reason)
        {
            reason = null;

            if (Rules.Goal.Kind != GoalKind.Custom && CheckBuiltInGoal(out reason)) return true;
            if (CheckExtraWin(out reason)) return true;

            reason = null;
            return false;
        }

        bool CheckBuiltInGoal(out string reason)
        {
            reason = null;

            switch (Rules.Goal.Kind)
            {
                case GoalKind.WaterOnly:
                {
                    int water = Board.WaterCount();
                    int total = Board.CreepCount();
                    if (total > 0 && water == total)
                    {
                        reason = $"胜利：全场只剩水元素（{water} 个）";
                        return true;
                    }
                    return false;
                }

                case GoalKind.SingleElement:
                {
                    // 全场只剩一种元素（空场也算，即清空）
                    int distinct = Board.DistinctElementCount();
                    if (distinct <= 1)
                    {
                        reason = distinct == 0
                            ? "胜利：全场元素已清空"
                            : $"胜利：全场只剩{ElementDefs.Name(Board.FirstElementOrDefault())}元素";
                        return true;
                    }
                    return false;
                }

                case GoalKind.ClearBoard:
                    if (Board.CreepCount() == 0)
                    {
                        reason = "胜利：全场元素已清空";
                        return true;
                    }
                    return false;

                case GoalKind.SurviveRounds:
                    if (Round >= Rules.Goal.Rounds)
                    {
                        reason = $"胜利：撑过了 {Rules.Goal.Rounds} 回合";
                        return true;
                    }
                    return false;

                case GoalKind.ElementAtMost:
                {
                    int count = Board.CountOf(Rules.Goal.Target);
                    if (count <= Rules.Goal.TargetCount)
                    {
                        reason = $"胜利：场上{ElementDefs.Name(Rules.Goal.Target)}元素只剩 {count} 个（≤ {Rules.Goal.TargetCount}）";
                        return true;
                    }
                    return false;
                }

                case GoalKind.ElementAtLeast:
                {
                    int count = Board.CountOf(Rules.Goal.Target);
                    if (count >= Rules.Goal.TargetCount)
                    {
                        reason = $"胜利：场上{ElementDefs.Name(Rules.Goal.Target)}元素达到 {count} 个（≥ {Rules.Goal.TargetCount}）";
                        return true;
                    }
                    return false;
                }

                default:
                    return false;
            }
        }

        public string WinConditionDescription()
        {
            if (Rules.Goal.Kind != GoalKind.Custom) return Rules.Goal.Describe();
            return CustomGoalDescription() ?? "自定义目标（本关没有挂机制组件）";
        }

        /// <summary>给 HUD 画进度条用的目标进度（描述 + 填充比例 0~1 + 一行文字）。</summary>
        public GoalProgress GetGoalProgress()
        {
            if (Rules.Goal.Kind == GoalKind.Custom) return CustomGoalProgress();

            switch (Rules.Goal.Kind)
            {
                case GoalKind.WaterOnly:
                {
                    int water = Board.WaterCount();
                    int total = Board.CreepCount();
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = total == 0 ? 0f : Clamp01((float)water / total),
                        Label = $"水 {water} / 场上 {total}",
                    };
                }

                case GoalKind.SingleElement:
                {
                    int distinct = Board.DistinctElementCount();
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = Clamp01(1f - (distinct - 1) / 4f),
                        Label = $"元素种类 {distinct} / 5",
                    };
                }

                case GoalKind.ClearBoard:
                {
                    int left = Board.CreepCount();
                    float max = Math.Max(1, Rules.CellCount);
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = Clamp01(1f - left / max),
                        Label = $"剩余 {left} 个",
                    };
                }

                case GoalKind.SurviveRounds:
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = Rules.Goal.Rounds <= 0 ? 0f : Clamp01((float)Round / Rules.Goal.Rounds),
                        Label = $"第 {Round} / {Rules.Goal.Rounds} 回合",
                    };

                case GoalKind.ElementAtMost:
                {
                    int count = Board.CountOf(Rules.Goal.Target);
                    float max = Math.Max(1, Rules.Goal.TargetCount);
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = Clamp01(1f - count / max),
                        Label = $"{ElementDefs.Name(Rules.Goal.Target)} {count} / ≤{Rules.Goal.TargetCount}",
                    };
                }

                case GoalKind.ElementAtLeast:
                {
                    int count = Board.CountOf(Rules.Goal.Target);
                    float target = Math.Max(1, Rules.Goal.TargetCount);
                    return new GoalProgress
                    {
                        Description = Rules.Goal.Describe(),
                        Fill = Clamp01(count / target),
                        Label = $"{ElementDefs.Name(Rules.Goal.Target)} {count} / ≥{Rules.Goal.TargetCount}",
                    };
                }

                default:
                    return new GoalProgress { Description = Rules.Goal.Describe(), Fill = 0f, Label = "" };
            }
        }

        /// <summary>
        /// 自定义目标的进度条：把所有给出了进度（Label 非空）的机制**合起来**报。
        ///
        /// 为什么要合：第二~四章有一批「A 且 B」的关卡（比如「金瓶 10 张 + 木瓶 10 张」），
        /// 只报第一条的话，玩家永远看不到第二条差多少 —— 明明还差 8 张，进度条却已经满了。
        ///
        /// · 说明文字：一律用 <see cref="WinConditionDescription"/>（它会把**所有**机制的描述连起来）——
        ///   以前这里取「第一条机制的描述」，于是多目标关卡在 HUD 上只显示一个目标（测试反馈过）
        /// · 数值：各条 Label 用全角空格连起来（窄屏会被裁掉，所以顺序 = 预制体上的挂载顺序，越靠前越重要）
        /// · 填充：<see cref="Rules.RequireAllWinRules"/> 为真（且）时取**最差**的那一条，
        ///   否则（或）取**最好**的那一条 —— 两种语义下「进度条满了 = 要赢了」都成立。
        /// 机制没做进度条就退化成「只有描述、不填充」。
        /// </summary>
        GoalProgress CustomGoalProgress()
        {
            var labels = new List<string>();
            float best = 0f;
            float worst = 1f;

            foreach (var rule in _levelRules)
            {
                if (rule == null) continue;

                GoalProgress progress;
                try
                {
                    progress = rule.GetGoalProgress(this);
                }
                catch (Exception)
                {
                    continue; // 进度条不值得为它停掉一局，异常留给判定钩子那套去报
                }

                if (string.IsNullOrEmpty(progress.Label)) continue;

                labels.Add(progress.Label);

                float fill = Clamp01(progress.Fill);
                if (fill > best) best = fill;
                if (fill < worst) worst = fill;
            }

            if (labels.Count == 0)
                return new GoalProgress { Description = WinConditionDescription(), Fill = 0f, Label = "" };

            return new GoalProgress
            {
                Description = WinConditionDescription(),
                Fill = Rules.RequireAllWinRules ? worst : best,
                Label = string.Join("　", labels),
            };
        }


        // ------------------------------------------------------------ 事件/回放

        void PushEvent(GameEvent e)
        {
            e.Round = Round;
            _log.Add(e);
        }

        void Enqueue(StepRecord record)
        {
            _steps.Enqueue(record);
            _history.Add(record);
        }

        public StepRecord PeekNextStep() => _steps.Count > 0 ? _steps.Peek() : null;

        public StepRecord DequeueStep() => _steps.Count > 0 ? _steps.Dequeue() : null;

        public int PendingStepCount => _steps.Count;

        public IReadOnlyList<StepRecord> History => _history;

        /// <summary>回合流程是否已经走到终点（胜负已定）。</summary>
        public bool IsOver => Phase == GamePhase.Victory || Phase == GamePhase.Defeat;

        /// <summary>胜负刚出来的那一帧通知表现层（只报一次）。</summary>
        public bool TryTakeResult(out GamePhase phase, out string message)
        {
            phase = Phase;
            message = null;
            if (ResultReported) return false;
            if (Phase != GamePhase.Victory && Phase != GamePhase.Defeat) return false;

            ResultReported = true;
            for (int i = _log.Count - 1; i >= 0; i--)
            {
                if (_log[i].Type == GameEventType.Win || _log[i].Type == GameEventType.Lose)
                {
                    message = _log[i].Text;
                    break;
                }
            }
            return true;
        }

        public string StatusLine()
        {
            var parts = new List<string>();
            foreach (var e in ElementDefs.All)
                parts.Add($"{ElementDefs.Name(e)}{Board.CountOf(e)}");

            // 复合元素不属于五行，单独列出来（不然 HUD 上会「凭空少了几格」）
            int composites = Board.CompositeCount();
            if (composites > 0) parts.Add($"复合{composites}");

            return string.Join(" ", parts);
        }
    }
}
