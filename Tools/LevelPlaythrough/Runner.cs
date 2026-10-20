using System;
using System.Collections.Generic;
using System.Linq;
using TapGJ.Core;

namespace TapGJ.Playthrough
{
    public enum PlayStyle
    {
        ObjectiveGreedy = 0,
        Aggressive = 1,
        Conservative = 2,
        SummonFocused = 3,
        ReactionFocused = 4,
        CompositeFocused = 5,
        BottleFocused = 6,
        BalanceFocused = 7,
        RandomizedNearBest = 8,
        Exploratory = 9,
    }

    public static class PlayStyles
    {
        public static readonly PlayStyle[] All =
        {
            PlayStyle.ObjectiveGreedy, PlayStyle.Aggressive, PlayStyle.Conservative,
            PlayStyle.SummonFocused, PlayStyle.ReactionFocused, PlayStyle.CompositeFocused,
            PlayStyle.BottleFocused, PlayStyle.BalanceFocused, PlayStyle.RandomizedNearBest,
            PlayStyle.Exploratory,
        };

        public static string Name(PlayStyle style) => style switch
        {
            PlayStyle.ObjectiveGreedy => "目标贪心",
            PlayStyle.Aggressive => "相克优先",
            PlayStyle.Conservative => "保守省牌",
            PlayStyle.SummonFocused => "召唤铺路",
            PlayStyle.ReactionFocused => "相遇反应优先",
            PlayStyle.CompositeFocused => "复合循环优先",
            PlayStyle.BottleFocused => "瓶子产牌优先",
            PlayStyle.BalanceFocused => "均衡目标优先",
            PlayStyle.RandomizedNearBest => "近优随机",
            PlayStyle.Exploratory => "探索型随机",
            _ => style.ToString(),
        };

        public static double AllowedLoss(PlayStyle style) => style switch
        {
            PlayStyle.Aggressive => 0.03,
            PlayStyle.SummonFocused => 0.03,
            PlayStyle.CompositeFocused => 0.02,
            PlayStyle.BottleFocused => 0.02,
            PlayStyle.RandomizedNearBest => 0.08,
            PlayStyle.Exploratory => 0.30,
            _ => 0.000001,
        };
    }

    public sealed class Action
    {
        public string Kind;     // play / fusion / decompose
        public int CardId;
        public Pos A = Pos.None;
        public Pos B = Pos.None;

        public string Label => Kind switch
        {
            "fusion" => $"融合#{CardId}@{A}+{B}",
            "decompose" => $"分解#{CardId}@{A}",
            _ => $"出牌#{CardId}@{A}",
        };
    }

    public sealed class LevelResult
    {
        public LevelData Level;
        public int Seed;
        public bool Victory;
        public string Message;
        public int Rounds;
        public int CardsPlayed;
        public int Fusions;
        public int Decompositions;
        public int Summons;
        public List<string> Trace = new List<string>();
        public string FinalBoard;
        public string FailPhase;
        public List<string> GoalProgress = new List<string>();
    }

    /// <summary>
    /// 用引擎自己的规则重放一串「每回合打哪些牌」的动作脚本。
    /// 脚本是唯一真源：同一个 seed + 同一串动作，必然得到同一局。
    /// </summary>
    public static class GameRunner
    {
        public static GameEngine NewEngine(LevelData level, int seed)
        {
            var engine = new GameEngine(level.Rules, seed);
            engine.AttachRules(level.RuleFactories.Select(f => f()));
            if (level.UseEvents) engine.AttachEventPool(level.EventFactories.Select(f => f()));
            engine.Start(level.Setup());
            Drain(engine);
            return engine;
        }

        public static void Drain(GameEngine engine)
        {
            int guard = 0;
            while (engine.AwaitingStepPlayback && guard++ < 10000) engine.DequeueStep();
        }

        public static void AdvanceToPlayerPhase(GameEngine engine)
        {
            int guard = 0;
            while (engine.Phase == GamePhase.Preparation && guard++ < 1000)
            {
                engine.AdvancePreparation();
                Drain(engine);
            }
        }

