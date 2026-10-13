using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 秘术牌与复合元素（图2 上半部分那张表）的实现。
    ///
    /// 玩法约定（每一条都对应一个测试）：
    ///   · **融合牌**：先点融合牌，再点场上**相邻**的两个基础元素 → 在**第一个**元素的位置生成复合元素，
    ///     两个基础元素都被消耗掉（不算「相克消失」，所以**不喂小瓶子**）。
    ///   · **融合的前提**：场上**没有任何复合元素**。一旦场上已经有复合元素，融合牌就用不出去
    ///     （「如果开局场上不存在复合元素则必须通过融合牌来获得复合元素」的反面）。
    ///   · **分解牌**：点场上的复合元素 → 它从棋盘上消失，并给回两张**对应元素的元素牌**
    ///     （不喂瓶子；分解牌本身不占「每回合出牌上限」，它是一张不产出元素的解场牌）。
    ///   · **复合元素产生效果**：回合开始时，在自己的**范围内**生成两种组成元素各
    ///     <see cref="Rules.CompositeIngredientsPerRound"/> 份；融合当回合立刻先生效一次。
    ///   · 复合元素**不会移动**、**不受元素反应影响**，只占格子。
    ///
    /// 分成 partial 是为了让 GameEngine.cs 只保留原有主流程。
    /// </summary>
    public sealed partial class GameEngine
    {
        readonly Dictionary<int, Composite> _composites = new Dictionary<int, Composite>();

        /// <summary>本回合已经「产生」过的复合元素。每回合清空 —— 每只每回合产生一次。</summary>
        readonly HashSet<int> _compositeProducedThisRound = new HashSet<int>();

        /// <summary>本回合复合元素已经产生的元素份数（HUD/调试用）。</summary>
        int _compositeProducedCountThisRound;

        /// <summary>
        /// 本次「产生」里已经被占掉的格子。
        ///
        /// 有了它，同一批产出**不会互相踩格**（不然随机交错时，后面产出的元素会落到前面刚产出的
        /// 那一格上，就地反应掉 —— 表现就是「说好各 4 份，最后只看到 4 份」）。
        /// 每次 <see cref="EstablishComposite"/> 开头清空。
        /// </summary>
        readonly HashSet<Pos> _reservedThisProduction = new HashSet<Pos>();

        // ---------------------------------------------------------------- 状态

        /// <summary>棋盘上的复合元素。</summary>
        public IEnumerable<Composite> Composites => _composites.Values;

        public int CompositeCount => Board.CompositeCount();

        /// <summary>某格上的复合元素（没有就返回 null）。</summary>
        public Composite CompositeAt(Pos pos) => Board.CompositeAt(pos);

        /// <summary>本回合复合元素一共生产了多少份元素。</summary>
        public int CompositeProducedThisRound => _compositeProducedCountThisRound;

        /// <summary>
        /// 现在能不能用融合牌。取决于 <see cref="Rules.AllowMultipleComposites"/>：
        ///   · 关（默认）：场上必须**一个复合元素都没有**
        ///   · 开：只要场上没有**同一种**复合元素就行（具体看 <see cref="CanFuseInto"/>）
        ///
        /// 这是设计原文「如果开局场上不存在复合元素则必须通过融合牌来获得复合元素」的反面读法。
        /// ⚠ 这里**不是数量上限** —— 上限只来自这条前置条件，用 <see cref="Rules.InitialComposites"/>
        /// 开局摆几个都不受它管。
        /// </summary>
        public bool CanFuseNow => Rules.AllowMultipleComposites || CompositeCount == 0;

        /// <summary>
        /// 能不能再合出一个 <paramref name="kind"/> 的复合元素：
        /// 「只允许一个」模式下要求场上空着；「允许多个」模式下要求场上没有**同种**的。
        /// </summary>
        public bool CanFuseInto(CompositeKind kind)
        {
            if (!Rules.AllowMultipleComposites) return CompositeCount == 0;

            foreach (var c in Board.Composites())
                if (c.Kind == kind) return false;
            return true;
        }

        /// <summary>场上复合元素的名单（人话，提示文案用）。</summary>
        public string CompositeNames() => DescribeComposites();

        /// <summary>本关现在有没有复合元素（融合牌能不能用、分解牌有没有目标，都看它）。</summary>
        public bool HasComposite => CompositeCount > 0;

        // ---------------------------------------------------------------- 融合（融合牌）

        /// <summary>
        /// 十字（正交 4 邻格）偏移：上、右、下、左。顺序固定 → 提示文案与高亮顺序可复现。
        ///
        /// ⚠ **融合的相邻判定用的就是这一组**，不是周围 8 格：
        /// 图2 的示意图给的是「十字范围」，斜角不算相邻（斜着摆的两只元素合不了）。
        /// </summary>
        static readonly Pos[] CrossOffsets =
        {
            new Pos(0, 1), new Pos(1, 0), new Pos(0, -1), new Pos(-1, 0),
        };

        /// <summary>两只元素是否**正交相邻**（上下左右紧挨着）。斜角不算。</summary>
        public static bool IsOrthogonallyAdjacent(Pos a, Pos b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;

        /// <summary>
        /// 两只元素小怪能不能融合成复合元素。
        /// 条件：两只都活着、不是同一只、位置合法、两种元素在配方表里配得上（顺序无关）；
        /// 本关要求相邻时，还要**正交相邻**（十字 4 格 —— 斜角不算）。
        /// </summary>
        public bool CanFusePair(Creep a, Creep b)
        {
            if (a == null || b == null || a == b) return false;
            if (!a.Alive || !b.Alive) return false;
            if (!Board.InBounds(a.Pos) || !Board.InBounds(b.Pos)) return false;
            if (!FusionRecipes.CanFuse(a.Element, b.Element)) return false;

            if (Rules.FusionRequiresAdjacency && !IsOrthogonallyAdjacent(a.Pos, b.Pos)) return false;

            return true;
        }

        /// <summary>两只元素小怪能合成哪种复合元素（配不上时返回 false）。</summary>
        public bool TryGetFusionKind(Creep a, Creep b, out CompositeKind kind)
        {
            kind = default;
            if (!CanFusePair(a, b)) return false;
            return FusionRecipes.TryGetKind(a.Element, b.Element, out kind);
        }

        /// <summary>「元素1」能点的格子：格上有一只元素小怪，且它至少能和一个候选配成复合元素。</summary>
        public List<Pos> CellsForFusionFirst()
        {
            var result = new List<Pos>();
            if (!CanFuseNow) return result;

            foreach (var creep in Board.Creeps())
                if (CellsForFusionSecond(creep.Pos).Count > 0)
                    result.Add(creep.Pos);

            return result;
        }

        /// <summary>
        /// 「元素2」能点的格子：和 first 那一格上的元素配得上、且满足相邻要求的所有元素小怪。
        /// 融合牌点完元素1 之后，棋盘上高亮的就是这一批格子。
        /// </summary>
        public List<Pos> CellsForFusionSecond(Pos first)
        {
            var result = new List<Pos>();
            if (!CanFuseNow) return result;

            var anchor = Board.At(first);
            if (anchor == null || !anchor.Alive) return result;

            foreach (var other in CandidatesForSecond(anchor))
                if (CanFusePair(anchor, other)) result.Add(other.Pos);

            return result;
        }

        /// <summary>
        /// 元素2 的候选从哪儿来：要求相邻就只看**十字 4 格**（上下左右），否则看全场。
        /// 顺序固定（上右下左），保证同一个种子/同一局面下高亮顺序一致。
        /// </summary>
        IEnumerable<Creep> CandidatesForSecond(Creep anchor)
        {
            if (!Rules.FusionRequiresAdjacency) return Board.Creeps();

            var near = new List<Creep>(CrossOffsets.Length);
            foreach (var offset in CrossOffsets)
            {
                var creep = Board.At(new Pos(anchor.Pos.X + offset.X, anchor.Pos.Y + offset.Y));
                if (creep != null && creep.Alive) near.Add(creep);
            }
            return near;
        }

        /// <summary>
        /// 打出一张融合牌：把 <paramref name="first"/> 和 <paramref name="second"/> 两只元素小怪
        /// 合成复合元素，放在 **first 的位置**。失败原因见 <see cref="FailReason"/>。
        /// 成功时返回的步骤里，复合元素的出现和它**当回合的那一次产生**都在里面（一次出牌播完）。
        /// </summary>
        public StepRecord TryPlayFusionCard(int cardId, Pos first, Pos second)
        {
            FailReason = null;

            if (!CheckCompositeFeatureOn()) return null;
            if (!CheckSpecialCardPlayable(cardId, CardKind.FusionCard, out var card)) return null;

            var a = Board.At(first);
            var b = Board.At(second);
            if (a == null)
            {
                FailReason = $"元素1 的位置 {FormatPos(first)} 上没有元素";
                return null;
            }
            if (b == null)
            {
                FailReason = $"元素2 的位置 {FormatPos(second)} 上没有元素";
                return null;
            }
            if (a == b)
            {
                FailReason = first == second
                    ? "元素1 和元素2 是同一个格子，融合需要两只不同的元素"
                    : "元素1 和元素2 是同一只元素";
                return null;
            }

            if (!FusionRecipes.TryGetKind(a.Element, b.Element, out var kind))
            {
                FailReason = $"{ElementDefs.Name(a.Element)} 和 {ElementDefs.Name(b.Element)} 配不成复合元素"
                             + $"（配方：{RecipeListText()}）";
                return null;
            }

            // 前置条件要在算出配方**之后**才能判：允许多个时，限制是「没有同种的」。
            if (!CanFuseInto(kind))
            {
                FailReason = Rules.AllowMultipleComposites
                    ? $"场上已经有一个「{CompositeDefs.Name(kind)}」了，同一种复合元素不能再合一个"
                    : $"场上已经有「{DescribeComposites()}」，融合牌只在场上没有复合元素时才能用"
                      + "（想再合一个得先用分解牌把它拆掉；要允许多个并存就把 Rules.AllowMultipleComposites 打开）";
                return null;
            }

            if (Rules.FusionRequiresAdjacency && !IsOrthogonallyAdjacent(a.Pos, b.Pos))
            {
                FailReason = $"{ElementDefs.Name(a.Element)}{FormatPos(a.Pos)} 和 "
                             + $"{ElementDefs.Name(b.Element)}{FormatPos(b.Pos)} 不是十字相邻的（上下左右紧挨着才算），"
                             + "融合需要两个正交相邻的元素（斜角不行）";
                return null;
            }

            _hand.Remove(card);
            _cardsPlayedThisRound++;
            TotalFusions++;

            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsPlayerCard = true,
                IsFusion = true,
                Element = a.Element,
                From = a.Pos,
                To = first,
            };

            // 两个基础元素被「用掉」：不是相克消失，所以不喂小瓶子（消耗 ≠ 湮灭）。
            RemoveCreep(a, record, first, feedBottle: false);
            RemoveCreep(b, record, second, feedBottle: false);

            // 复合元素落在**元素1**的位置。
            var composite = new Composite(_nextUid++, kind, first);
            _composites[composite.Uid] = composite;
            Board.PutComposite(composite);

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.CompositeSpawn,
                From = first,
                To = first,
                Uid = composite.Uid,
                Element = a.Element,
                Text = $"{ElementDefs.Name(a.Element)} × {ElementDefs.Name(b.Element)} → 合成「{CompositeDefs.Name(kind)}」",
            });

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);

            // 复合元素的效果「当回合会直接生效一次」；之后每回合开始时再生效。
            if (Rules.CompositeProducesOnCreation)
                EstablishComposite(composite, first);

            return record;
        }

        // ---------------------------------------------------------------- 分解（分解牌）

        /// <summary>「分解牌」能点的格子：场上有复合元素的格子。</summary>
        public List<Pos> CellsForDecompose()
        {
            var result = new List<Pos>();
            foreach (var composite in Board.Composites()) result.Add(composite.Pos);
            return result;
        }

        /// <summary>
        /// 打出一张分解牌：拆掉 <paramref name="target"/> 上的复合元素。
        /// 复合元素从棋盘消失，并按 <see cref="Rules.DecomposeGrantsElementCards"/> 给回两张对应元素的元素牌。
        /// 这张牌**不占**每回合出牌上限（它不产出元素，是纯解场牌），但一张牌只能拆一个复合元素。
        /// </summary>
        public StepRecord TryPlayDecomposeCard(int cardId, Pos target)
        {
            FailReason = null;

            if (!CheckCompositeFeatureOn()) return null;
            if (!CheckSpecialCardPlayable(cardId, CardKind.DecomposeCard, out var card)) return null;

            var composite = Board.CompositeAt(target);
            if (composite == null)
            {
                FailReason = $"位置 {FormatPos(target)} 上没有复合元素，分解牌只能作用于复合元素";
                return null;
            }

            _hand.Remove(card);
            TotalDecompositions++;

            return DecomposeComposite(composite, card, countAsPlayed: false);
        }

        /// <summary>
        /// 拆掉一个复合元素（分解牌、以及将来的机制都可以走这里）。
        /// 返回排好的步骤；<paramref name="countAsPlayed"/> 为 true 时计入本回合出牌数。
        /// </summary>
        public StepRecord DecomposeComposite(Composite composite, Card card, bool countAsPlayed = true)
        {
            if (composite == null) return null;

            if (countAsPlayed) _cardsPlayedThisRound++;

            var ingredients = composite.Ingredients;
            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsPlayerCard = card != null,
                IsDecompose = true,
                From = composite.Pos,
                To = composite.Pos,
            };

            RemoveComposite(composite, record);

            var granted = new List<string>(2);
            if (Rules.DecomposeGrantsElementCards)
            {
                foreach (var element in ingredients)
                {
                    // 元素池是这个关卡的硬约束：池外元素连牌都不该产生（配置正确时不会走到这里）。
                    if (!Rules.ElementPool.Has(element))
                    {
                        record.Events.Add(new GameEvent
                        {
                            Type = GameEventType.SpawnBlocked,
                            From = composite.Pos,
                            Element = element,
                            Text = $"分解本应给回一张{ElementDefs.Name(element)}牌，但本关可用元素只有「{Rules.ElementPool.Names()}」",
                        });
                        continue;
                    }

                    var newCard = DrawCard(element);
                    granted.Add(ElementDefs.Name(element));
                    record.Events.Add(new GameEvent
                    {
                        Type = GameEventType.GainCard,
                        Element = element,
                        Uid = newCard.Id,
                        Text = $"分解「{CompositeDefs.Name(composite.Kind)}」→ 获得 1 张{ElementDefs.Name(element)}牌",
                    });
                }
            }

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);
            return record;
        }

        /// <summary>把复合元素从棋盘上拿掉（不喂小瓶子：它不是元素）。</summary>
        void RemoveComposite(Composite composite, StepRecord record)
        {
            if (composite == null) return;

            Board.ClearComposite(composite.Pos);
            _composites.Remove(composite.Uid);
            _compositeProducedThisRound.Remove(composite.Uid);

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.CompositeVanish,
                From = composite.Pos,
                To = composite.Pos,
                Uid = composite.Uid,
                Text = $"「{CompositeDefs.Name(composite.Kind)}」从棋盘上移除",
            });
        }

        // ---------------------------------------------------------------- 复合元素产生效果

        /// <summary>
        /// 本回合还有没有复合元素没「产生」过。准备阶段在推进小怪之前先把它跑光，
        /// 这样复合元素生出来的元素本回合就能正常参与移动。
        /// </summary>
        public bool HasPendingCompositeProduction()
        {
            foreach (var kv in _composites)
                if (!_compositeProducedThisRound.Contains(kv.Key)) return true;
            return false;
        }

        /// <summary>
        /// 让下一只还没产生的复合元素产生一次，返回它的步骤记录；全都产生完了返回 null。
        /// 顺序按 uid（= 上场顺序），保证同种子可复现。
        /// </summary>
        public StepRecord AdvanceCompositeProduction()
        {
            Composite next = null;
            foreach (var kv in _composites)
            {
                if (_compositeProducedThisRound.Contains(kv.Key)) continue;
                if (next == null || kv.Key < next.Uid) next = kv.Value;
            }

            if (next == null) return null;
            return EstablishComposite(next, Pos.None);
        }

        /// <summary>
        /// 复合元素的「产生」：在自己的范围内生成两种组成元素各 N 份。
        /// <paramref name="originOverride"/> 非 None 时用它当范围中心（融合当回合用元素1 的位置）。
        /// 生成出来落在**已被占的格子**上时，会和那格上的元素按图2 的表反应（这就是「可与范围内已有的元素产生反应」）。
        /// </summary>
        StepRecord EstablishComposite(Composite composite, Pos originOverride)
        {
            if (composite == null) return null;

            _compositeProducedThisRound.Add(composite.Uid);
            _reservedThisProduction.Clear(); // 这一批产出重新开始预约空格

            var origin = originOverride.IsNone ? composite.Pos : originOverride;
            var record = new StepRecord
            {
                Index = _stepIndex++,
                IsCompositeEffect = true,
                Element = Element.Metal,
                From = origin,
                To = origin,
                Uid = composite.Uid,
            };

            var ingredients = composite.Ingredients;
            int perRound = Math.Max(0, Rules.CompositeIngredientsPerRound);

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Message,
                From = origin,
                Uid = composite.Uid,
                Text = $"「{CompositeDefs.Name(composite.Kind)}」生效：在范围内生产 "
                       + $"{ElementDefs.Name(ingredients[0])}+{perRound}、"
                       + $"{ElementDefs.Name(ingredients[1])}+{perRound}"
                       + (Rules.CompositeRandomProductionOrder ? "（两种随机交错产出）" : ""),
            });

            // 产出顺序：
            //   · 默认**随机交错** —— 两种组成元素打乱成一条序列依次产，不会「先把木铺满最近一圈再铺火」
            //   · 关掉开关则按配方顺序「先产完 A 的 N 份、再产 B 的 N 份」（图2 表格的字面读法）
            // 打乱用的是引擎自己的随机源（NextRandom），所以同一个种子完全可复现。
            foreach (var element in ProductionSequence(ingredients, perRound))
                ProduceCompositeElement(element, origin, record);

            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);
            return record;
        }

        /// <summary>
        /// 这一次「产生」按什么顺序产元素：两种组成元素各 <paramref name="perRound"/> 份，
        /// 展平成一条序列。
        ///
        ///   · <see cref="Rules.CompositeRandomProductionOrder"/> 开（默认）→ **打乱**
        ///   · 关 → 按配方顺序「A 全部在前、B 全部在后」
        ///
        /// 打乱用 <see cref="NextRandom"/>（= 引擎的随机源 / 测试的脚本随机），所以同种子可复现。
        /// </summary>
        List<Element> ProductionSequence(Element[] ingredients, int perRound)
        {
            var sequence = new List<Element>(perRound * ingredients.Length);

            if (!Rules.CompositeRandomProductionOrder)
            {
                foreach (var element in ingredients)
                    for (int i = 0; i < perRound; i++) sequence.Add(element);

                return sequence;
            }

            // 先铺成「成对交错」的底：A,B,A,B…（这样即使不打乱也不会先堆完一种）
            for (int i = 0; i < perRound; i++)
                for (int j = 0; j < ingredients.Length; j++)
                    sequence.Add(ingredients[j]);

            // 再整体打乱 —— Fisher-Yates，随机源是引擎自己的
            for (int i = sequence.Count - 1; i > 0; i--)
            {
                int j = NextRandom(i + 1);
                (sequence[i], sequence[j]) = (sequence[j], sequence[i]);
            }

            return sequence;
        }

        /// <summary>在范围内找一格把这一个元素产出来（逐份产出，所以每次重算范围）。</summary>
        void ProduceCompositeElement(Element element, Pos origin, StepRecord record)
        {
            var cell = FindProductionCell(origin);
            if (cell.IsNone)
            {
                SpawnBlockedCount++;
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.SpawnBlocked,
                    From = origin,
                    Element = element,
                    Text = $"{ElementDefs.Name(element)}元素本应产出，但范围内没有可用空格",
                });
                return;
            }

            SpawnProducedElement(element, cell, origin, record);
        }

        /// <summary>
        /// 范围内挑一格：
        ///   1. 范围内**已经被元素占着**的格子 —— 直接把元素生产到它头上，触发一次图2 的反应；
        ///   2. 再不行找范围内**离中心最近**的空格（同距离按行优先，保证可复现）。
        /// 复合元素占的格子、墙、越界一律跳过（复合元素不吃反应、也不能被覆盖）。
        /// </summary>
        Pos FindProductionCell(Pos origin)
        {
            // 优先撞已有元素（图2：「可与范围内已有的元素产生反应」）；没有才用最近空格。
            var occupied = FirstOccupiedProductionCell(origin);
            if (!occupied.IsNone) return occupied;

            return FirstEmptyProductionCell(origin, 0, out _);
        }

        /// <summary>范围内第一个被元素占着的格子（行优先，确定性）；没有就返回 Pos.None。</summary>
        Pos FirstOccupiedProductionCell(Pos origin)
        {
            int range = Math.Max(1, Rules.CompositeRange);

            for (int dy = -range; dy <= range; dy++)
            {
                for (int dx = -range; dx <= range; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var p = new Pos(origin.X + dx, origin.Y + dy);
                    if (!Board.InBounds(p) || Board.IsBlocked(p)) continue;
                    if (Board.HasComposite(p)) continue;
                    if (_reservedThisProduction.Contains(p)) continue;
                    if (Board.At(p) != null) return p;
                }
            }
            return Pos.None;
        }

        /// <summary>
        /// 范围内第 <paramref name="skipCount"/> 个能放元素的空格：跳过墙、越界、复合元素、
        /// 已有元素，以及**本次产生里已经被占掉的格子**。
        /// 排序 = 距离（曼哈顿）升序 → 同距离按行优先。完全确定性，不掷骰子。
        /// </summary>
        Pos FirstEmptyProductionCell(Pos origin, int skipCount, out int total)
        {
            int range = Math.Max(1, Rules.CompositeRange);
            var candidates = new List<(int distance, Pos pos)>();

            for (int dy = -range; dy <= range; dy++)
            {
                for (int dx = -range; dx <= range; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var p = new Pos(origin.X + dx, origin.Y + dy);
                    if (!Board.IsEmpty(p)) continue; // IsEmpty 已经排掉了墙 / 越界 / 复合元素 / 已有元素
                    if (_reservedThisProduction.Contains(p)) continue;

                    candidates.Add((Math.Abs(dx) + Math.Abs(dy), p));
                }
            }

            total = candidates.Count;
            if (total == 0) return Pos.None;

            candidates.Sort((a, b) =>
            {
                int byDistance = a.distance.CompareTo(b.distance);
                if (byDistance != 0) return byDistance;
                int byY = a.pos.Y.CompareTo(b.pos.Y);
                return byY != 0 ? byY : a.pos.X.CompareTo(b.pos.X);
            });

            return candidates[skipCount % total].pos;
        }

        /// <summary>
        /// 把一份产出的元素放进格子：
        ///   · 空格 → 直接生成
        ///   · 已有元素 → 按图2 的表反应一次（相克：输家消失；相生：输家就地变）
        ///   · 被复合元素占着 → 换一格（不该发生，防御性处理）
        /// </summary>
        void SpawnProducedElement(Element element, Pos cell, Pos origin, StepRecord record)
        {
            var existing = Board.At(cell);
            if (existing == null)
            {
                if (!Rules.ElementPool.Has(element))
                {
                    SpawnBlockedCount++;
                    record.Events.Add(new GameEvent
                    {
                        Type = GameEventType.SpawnBlocked,
                        From = origin,
                        To = cell,
                        Element = element,
                        Text = $"{ElementDefs.Name(element)}元素本应产出，但本关可用元素只有「{Rules.ElementPool.Names()}」",
                    });
                    return;
                }

                PlaceProducedCreep(element, cell, origin, record);
                return;
            }

            // 和范围内已有的元素反应：**产出的那个**当作主动撞上去的一方（和出牌时的约定一致）。
            var def = Reactions.Get(element, existing.Element);
            int existingUid = existing.Uid;
            var existingElement = existing.Element;

            _producedEncounters++;
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Message,
                From = cell,
                Element = element,
                Text = $"产出的{ElementDefs.Name(element)}与{ElementDefs.Name(existingElement)}相遇（{def.Describe()}）",
            });

            var probe = new Creep(-1, element, cell);

            if (def.Rule == ReactionRule.NoReaction)
            {
                // 同元素：这一格上有同种元素，这一份没法放上去。
                //
                // ⚠ 正常情况下走不到这里 —— 同一批产出会先把空格预约掉（_reservedThisProduction），
                //    所以不会自己撞自己。能走到的只有「范围内原有的同种元素」这一种情况。
                //    这里只是把这一份换到范围内的另一个空格；换位置必须走认识复合元素的取格路径，
                //    **不能**用 Board.FindSpawnCell（它会把元素放到隔壁复合元素那格上，然后 Put 抛异常）。
                SpawnBlockedCount++;
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.SpawnBlocked,
                    From = origin,
                    To = cell,
                    Element = element,
                    Text = $"产出的{ElementDefs.Name(element)}遇到同种元素，改产范围内的其他空格",
                });

                if (!Rules.ElementPool.Has(element)) return;

                var elsewhere = FirstEmptyProductionCell(origin, 0, out var freeCount);
                if (freeCount == 0) return; // 范围内一个空格都没有，这一份浪费掉

                if (elsewhere != cell && Board.IsEmpty(elsewhere))
                    PlaceProducedCreep(element, elsewhere, origin, record);
                return;
            }

            var winnerIsProduced = def.Winner == element;
            var loser = winnerIsProduced ? existing : probe;

            if (def.Rule == ReactionRule.BothVanish)
            {
                RemoveCreep(existing, record, cell);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = cell,
                    Element = existingElement,
                    Uid = existingUid,
                    Text = $"产出的{ElementDefs.Name(element)} × {ElementDefs.Name(existingElement)} → 双方都消失",
                });
                return;
            }

            if (def.Rule == ReactionRule.Transform)
            {
                var loserElement = loser.Element;
                loser.Element = def.Winner;
                TotalTransformed++;
                _converted[loserElement] = ConvertedOf(loserElement) + 1;
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Transform,
                    From = cell,
                    To = cell,
                    Element = loserElement,
                    Uid = loser.Uid,
                    Text = $"{ElementDefs.Name(element)} × {ElementDefs.Name(existingElement)} → "
                           + $"{ElementDefs.Name(loserElement)}变为{ElementDefs.Name(def.Winner)}（相生）",
                });
                return;
            }

            // 相克：输家消失
            if (loser == existing)
            {
                RemoveCreep(existing, record, cell);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = cell,
                    Element = existingElement,
                    Uid = existingUid,
                    Text = $"产出的{ElementDefs.Name(element)} × {ElementDefs.Name(existingElement)} → "
                           + $"{ElementDefs.Name(existingElement)}消失（{ElementDefs.Name(def.Winner)}克之）",
                });

                // 产出方赢了 → 它顺势占住让出来的这一格
                PlaceProducedCreep(element, cell, origin, record);
            }
            else
            {
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Vanish,
                    From = cell,
                    Element = element,
                    Text = $"产出的{ElementDefs.Name(element)} × {ElementDefs.Name(existingElement)} → "
                           + $"产出的{ElementDefs.Name(element)}消失（{ElementDefs.Name(def.Winner)}克之）",
                });
            }
        }

        /// <summary>真正把一份产出的元素放上棋盘（走和普通生成完全一样的记账路径）。</summary>
        void PlaceProducedCreep(Element element, Pos cell, Pos origin, StepRecord record)
        {
            // 预约这一格：同一批产出后面几份就不会再挑到它了。
            _reservedThisProduction.Add(cell);

            var creep = new Creep(_nextUid++, element, cell);
            _creeps[creep.Uid] = creep;
            Board.Put(creep);

            TotalSpawned++;
            _producedSpawns++;
            _compositeProducedCountThisRound++;
            _spawned[element] = SpawnedOf(element) + 1;

            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Spawn,
                From = origin,
                To = cell,
                Element = element,
                Uid = creep.Uid,
                Text = $"复合元素产出 1 个{ElementDefs.Name(element)}元素",
            });
        }

        // ---------------------------------------------------------------- 统计 / 文案

        /// <summary>本局累计成功融合的次数。</summary>
        public int TotalFusions { get; private set; }

        /// <summary>本局累计分解复合元素的次数。</summary>
        public int TotalDecompositions { get; private set; }

        /// <summary>
        /// 本局复合元素**尝试产出的份数**（一次一份，不管最后是落在空格上、还是撞到已有元素上反应掉了）。
        /// 想区分「多少份真的变成新元素」用 <see cref="CompositeProducedSpawns"/>。
        /// </summary>
        public int CompositeProducedCount => _producedSpawns + SpawnBlockedCount;

        /// <summary>其中**真的在空格上生成了一只新元素**的份数。</summary>
        public int CompositeProducedSpawns => _producedSpawns;

        /// <summary>本局复合元素产出时和范围内已有元素发生的反应次数。</summary>
        public int CompositeProducedEncounters => _producedEncounters;

        int _producedSpawns;
        int _producedEncounters;

        void ResetCompositeState()
        {
            _composites.Clear();
            _compositeProducedThisRound.Clear();
            _compositeProducedCountThisRound = 0;
            _reservedThisProduction.Clear();
            TotalFusions = 0;
            TotalDecompositions = 0;
            _producedSpawns = 0;
            _producedEncounters = 0;
        }

        /// <summary>
        /// 复合元素总开关的公共检查：<see cref="Rules.UseCompositeElements"/> 没勾时，
        /// 融合牌 / 分解牌一律用不出去，并且**明确告诉玩家是关卡的开关没开** ——
        /// 不然玩家的体验是「这张牌点了没反应，也不说为什么」。
        /// </summary>
        bool CheckCompositeFeatureOn()
        {
            if (Rules.UseCompositeElements) return true;

            FailReason = "本关没有开启「复合元素」，融合牌 / 分解牌都用不了";
            return false;
        }

        /// <summary>秘术牌的公共前置检查（阶段、牌在不在手里、种类对不对、出牌上限）。</summary>
        bool CheckSpecialCardPlayable(int cardId, CardKind kind, out Card card)
        {
            card = null;

            if (Phase != GamePhase.PlayerAction)
            {
                FailReason = "当前不是玩家行动阶段";
                return false;
            }

            if (Rules.MaxCardsPerRound > 0 && _cardsPlayedThisRound >= Rules.MaxCardsPerRound)
            {
                FailReason = $"本回合已经出了 {_cardsPlayedThisRound} 张牌（上限 {Rules.MaxCardsPerRound}）";
                return false;
            }

            card = FindCard(cardId);
            if (card == null)
            {
                FailReason = "手牌里没有这张牌";
                return false;
            }

            if (card.Kind != kind)
            {
                FailReason = $"这张牌是{card.Label}，不是{(kind == CardKind.FusionCard ? "融合牌" : "分解牌")}";
                card = null;
                return false;
            }

            // 牌在手里但本关根本没启用这一种（比如只有融合牌、没开分解牌）
            if (!Rules.HasSpecialCard(kind))
            {
                FailReason = $"本关没有启用{(kind == CardKind.FusionCard ? "融合牌" : "分解牌")}（这一关的秘术牌只有「{Rules.SpecialCardPool}」）";
                card = null;
                return false;
            }

            return true;
        }

        static string FormatPos(Pos p) => p.IsNone ? "（越界）" : p.ToString();

        /// <summary>场上复合元素的名单（提示文案用）。</summary>
        string DescribeComposites()
        {
            var names = new List<string>();
            foreach (var c in Board.Composites())
            {
                var name = CompositeDefs.Name(c.Kind);
                if (!names.Contains(name)) names.Add(name);
            }
            return names.Count == 0 ? "（没有）" : string.Join("、", names);
        }

        /// <summary>配方表的人话说明（失败提示 / HUD 用）。</summary>
        public static string RecipeListText()
        {
            var parts = new List<string>(CompositeDefs.Count);
            for (int i = 0; i < CompositeDefs.Count; i++)
            {
                var def = CompositeDefs.Of((CompositeKind)i);
                parts.Add($"{ElementDefs.Name(def.A)}+{ElementDefs.Name(def.B)}={def.Name}");
            }
            return string.Join("、", parts);
        }
    }
}
