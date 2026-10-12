using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 一个很笨的贪心玩家，只用来回答「玩家有没有操作空间 / 这个胜利条件能不能达成」。
    /// 不是 AI，只是一条可复现的固定策略：能打就打最有利于减少元素种类的那一张。
    /// 测试与编辑器批量模拟共用同一份，保证两边结论一致。
    ///
    /// ⚠ 它**不会主动召唤**（图1 第 2 条：元素牌打在空格上召唤一只）—— 召唤是「凭空多一只元素」，
    /// 对它那几个「减少元素 / 减少种类」的目标基本都是负收益，所以它只考虑打在元素格上。
    /// 于是批量模拟里的召唤次数恒为 0，这条规则要靠人工试玩验证（见 README 第九节）。
    /// </summary>
    public static class GreedyPlayer
    {
        public static int Play(GameEngine engine, int maxCardsPerRound = 40)
        {
            int played = 0;
            for (int guard = 0; guard < maxCardsPerRound; guard++)
            {
                Card best = null;
                Pos bestTarget = Pos.None;
                int bestScore = int.MinValue;

                foreach (var card in engine.Hand)
                {
                    // 这个贪心玩家只理解元素牌（秘术牌要配位置、要挑相邻对，不在它的策略里）。
                    if (!card.IsElementCard) continue;

                    foreach (var creep in engine.Creeps)
                    {
                        if (!engine.CanPlayCardOn(card, creep)) continue;

                        var def = engine.Reactions.Get(creep.Element, card.Element);

                        // 打分：优先消耗场上数量最多的元素；相克能真正减少元素种类，相生只是换身份；
                        // 胜利条件相关的元素（默认是水）尽量留着。
                        int score = engine.Board.CountOf(creep.Element) * 10;
                        if (def.IsDestroying) score += 12;              // 相克：直接把目标清掉
                        else if (def.Winner == card.Element) score += 3; // 相生且牌赢：目标元素 −1（但会多出牌面元素）
                        else score -= 8;                                 // 相生且目标赢：等于白送目标 +1

                        if (creep.Element == PreferredElement(engine)) score -= 100;
                        if (def.IsGenerating && card.Element != PreferredElement(engine)) score -= 6;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = card;
                            bestTarget = creep.Pos;
                        }
                    }
                }

                if (best == null) break;
                if (engine.TryPlayCard(best.Id, bestTarget) == null) break;
                played++;
            }
            return played;
        }

        /// <summary>胜利条件关注的元素：WaterOnly 时是水，其它目标没有偏好。</summary>
        static Element PreferredElement(GameEngine engine)
            => engine.Rules.Goal.Kind == GoalKind.WaterOnly ? Element.Water : (Element)(-1);

        /// <summary>
        /// 弃牌阶段的简单策略：把手牌里「能打的目标最少」的牌先弃掉，
        /// 一直弃到手牌降到上限以内、或本回合弃牌次数用完。返回实际弃掉的张数。
        /// </summary>
        public static int DiscardWorst(GameEngine engine, int maxDiscards = 40)
        {
            int discarded = 0;
            for (int guard = 0; guard < maxDiscards; guard++)
            {
                if (engine.Hand.Count <= engine.Rules.HandLimit) break;
                if (engine.DiscardsRemaining <= 0) break;

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

                    // 能打的目标越多越值钱；胜利条件关注的元素再减一点价值
                    if (card.IsElementCard && card.Element == PreferredElement(engine)) score--;

                    if (score < worstScore)
                    {
                        worstScore = score;
                        worst = card;
                    }
                }

                if (worst == null) break;
                if (!engine.TryDiscardCard(worst.Id)) break;
                discarded++;
            }
            return discarded;
        }
    }
}