        /// <summary>
        /// 跑脚本。stopAtPlayerPhase = true 时停在**最后一回合**的玩家行动阶段，方便评估候选动作。
        /// </summary>
        public static GameEngine Replay(LevelData level, int seed, List<List<Action>> script, bool stopAtPlayerPhase = true)
        {
            var engine = NewEngine(level, seed);

            if (script.Count == 0)
            {
                AdvanceToPlayerPhase(engine);
                return engine;
            }

            for (int r = 0; r < script.Count; r++)
            {
                AdvanceToPlayerPhase(engine);

                foreach (var action in script[r])
                {
                    if (!Apply(engine, action))
                    {
                        // 理论上不会发生：脚本是确定性重放出来的
                        return engine;
                    }
                    Drain(engine);
                }

                if (stopAtPlayerPhase && r == script.Count - 1) return engine;

                EndRound(engine);
                if (engine.IsOver) break;
            }

            return engine;
        }

        public static bool Apply(GameEngine engine, Action action)
        {
            switch (action.Kind)
            {
                case "play": return engine.TryPlayCard(action.CardId, action.A) != null;
                case "fusion": return engine.TryPlayFusionCard(action.CardId, action.A, action.B) != null;
                case "decompose": return engine.TryPlayDecomposeCard(action.CardId, action.A) != null;
                default: return false;
            }
        }

        public static void EndRound(GameEngine engine)
        {
            if (engine.Phase == GamePhase.PlayerAction) engine.EndPlayerPhase();
            if (engine.Phase == GamePhase.Discard)
            {
                DiscardToLimit(engine);
                engine.EndDiscardPhase();
            }
            if (engine.Phase == GamePhase.Judgment) engine.ResolveJudgment();
            Drain(engine);
        }

        /// <summary>
        /// 弃牌阶段：把「这一关永远打不出去 / 对当前目标没有帮助 / 挡着挖牌」的牌扔掉，
        /// 再把多出来的牌弃到上限。真人玩家在第三章会这么干，否则瓶子产的死牌会把 5 张手牌堵死。
        /// </summary>
        public static void DiscardToLimit(GameEngine engine)
        {
            int guard = 0;

            while (engine.Phase == GamePhase.Discard && engine.DiscardsRemaining > 0 && guard++ < 40)
            {
                Card junk = null;
                foreach (var card in engine.Hand)
                {
                    if (!GoalLogic.HelpsGoal(engine, card)) { junk = card; break; }
                }

                if (junk == null) break;
                if (!engine.TryDiscardCard(junk.Id)) break;
                Drain(engine);
            }

            // 超过手牌上限的部分
            while (engine.Phase == GamePhase.Discard &&
                   engine.Hand.Count > engine.Rules.HandLimit &&
                   engine.DiscardsRemaining > 0 && guard++ < 40)
            {
                Card worst = null;
                int worstScore = int.MaxValue;

                foreach (var card in engine.Hand)
                {
                    int score = 0;
                    if (card.IsElementCard)
                    {
                        foreach (var creep in engine.Creeps)
                            if (engine.CanPlayCardOn(card, creep)) score++;
                    }
                    else
                    {
                        score = engine.Rules.UseCompositeElements ? 1 : 0;
                    }

                    if (score < worstScore) { worstScore = score; worst = card; }
                }

                if (worst == null || !engine.TryDiscardCard(worst.Id)) break;
                Drain(engine);
            }
        }
    }

    /// <summary>
    /// 「这张牌/这个动作对当前目标有没有用」的纯逻辑判断，弃牌和候选筛选共用。
    /// 只看目标，不看具体位置 —— 用来回答「该不该把它扔掉去挖别的牌」。
    /// </summary>
    public static class GoalLogic
    {
        public static bool NeedsFusion(GameEngine e)
        {
            bool explicitFusion = e.LevelRules.Any(r => r is FusionCountRule f && e.TotalFusions < f.Count);
            bool fusionForDecompose = e.LevelRules.Any(r => r is DecomposeCountRule d && e.TotalDecompositions < d.Count);
            return explicitFusion || (fusionForDecompose && e.Rules.UseCompositeElements &&
                   e.Rules.HasSpecialCard(CardKind.FusionCard));
        }

        public static bool NeedsDecompose(GameEngine e)
            => e.LevelRules.Any(r => r is DecomposeCountRule d && e.TotalDecompositions < d.Count);

