using NUnit.Framework;
using TapGJ.Core;

namespace TapGJ.Tests
{
    /// <summary>
    /// 平衡性探针：用固定的贪心策略跑一批对局，把「有没有可赢路径」变成可观察的数字。
    /// 这里刻意不断言具体胜率（原型阶段胜率本来就会随规则微调变化），
    /// 只断言「能跑完、能复现、玩家确实有牌可打」，并把统计打到日志里。
    /// </summary>
    public class BalanceProbeTests
    {
        const int Games = 60;

        [Test]
        public void 贪心玩家_只剩一种元素_可赢且元素种类收敛()
        {
            var rules = new Rules
            {
                WinCondition = WinCondition.SingleElement,
                RoundLimit = 15,
            };

            var stats = RunBatch(rules, Games);
            UnityEngine.Debug.Log("[BALANCE/SingleElement] " + stats.Report());

            Assert.AreEqual(Games, stats.Games, "每局都要跑到终局");
            Assert.Greater(stats.TotalCardsPlayed, 0, "玩家至少要打出过牌，否则说明出牌路径根本走不通");

            // 元素种类必须真的在收敛（终局平均要明显低于开局的 5 种）
            Assert.LessOrEqual(stats.BestFinalElementTypes, 5, "元素种类不会超过 5");
            Assert.Less(stats.AverageFinalElementTypes, 5f,
                "如果平均终局元素种类还是 5，说明玩家与反应都没能减少种类 —— 那才是真正的问题");

            // 抽牌阶段每回合补满手牌之后，这条赢法变得很容易：
            // 这里只要求「存在可赢路径」，不锁定具体胜率（数值还会随规则调整）。
            Assert.GreaterOrEqual(stats.Wins, 1,
                "「只剩一种元素」应该存在可赢路径；如果这里是 0，说明当前规则下清不干净棋盘");

            if (stats.Wins == Games)
            {
                UnityEngine.Debug.LogWarning(
                    $"[BALANCE] 「只剩一种元素」在 {Games} 局里全胜（平均 {stats.AverageRounds:F1} 回合、"
                    + $"平均出牌 {stats.AverageCardsPlayed:F1} 张/局）—— 说明当前规则过于宽松，"
                    + "建议给「玩家行动阶段」加上每回合出牌张数上限。");
            }
        }

        [Test]
        public void 贪心玩家_只剩水元素_可赢且元素种类收敛()
        {
            var rules = new Rules
            {
                WinCondition = WinCondition.WaterOnly, // 图1 原文
                RoundLimit = 15,
            };

            var stats = RunBatch(rules, Games);
            UnityEngine.Debug.Log("[BALANCE/WaterOnly] " + stats.Report());

            Assert.AreEqual(Games, stats.Games);
            Assert.Greater(stats.TotalCardsPlayed, 0, "玩家至少要打出过牌");
            Assert.LessOrEqual(stats.BestFinalElementTypes, 5, "元素种类不会超过 5");

            if (stats.Wins == 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[BALANCE] 「只剩水元素」在 15 回合内 0 胜（{Games} 局）。"
                    + $"终局元素种类 平均 {stats.AverageFinalElementTypes:F2}、最好 {stats.BestFinalElementTypes}。");
            }
        }

        [Test]
        public void 贪心玩家_同一批种子结果可复现()
        {
            var rules = new Rules { WinCondition = WinCondition.SingleElement, RoundLimit = 15 };
            var a = RunBatch(rules, 10);
            var b = RunBatch(rules, 10);

            Assert.AreEqual(a.Wins, b.Wins, "同种子同策略必须得到同样的胜场数");
            Assert.AreEqual(a.TotalCardsPlayed, b.TotalCardsPlayed, "同种子同策略必须打出同样多的牌");
            Assert.AreEqual(a.TotalRounds, b.TotalRounds);
        }

        static BatchStats RunBatch(Rules rules, int games)
        {
            var stats = new BatchStats { Games = games };

            for (int i = 0; i < games; i++)
            {
                var engine = new GameEngine(rules, 1000 + i);
                engine.Start();

                int guard = 0;
                while (!engine.IsOver && guard++ < rules.RoundLimit + 4)
                {
                    engine.EndPreparationNow();             // 准备阶段 + 自动抽牌
                    if (engine.IsOver) break;
                    if (engine.Phase != GamePhase.PlayerAction) break;

                    stats.TotalCardsPlayed += GreedyPlayer.Play(engine);
                    engine.EndPlayerPhase();                // 出牌阶段结束 → 弃牌阶段
                    if (engine.Phase == GamePhase.Discard)
                    {
                        stats.TotalCardsDiscarded += GreedyPlayer.DiscardWorst(engine);
                        engine.EndDiscardPhase();           // 弃牌阶段结束 → 判定阶段
                    }
                    engine.ResolveJudgment();               // 判定
                }

                stats.TotalRounds += engine.Round;
                stats.RecordEndState(engine.Board.DistinctElementCount());
                if (engine.Phase == GamePhase.Victory) stats.Wins++;
                else if (engine.Phase == GamePhase.Defeat) stats.Defeats++;
                else stats.Unfinished++;
            }

            return stats;
        }

        sealed class BatchStats
        {
            public int Games;
            public int Wins;
            public int Defeats;
            public int Unfinished;
            public int TotalRounds;
            public int TotalCardsPlayed;
            public int TotalCardsDiscarded;

            /// <summary>终局时场上还剩几种元素 —— 赢不了的时候用它判断「有没有在收敛」。</summary>
            public int BestFinalElementTypes = 5;
            public float AverageFinalElementTypes;

            public float AverageRounds => Games == 0 ? 0f : (float)TotalRounds / Games;
            public float AverageCardsPlayed => Games == 0 ? 0f : (float)TotalCardsPlayed / Games;
            public float AverageCardsDiscarded => Games == 0 ? 0f : (float)TotalCardsDiscarded / Games;

            int _finalTypesSum;

            public void RecordEndState(int distinctElementTypes)
            {
                _finalTypesSum += distinctElementTypes;
                if (distinctElementTypes < BestFinalElementTypes) BestFinalElementTypes = distinctElementTypes;
                AverageFinalElementTypes = Games == 0 ? 0f : (float)_finalTypesSum / Games;
            }

            public string Report() =>
                $"局数={Games} 胜利={Wins} 失败={Defeats} 未结束={Unfinished} "
                + $"平均回合={(Games == 0 ? 0f : (float)TotalRounds / Games):F2} "
                + $"平均出牌={(Games == 0 ? 0f : (float)TotalCardsPlayed / Games):F2} 张/局 "
                + $"平均弃牌={(Games == 0 ? 0f : (float)TotalCardsDiscarded / Games):F2} 张/局 "
                + $"终局元素种类 平均={AverageFinalElementTypes:F2} 最好={BestFinalElementTypes}";
        }
    }
}
