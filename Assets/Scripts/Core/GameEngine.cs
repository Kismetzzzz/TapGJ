using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>玩家手里的一张元素牌。牌面元素 = 打出后能与场上哪个元素发生反应。</summary>
    public sealed class Card
    {
        public int Id { get; }
        public Element Element { get; }

        internal Card(int id, Element element)
        {
            Id = id;
            Element = element;
        }

        public string Label => $"{ElementDefs.Name(Element)}牌";

        public override string ToString() => $"#{Id}{Label}";
    }

    /// <summary>
    /// 元素小瓶子：与五行元素一一对应，贯穿整个流程。
    /// 场上每消失一个某元素的小怪，对应瓶子 +1；满瓶（上限 5）自动产出一张对应元素牌并清空。
    /// </summary>
    public sealed class Bottle
    {
        public Element Element { get; }
        readonly int[] _counts = new int[ElementDefs.Count];
        public int Capacity { get; }

        public int TotalCardsGranted { get; private set; }

        internal Bottle(int capacity)
        {
            Capacity = capacity;
        }

        public int Count(Element e) => _counts[(int)e];

        /// <summary>计数 +1，返回是否满瓶产出卡牌。</summary>
        internal bool Add(Element e)
        {
            _counts[(int)e]++;
            if (_counts[(int)e] < Capacity) return false;
            _counts[(int)e] = 0;
            TotalCardsGranted++;
            return true;
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
    ///   玩家行动  → 小怪静止，玩家打出元素牌，与场上同元素小怪发生反应，自行结束回合
    ///   判定阶段  → 判胜负，未达成则进入下一回合
    /// </summary>
    public sealed partial class GameEngine
    {
        public Rules Rules { get; }
        public Board Board { get; }
        public ReactionTable Reactions { get; }
        public Bottle Bottle { get; }

        readonly Random _rng;
        readonly List<Card> _hand = new List<Card>();
        readonly Dictionary<int, Creep> _creeps = new Dictionary<int, Creep>();
        readonly Queue<StepRecord> _steps = new Queue<StepRecord>();
        readonly List<StepRecord> _history = new List<StepRecord>();
        readonly List<GameEvent> _log = new List<GameEvent>();
        readonly Dictionary<Element, int> _removed = new Dictionary<Element, int>();
        readonly Dictionary<Element, int> _spawned = new Dictionary<Element, int>();

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

        /// <summary>本局累计各元素被反应消耗 / 生成的数量，用于观察平衡性。</summary>
        public IReadOnlyDictionary<Element, int> RemovedCounts => _removed;
        public IReadOnlyDictionary<Element, int> SpawnedCounts => _spawned;
        public int TotalRemoved { get; private set; }
        public int TotalSpawned { get; private set; }

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
        public void Start()
        {
            Board.ClearAll();
            _creeps.Clear();
            _hand.Clear();
            _steps.Clear();
            _history.Clear();
            _log.Clear();
            _removed.Clear();
            _spawned.Clear();
            _nextUid = 1;
            _nextCardId = 1;
            _movedThisRound.Clear();
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
            SpawnBlockedCount = 0;

            // 均匀生成：把 64 格洗牌，前 20 格按「每种元素 4 个」铺开。
            var cells = new List<Pos>(Rules.CellCount);
            for (int y = 0; y < Board.Height; y++)
                for (int x = 0; x < Board.Width; x++)
                    cells.Add(new Pos(x, y));
            Board.Shuffle(cells, _rng);

            int index = 0;
            foreach (var element in ElementDefs.All)
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

            BeginRound();
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
            PushEvent(new GameEvent { Type = GameEventType.Message, Text = $"—— 第 {Round} 回合：准备阶段 ——" });
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
        /// 推进一个准备阶段步骤（一只小怪的移动决策）。
        /// 本回合所有小怪都走完后，本次调用会自动把阶段切到「玩家行动阶段」并返回 null。
        /// </summary>
        public StepRecord AdvancePreparation()
        {
            if (Phase != GamePhase.Preparation) return null;

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
        /// 返回本次抽到的牌数（0 表示本来就满了）。
        /// </summary>
        public int ResolveDrawPhase()
        {
            int before = _hand.Count;
            int target = Rules.HandLimit;
            var drawn = new List<Card>();

            while (_hand.Count < target)
            {
                var card = DrawCard(RandomElement());
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
                if (!Board.InBounds(next)) break; // 撞到棋盘边界，本次移动结束

                var other = Board.At(next);
                if (other != null && other.Alive)
                {
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
                        // 不同元素相遇：发生反应（撞上去的一方可能被消耗掉，那就不再往下走）
                        ResolveEncounter(creep, other, next, record);
                        if (!creep.Alive)
                        {
                            record.To = record.Path.Count > 0 ? record.Path[record.Path.Count - 1] : creep.Pos;
                            break;
                        }
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
        ///   def.Spawn 就是这一对的赢家 —— 它留下并「+1」（在相邻空格生成一个新的自己），
        ///   另一方（输家）从棋盘上消失。输家可能是主动撞上去的，也可能是被撞上的，
        ///   由表决定，不能写死。
        /// 当前 10 对跨元素组合都有明确赢家，所以走不到 BothVanish 分支；
        /// 保留它是为了以后能配出「湮灭」型规则。
        /// </summary>
        void ResolveEncounter(Creep trigger, Creep target, Pos meetingPoint, StepRecord record)
        {
            var def = Reactions.Get(trigger.Element, target.Element);

            if (def.Rule == ReactionRule.NoReaction)
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Bump,
                    From = trigger.Pos,
                    To = meetingPoint,
                    Element = trigger.Element,
                    Uid = trigger.Uid,
                    Text = $"同种{ElementDefs.Name(trigger.Element)}相遇，禁止移动",
                });
                return;
            }

            if (def.Rule == ReactionRule.BothVanish)
            {
                // 可选规则：双方都消失，没有任何赢家，也不生成新元素
                RemoveCreep(trigger, record, meetingPoint);
                RemoveCreep(target, record, meetingPoint);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = meetingPoint,
                    Element = trigger.Element,
                    Uid = trigger.Uid,
                    Text = $"{ElementDefs.Name(trigger.Element)} × {ElementDefs.Name(target.Element)} → 双方都消失",
                });
                return;
            }

            // 赢家是 trigger 还是 target，由表决定
            var winnerIsTrigger = def.Spawn == trigger.Element;
            var loser = winnerIsTrigger ? target : trigger;

            RemoveCreep(loser, record, meetingPoint);
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Vanish,
                From = meetingPoint,
                Element = loser.Element,
                Uid = loser.Uid,
                Text = $"{ElementDefs.Name(trigger.Element)} × {ElementDefs.Name(target.Element)} → "
                      + $"{ElementDefs.Name(loser.Element)}消失，{ElementDefs.Name(def.Spawn)}+1",
            });

            // 赢家是撞上去的那一方时，它顺势占住让出来的那一格（等于往前走了一步）
            if (winnerIsTrigger && Board.IsEmpty(meetingPoint))
            {
                Board.Clear(trigger.Pos);
                trigger.Pos = meetingPoint;
                Board.Put(trigger);
                record.Path.Add(meetingPoint);
                record.To = meetingPoint;
            }

            SpawnCreep(def.Spawn, SpawnCellFor(meetingPoint), record, meetingPoint);
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

            var creep = new Creep(_nextUid++, element, cell);
            _creeps[creep.Uid] = creep;
            Board.Put(creep);

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

        void RemoveCreep(Creep creep, StepRecord record, Pos where)
        {
            if (!creep.Alive) return;

            creep.Alive = false;
            Board.Clear(creep.Pos);
            _creeps.Remove(creep.Uid);

            // 场上消失一个元素 → 对应瓶子 +1；满瓶自动产出一张对应元素牌。
            if (Bottle.Add(creep.Element))
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.GainCard,
                    Element = creep.Element,
                    Text = $"{ElementDefs.Name(creep.Element)}瓶子满，获得 1 张{ElementDefs.Name(creep.Element)}牌",
                });
                DrawCard(creep.Element);
            }

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.BottleFill,
                From = where,
                Element = creep.Element,
                Text = $"{ElementDefs.Name(creep.Element)}瓶 +1",
            });

            TotalRemoved++;
            _removed[creep.Element] = RemovedCounts.TryGetValue(creep.Element, out var n) ? n + 1 : 1;
        }

        // -------------------------------------------------------- 玩家行动阶段

        /// <summary>
        /// 打出一张元素牌（默认 CardEffectMode.CardAsVirtualElement）：
        /// 牌面元素被当作一个「虚拟小怪」，与目标格上的元素按图2 的表发生反应，
        /// 消失/生成的结果直接作用在棋盘上（棋盘元素计数变化）。
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

            var victim = Board.At(target);
            if (victim == null)
            {
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
                CreepWentAway = true,
            };

            _hand.Remove(card);
            _cardsPlayedThisRound++;
            ApplyCardEffect(plan.Value, victim, card, target, record);

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);
            return record;
        }

        struct CardPlan
        {
            public Creep Loser;
            public Element? Spawn;
        }

        /// <summary>
        /// 算出这次出牌会让谁消失、生成什么；返回 null 表示这次出牌不合法。
        /// 牌 = 一个「虚拟元素」，主动撞上目标格上的元素，因此按图2 的表：
        ///   目标元素的「遇到 牌面元素」那一行决定结果 —— 目标消失。
        ///   SpawnOne(winner) 时，若 winner 就是牌面元素，则「牌+1」= 生成一个新的牌面元素；
        ///   若 winner 是目标元素，则这次出牌只是把它清掉（没有额外生成），牌被消耗。
        /// </summary>
        CardPlan? PlanCardEffect(Creep victim, Card card)
        {
            switch (Rules.CardEffect)
            {
                case CardEffectMode.SameElementOnly:
                    if (victim.Element != card.Element) return null;
                    return new CardPlan { Loser = victim, Spawn = null };

                case CardEffectMode.SameElementSpawnOne:
                    if (victim.Element != card.Element) return null;
                    return new CardPlan { Loser = victim, Spawn = victim.Element };

                default: // CardAsVirtualElement
                {
                    if (victim.Element == card.Element) return null; // 同元素之间不发生反应

                    var def = Reactions.Get(victim.Element, card.Element);
                    if (def.Rule == ReactionRule.NoReaction) return null;

                    // 牌面元素主动撞上目标：目标消失；若赢家就是牌面元素，就在旁边生成一个新的牌面元素。
                    // （10 对跨元素组合里牌面元素永远是赢家，所以正常都会生成；这里仍按表判断，
                    //   以后把某对改成「双方都消失」时不用动这段。）
                    var spawn = def.CreatesCreep && def.Spawn == card.Element ? card.Element : (Element?)null;
                    return new CardPlan { Loser = victim, Spawn = spawn };
                }
            }
        }

        void ApplyCardEffect(CardPlan plan, Creep victim, Card card, Pos target, StepRecord record)
        {
            RemoveCreep(plan.Loser, record, target);
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Vanish,
                From = target,
                Element = plan.Loser.Element,
                Uid = plan.Loser.Uid,
                Text = $"打出{ElementDefs.Name(card.Element)}牌 → {ElementDefs.Name(plan.Loser.Element)}元素消失",
            });

            if (plan.Spawn == null) return;

            var spawnCell = SpawnCellFor(target);
            SpawnCreep(plan.Spawn.Value, spawnCell, record, target);
        }

        /// <summary>这次出牌不合法时，给玩家一句能看懂的提示（含这张目标格可用的牌）。</summary>
        string BuildFailReason(Creep victim, Card card)
        {
            if (Rules.CardEffect != CardEffectMode.CardAsVirtualElement)
                return $"{ElementDefs.Name(card.Element)}牌只能作用于{ElementDefs.Name(card.Element)}元素，目标格是{ElementDefs.Name(victim.Element)}";

            // 反推：哪些牌能打在这个目标上
            var valid = new List<string>();
            foreach (var e in ElementDefs.All)
            {
                if (e == victim.Element) continue; // 同元素之间不发生反应，不能出牌
                var d = Reactions.Get(victim.Element, e);
                if (d.Rule == ReactionRule.NoReaction) continue;
                valid.Add($"{ElementDefs.Name(e)}牌 → {ElementDefs.Name(victim.Element)}消失并生成 1 个{ElementDefs.Name(d.Spawn)}");
            }

            return $"这张牌打在该目标上不构成反应；可用的牌（{ElementDefs.Name(victim.Element)}目标）：{string.Join("、", valid)}";
        }

        public string FailReason { get; private set; } = null;

        public Card FindCard(int id)
        {
            foreach (var c in _hand)
                if (c.Id == id) return c;
            return null;
        }

        /// <summary>从五行牌堆里随机取一种元素（抽牌阶段与初始手牌都用它）。</summary>
        Element RandomElement() => ElementDefs.All[NextRandom(ElementDefs.Count)];

        Card DrawRandomCard() => DrawCard(RandomElement());

        Card DrawCard(Element element)
        {
            var card = new Card(_nextCardId++, element);
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

            if (CheckWinCondition(out var reason))
            {
                SetPhase(GamePhase.Victory);
                PushEvent(new GameEvent { Type = GameEventType.Win, Text = reason });
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

        public bool CheckWinCondition(out string reason)
        {
            reason = null;

            if (Rules.WinCondition == WinCondition.WaterOnly)
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

            // SingleElement：全场只剩一种元素（含空场）
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

        public string WinConditionDescription() => DescribeWinCondition(Rules.WinCondition);

        /// <summary>静态版本：不需要实例就能拿到文案（编辑器窗口用）。</summary>
        public static string DescribeWinCondition(WinCondition condition)
        {
            switch (condition)
            {
                case WinCondition.WaterOnly: return "全场只剩水元素";
                case WinCondition.SingleElement: return "全场只剩一种元素";
                default: return condition.ToString();
            }
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
            return string.Join(" ", parts);
        }
    }
}