        /// <summary>这个元素「从场上消失」对目标有没有好处。</summary>
        public static bool WantsRemove(GameEngine e, Element el)
        {
            var g = e.Rules.Goal;
            switch (g.Kind)
            {
                case GoalKind.WaterOnly: return el != Element.Water;
                case GoalKind.SingleElement: return true;
                case GoalKind.ClearBoard: return true;
                case GoalKind.ElementAtMost: return el == g.Target;
                case GoalKind.ElementAtLeast: return false;
                case GoalKind.Custom: break;
                default: return false;
            }

            foreach (var rule in e.LevelRules)
            {
                switch (rule)
                {
                    case OnlyTheseElementsRule o: return !o.Allowed.Has(el);
                    case BottleProducedRule: 
                        return e.LevelRules.OfType<BottleProducedRule>().Any(b => b.Element == el);
                    case EqualElementCountsRule: return true;
                    case ElementKindsRule: return true;
                }
            }
            return false;
        }

        /// <summary>这个元素「多出来一个」对目标有没有好处。</summary>
        public static bool WantsAdd(GameEngine e, Element el)
        {
            var g = e.Rules.Goal;
            switch (g.Kind)
            {
                case GoalKind.WaterOnly: return el == Element.Water;
                case GoalKind.ElementAtLeast: return el == g.Target;
                case GoalKind.Custom: break;
                default: return false;
            }

            foreach (var rule in e.LevelRules)
            {
                if (rule is OnlyTheseElementsRule o && o.Allowed.Has(el) && e.Board.CountOf(el) == 0) return true;
                if (rule is EqualElementCountsRule q)
                {
                    var considered = EqualElementCountsRule.Considered(e, q.RequireAllPoolElements);
                    if (considered.Count == 0) continue;
                    int min = considered.Min(x => e.Board.CountOf(x));
                    if (e.Board.CountOf(el) == min) return true;
                }
            }

            // 复合元素循环的下一步目标可能是补齐一对可融合的原料。
            if (NeedsFusion(e) && e.CompositeCount == 0 &&
                e.Hand.Any(c => c.IsFusionCard) && e.CellsForFusionFirst().Count == 0)
                return true;

            return false;
        }

        /// <summary>手上一张对目标完全没用的牌 → 弃牌阶段扔掉它去挖牌。</summary>
        public static bool HelpsGoal(GameEngine e, Card card)
        {
            if (card.IsSpecialCard)
                return card.IsFusionCard ? NeedsFusion(e) : (NeedsDecompose(e) || e.CompositeCount > 0);

            if (ShouldDig(e, card)) return false;

            // 融合原料：元素牌本身就是资源，别扔
            if (NeedsFusion(e) || NeedsDecompose(e)) return true;

            foreach (var creep in e.Creeps)
            {
                if (!e.CanPlayCardOn(card, creep)) continue;

                var def = e.Reactions.Get(creep.Element, card.Element);
                if (def.IsDestroying && WantsRemove(e, creep.Element)) return true;
                if (def.Rule == ReactionRule.BothVanish &&
                    (WantsRemove(e, creep.Element) || WantsRemove(e, card.Element))) return true;

                if (def.IsGenerating)
                {
                    bool cardWins = def.Winner == card.Element;
                    if (cardWins && (WantsRemove(e, creep.Element) || WantsAdd(e, card.Element))) return true;
                    if (!cardWins && WantsAdd(e, creep.Element)) return true;
                }
            }

            if (WantsAdd(e, card.Element) && HasEmptyCell(e)) return true;
            return false;
        }

        /// <summary>缺关键秘术牌、手上又没有 → 先把元素牌扔掉，抽牌阶段才有位置挖到它。</summary>
        public static bool ShouldDig(GameEngine e, Card card)
        {
            if (card.IsSpecialCard) return false;

            bool handFusion = e.Hand.Any(c => c.IsFusionCard);
            bool handDecompose = e.Hand.Any(c => c.IsDecomposeCard);

            bool digFusion = NeedsFusion(e) && e.Rules.HasSpecialCard(CardKind.FusionCard) &&
                             !handFusion && e.CellsForFusionFirst().Count == 0;

            bool digDecompose = NeedsDecompose(e) && e.Rules.HasSpecialCard(CardKind.DecomposeCard) &&
                                !handDecompose && e.CompositeCount > 0;

            return digFusion || digDecompose;
        }

        static bool HasEmptyCell(GameEngine e)
        {
            for (int y = 0; y < e.Board.Height; y++)
            for (int x = 0; x < e.Board.Width; x++)
            {
                var p = new Pos(x, y);
                if (e.Board.IsEmpty(p) && !e.Board.IsBlocked(p)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 「我」这个玩家：对每个候选动作做一次**真实重放**，用关卡自己的胜负条件给结果打分，取最高分。
    /// 没有克隆引擎的能力，所以每个候选都是「从开局重放到这一刻」—— 慢，但结论和真玩完全一致。
    /// </summary>
    public sealed class Policy
    {
        readonly LevelData _level;
        readonly int _seed;
        readonly PlayStyle _style;
        readonly Random _random;

        public bool Verbose = Environment.GetEnvironmentVariable("PT_VERBOSE") == "1";

        public Policy(LevelData level, int seed, PlayStyle style, int strategySeed = 0)
        {
            _level = level;
            _seed = seed;
            _style = style;
            _random = new Random(unchecked(seed * 397 ^ ((int)style + 1) * 7919 ^ strategySeed));
        }

        // ------------------------------------------------------------ 评估

        public double Score(GameEngine e)
        {
            if (e.CheckWinCondition(out _)) return 1e9;

            double s = 0;
            var g = e.Rules.Goal;

            switch (g.Kind)
            {
                case GoalKind.WaterOnly:
                {
                    int w = e.Board.CountOf(Element.Water);
                    int other = e.CreepCount - w;
                    s = -other * 100.0 + w * 2.0 - (w == 0 ? 30 : 0);
                    break;
                }
                case GoalKind.SingleElement:
                    s = -e.Board.DistinctElementCount() * 100.0 - e.CreepCount;
                    break;
                case GoalKind.ClearBoard:
                    s = -e.CreepCount * 10.0;
                    break;
                case GoalKind.SurviveRounds:
                    s = e.Round * 10.0;
                    break;
                case GoalKind.ElementAtMost:
                    s = -e.Board.CountOf(g.Target) * 10.0;
                    break;
                case GoalKind.ElementAtLeast:
                    s = e.Board.CountOf(g.Target) * 10.0;
                    break;
                case GoalKind.Custom:
                {
                    double best = double.NegativeInfinity, sum = 0;
                    foreach (var rule in e.LevelRules)
                    {
                        double p = Progress(e, rule);
                        if (p > best) best = p;
                        sum += p;
                    }
                    s = (e.Rules.RequireAllWinRules ? sum : best) * 100.0;

                    // 分解教学循环的「准备动作」也应有价值：先融合，再等复合元素产出，最后分解。
                    // 否则只看当前胜利进度，融合这一步必然像亏分，玩家会一直空过回合。
                    if (GoalLogic.NeedsDecompose(e))
                    {
                        bool hasFusion = e.Hand.Any(c => c.IsFusionCard);
                        bool canFuse = hasFusion && e.CellsForFusionFirst().Count > 0;
                        if (e.CompositeCount == 0 && hasFusion)
                            s += canFuse ? 0.32 : 0.10; // 可融合=现在做；缺搭档=找机会召唤
                        else if (e.CompositeCount > 0 && e.Composites.Any(c => !c.ProducedThisRound)) s += 0.45;
                        else if (e.CompositeCount > 0) s += 0.50;
                    }
                    break;
                }
            }

            // 手牌是资源：白扔一张牌要有代价，否则「召唤」会被当成免费动作
            s += e.Hand.Count * 0.01;

            // 布局潜力：需要融合 / 分解时，「下一步就能做」的局面值一点分。
            // 没有这两项，1 层前瞻会把「先召唤一个元素让两只怪挨在一起」判成零收益，从而永远不开工。
            bool explicitFusionGoal = e.LevelRules.Any(r => r is FusionCountRule f && e.TotalFusions < f.Count);
            if (explicitFusionGoal && e.Hand.Any(c => c.IsFusionCard) && e.CellsForFusionFirst().Count > 0)
                s += 3.0;

            if (GoalLogic.NeedsDecompose(e) && e.Hand.Any(c => c.IsDecomposeCard) &&
                e.Composites.Any(c => c.ProducedThisRound)) s += 5.0;

            return s;
        }

        double Progress(GameEngine e, ILevelRule rule)
        {
            switch (rule)
            {
                case FusionCountRule f:
                    return Math.Min(1.0, e.TotalFusions / (double)Math.Max(1, f.Count));

                case DecomposeCountRule d:
                {
                    double p = Math.Min(1.0, e.TotalDecompositions / (double)Math.Max(1, d.Count));
                    if (d.RequireNoComposites && e.CompositeCount > 0) p -= 0.35;
                    return Math.Max(0, p);
                }

                case ElementKindsRule k:
                {
                    int kinds = e.Board.DistinctElementCount();
                    return Math.Max(0, 1.0 - Math.Abs(kinds - k.Kinds) / 5.0);
                }

                case OnlyTheseElementsRule o:
                {
                    int allowed = 0, total = 0;
                    foreach (var el in ElementDefs.All)
                    {
                        int n = e.Board.CountOf(el);
                        total += n;
                        if (o.Allowed.Has(el)) allowed += n;
                    }
                    if (total == 0) return 0;
                    return (double)allowed / total;
                }

                case EqualElementCountsRule q:
                {
                    var considered = EqualElementCountsRule.Considered(e, q.RequireAllPoolElements);
                    if (considered.Count == 0) return 0;

                    int min = int.MaxValue, max = 0;
                    bool missing = false;
                    foreach (var el in considered)
                    {
                        int c = e.Board.CountOf(el);
                        if (c == 0 && q.RequireAllPoolElements) missing = true;
                        if (c < min) min = c;
                        if (c > max) max = c;
                    }

                    double p = 1.0 - (max - min) / (double)Math.Max(1, max);
                    if (missing) p = Math.Min(p, 0.4);
                    return Math.Max(0, p);
                }

                case BottleProducedRule b:
                {
                    int cap = Math.Max(1, e.Bottle.Capacity);
                    int need = Math.Max(1, b.Count) * cap;
                    int got = e.Bottle.CardsGranted(b.Element) * cap + e.Bottle.Count(b.Element);
                    return Math.Min(1.0, (double)got / need);
                }

                case ReactionCountRule rc:
                    return Math.Min(1.0, rc.Count / (double)Math.Max(1, rc.Target));

                default:
                    return 0;
            }
        }

        double StyleUtility(GameEngine before, Action action, GameEngine after, double objectiveScore)
        {
            double value = objectiveScore;
            bool emptyTarget = action.Kind == "play" && before.Board.IsEmpty(action.A);
            Creep target = action.Kind == "play" ? before.Board.At(action.A) : null;
            ReactionDef reaction = target == null || action.Kind != "play"
                ? default
                : before.Reactions.Get(target.Element, before.FindCard(action.CardId).Element);

            switch (_style)
            {
                case PlayStyle.Aggressive:
                    if (reaction.Rule == ReactionRule.Annihilate) value += 0.35;
                    if (reaction.Rule == ReactionRule.BothVanish) value += 0.20;
                    break;

                case PlayStyle.Conservative:
                    if (emptyTarget) value -= 0.12;
                    if (action.Kind == "fusion" || action.Kind == "decompose") value -= 0.04;
                    break;

                case PlayStyle.SummonFocused:
                    if (emptyTarget) value += 0.40;
                    break;

                case PlayStyle.ReactionFocused:
                    if (target != null)
                    {
                        value += 0.22;
                        if (reaction.Rule == ReactionRule.Annihilate) value += 0.25;
                        if (reaction.Rule == ReactionRule.Transform) value += 0.10;
                    }
                    break;

                case PlayStyle.CompositeFocused:
                    if (action.Kind == "fusion") value += 0.50;
                    if (action.Kind == "decompose") value += 0.50;
                    break;

                case PlayStyle.BottleFocused:
                    value += BottleProgress(after) * 2.0;
                    if (after.Bottle.TotalCardsGranted > before.Bottle.TotalCardsGranted) value += 5.0;
                    break;

                case PlayStyle.BalanceFocused:
                    foreach (var rule in after.LevelRules.OfType<EqualElementCountsRule>())
                    {
                        if (EqualElementCountsRule.IsBalanced(after, rule.RequireAllPoolElements,
                                rule.Tolerance, out _)) value += 2.0;
                        else value += rule.GetGoalProgress(after).Fill * 0.50;
                    }
                    break;

                case PlayStyle.RandomizedNearBest:
                    value += _random.NextDouble() * 0.08;
                    break;

                case PlayStyle.Exploratory:
                    value += (_random.NextDouble() * 2.0 - 1.0) * 0.50;
                    break;
            }

            return value;
        }

        static double BottleProgress(GameEngine engine)
        {
            double sum = 0;
            foreach (var rule in engine.LevelRules.OfType<BottleProducedRule>())
                sum += Math.Min(1.0, engine.Bottle.CardsGranted(rule.Element) / (double)Math.Max(1, rule.Count));
            return sum;
        }

        // ------------------------------------------------------------ 候选动作

        public List<Action> Candidates(GameEngine e)
        {
            var list = new List<Action>();
            if (e.Phase != GamePhase.PlayerAction) return list;

            foreach (var card in e.Hand)
            {
                if (card.IsElementCard)
                {
                    // 同一张牌 + 同一种目标元素 + 同一种反应 = 同样的结果，只留一个代表
                    var seen = new HashSet<string>();
                    foreach (var creep in e.Creeps)
                    {
                        if (!e.CanPlayCardOn(card, creep)) continue;

                        var def = e.Reactions.Get(creep.Element, card.Element);
                        var key = $"{(int)creep.Element}|{(int)def.Rule}|{(int)def.Winner}";
                        if (!seen.Add(key)) continue;

                        list.Add(new Action { Kind = "play", CardId = card.Id, A = creep.Pos });
                    }

                    // 召唤：位置对元素数量没有影响，取几个代表（优先贴着别的元素的空格，方便之后融合）
                    foreach (var cell in SummonCells(e, 24))
                        list.Add(new Action { Kind = "play", CardId = card.Id, A = cell });
                }
                else if (card.IsFusionCard)
                {
                    int added = 0;
                    foreach (var first in e.CellsForFusionFirst())
                    {
                        foreach (var second in e.CellsForFusionSecond(first))
                        {
                            list.Add(new Action { Kind = "fusion", CardId = card.Id, A = first, B = second });
                            if (++added >= 10) break;
                        }
                        if (added >= 10) break;
                    }
                }
                else if (card.IsDecomposeCard)
                {
                    foreach (var pos in e.CellsForDecompose())
                    {
                        // 刚合出来的复合元素还没「产生」过：等它产一轮再拆，否则每拆一次场上净少两只元素，
                        // 拆到第二、三次就没有原料可以再合了（ch2_03 正是靠这一条才转得起来）。
                        var composite = e.CompositeAt(pos);
                        if (composite != null && !composite.ProducedThisRound &&
                            !e.Rules.CompositeProducesOnCreation && e.CreepCount <= 4)
                            continue;

                        list.Add(new Action { Kind = "decompose", CardId = card.Id, A = pos });
                    }
                }
            }

            return list;
        }

        static List<Pos> SummonCells(GameEngine e, int max)
        {
            var result = new List<Pos>();
            var b = e.Board;

            // 先找「贴着其他元素」的空格：融合牌要相邻，这类位置更有用
            for (int y = 0; y < b.Height && result.Count < max; y++)
            for (int x = 0; x < b.Width && result.Count < max; x++)
            {
                var p = new Pos(x, y);
                if (!b.IsEmpty(p)) continue;
                if (b.IsBlocked(p)) continue;

                bool touches = false;
                foreach (var d in new[] { new Pos(1, 0), new Pos(-1, 0), new Pos(0, 1), new Pos(0, -1) })
                {
                    var q = new Pos(x + d.X, y + d.Y);
                    if (b.InBounds(q) && b.At(q) != null) { touches = true; break; }
                }
                if (touches) result.Add(p);
            }

            if (result.Count < max)
            {
                for (int y = 0; y < b.Height && result.Count < max; y++)
                for (int x = 0; x < b.Width && result.Count < max; x++)
                {
                    var p = new Pos(x, y);
                    if (b.IsEmpty(p) && !b.IsBlocked(p) && !result.Contains(p)) result.Add(p);
                }
            }

            return result;
        }

        // ------------------------------------------------------------ 玩

        public LevelResult Play()
        {
            var result = new LevelResult { Level = _level, Seed = _seed };
            var script = new List<List<Action>>();
            var engine = GameRunner.Replay(_level, _seed, script);

            int guard = 0;
            while (!engine.IsOver && guard++ < 60)
            {
                script.Add(new List<Action>());
                int round = engine.Round;
                int plays = 0;
                var roundLog = new List<string>();

                while (engine.Phase == GamePhase.PlayerAction && plays < 10)
                {
                    var candidates = Candidates(engine);
                    if (Verbose)
                    {
                        Console.WriteLine($"      [R{round} P{plays}] 手牌 [{string.Join(",", engine.Hand.Select(c => c.Label))}]" +
                                          $" 复合 {engine.CompositeCount} 候选 {candidates.Count} 分数 {Score(engine):F2}");
                    }
                    if (candidates.Count == 0) break;

                    double current = Score(engine);
                    double allowedLoss = PlayStyles.AllowedLoss(_style);
                    var evaluations = new List<(Action action, double raw, double utility)>();
                    Action rawBest = null;
                    double rawBestScore = double.NegativeInfinity;

                    foreach (var candidate in candidates)
                    {
                        var trial = CloneScript(script);
                        trial[trial.Count - 1].Add(candidate);

                        var after = GameRunner.Replay(_level, _seed, trial);
                        double raw = Score(after);
                        if (raw > rawBestScore)
                        {
                            rawBestScore = raw;
                            rawBest = candidate;
                        }
                        if (raw < current - allowedLoss) continue;

                        double utility = StyleUtility(engine, candidate, after, raw);
                        evaluations.Add((candidate, raw, utility));
                    }

                    Action best = null;
                    double bestScore = double.NegativeInfinity;
                    if (evaluations.Count > 0)
                    {
                        bestScore = evaluations.Max(x => x.utility);
                        var shortlist = evaluations
                            .Where(x => x.utility >= bestScore - (_style == PlayStyle.RandomizedNearBest ? 0.08 : 0.000001))
                            .ToList();

                        if (_style == PlayStyle.RandomizedNearBest || _style == PlayStyle.Exploratory)
                        {
                            var exploratory = evaluations.Where(x => x.utility >= bestScore - 0.25).ToList();
                            if (exploratory.Count > 0) shortlist = exploratory;
                            best = shortlist[_random.Next(shortlist.Count)].action;
                        }
                        else
                        {
                            best = shortlist[0].action;
                        }
                    }

                    if (Verbose && best == null && candidates.Count > 0)
                        Console.WriteLine($"          无合规动作；最佳候选 {rawBest?.Label ?? "?"} 得分 {rawBestScore:F2}，基线 {current:F2}");

                    if (best == null) break;
                    if (!GameRunner.Apply(engine, best)) break;

                    GameRunner.Drain(engine);
                    script[script.Count - 1].Add(best);
                    plays++;
                    result.CardsPlayed++;

                    roundLog.Add(best.Label);
                }

                if (roundLog.Count > 0)
                    result.Trace.Add($"R{round}: " + string.Join("  ", roundLog));

                GameRunner.EndRound(engine);

                if (engine.IsOver) break;
                GameRunner.AdvanceToPlayerPhase(engine);
            }

            result.Victory = engine.Phase == GamePhase.Victory;
            result.Rounds = engine.Round;
            result.Fusions = engine.TotalFusions;
            result.Decompositions = engine.TotalDecompositions;
            result.Summons = engine.TotalSummoned;
            result.FailPhase = engine.Phase.ToString();
            engine.CheckWinCondition(out var reason);
            result.Message = reason ?? (engine.IsOver ? engine.Phase.ToString() : "回合用尽仍未达成");
            result.FinalBoard = BoardLine(engine);
            foreach (var rule in engine.LevelRules)
            {
                var progress = rule.GetGoalProgress(engine);
                if (!string.IsNullOrEmpty(progress.Label)) result.GoalProgress.Add(progress.Label);
            }
            return result;
        }

        static List<List<Action>> CloneScript(List<List<Action>> script)
        {
            var copy = new List<List<Action>>(script.Count);
            foreach (var round in script)
                copy.Add(new List<Action>(round));
            return copy;
        }

        public static string BoardLine(GameEngine e)
        {
            var parts = new List<string>();
            foreach (var el in ElementDefs.All)
            {
                int n = e.Board.CountOf(el);
                if (n > 0) parts.Add($"{ElementDefs.Name(el)}{n}");
            }
            return parts.Count == 0 ? "空场" : string.Join(" ", parts);
        }
    }
}
