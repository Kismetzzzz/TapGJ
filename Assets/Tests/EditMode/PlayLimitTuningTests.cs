using NUnit.Framework;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 出牌上限的调参探针。
    /// 抽牌阶段每回合把手牌补满 5 张，如果出牌不设上限，玩家一回合能打十几张，
    /// 棋盘几回合就被清空、弃牌阶段永远用不上。这里把几个候选值一次跑完，
    /// 把「回合数 / 出牌数 / 弃牌数 / 胜率」摊开，方便挑一个手感合适的值。
    /// 结果只打到日志里，不做断言（这是调参工具，不是回归测试）。
    /// </summary>
    public class PlayLimitTuningTests
    {
        const int Games = 60;

        [Test]
        public void 调参_每回合出牌上限对手感的影响()
        {
            var rows = new[]
            {
                new { Limit = 0, Label = "不限（默认）" },
                new { Limit = 4, Label = "4 张" },
                new { Limit = 3, Label = "3 张" },
                new { Limit = 2, Label = "2 张" },
                new { Limit = 1, Label = "1 张" },
            };

            var log = new System.Text.StringBuilder();
            log.AppendLine("[TUNING] 出牌上限 → 手感（每格 60 局，胜利条件=只剩一种元素，上限 15 回合）");
            log.AppendLine("  上限        胜率     平均回合   平均出牌   平均弃牌   终局元素种类");

            foreach (var row in rows)
            {
                var single = Probe(WinCondition.SingleElement, row.Limit);
                log.AppendLine($"  {row.Label,-10} {single.Wins,3}/{Games}  "
                               + $"{single.AverageRounds,8:F2}  {single.AverageCardsPlayed,8:F2}  "
                               + $"{single.AverageCardsDiscarded,8:F2}  {single.AverageFinalElementTypes,10:F2}");
            }

            log.AppendLine();
            log.AppendLine("[TUNING] 同上，但胜利条件=只剩水元素（图1 原文）");
            log.AppendLine("  上限        胜率     平均回合   平均出牌   平均弃牌   终局元素种类");
            foreach (var row in rows)
            {
                var water = Probe(WinCondition.WaterOnly, row.Limit);
                log.AppendLine($"  {row.Label,-10} {water.Wins,3}/{Games}  "
                               + $"{water.AverageRounds,8:F2}  {water.AverageCardsPlayed,8:F2}  "
                               + $"{water.AverageCardsDiscarded,8:F2}  {water.AverageFinalElementTypes,10:F2}");
            }

            Debug.Log(log.ToString());
        }

        static Stats Probe(WinCondition condition, int maxCardsPerRound)
        {
            var rules = new Rules
            {
                WinCondition = condition,
                RoundLimit = 15,
                MaxCardsPerRound = maxCardsPerRound,
            };

            var stats = new Stats();

            for (int i = 0; i < Games; i++)
            {
                var engine = new GameEngine(rules, 1000 + i);
                engine.Start();

                int guard = 0;
                while (!engine.IsOver && guard++ < rules.RoundLimit + 4)
                {
                    engine.EndPreparationNow();
                    if (engine.IsOver) break;
                    if (engine.Phase != GamePhase.PlayerAction) break;

                    stats.CardsPlayed += GreedyPlayer.Play(engine);
                    engine.EndPlayerPhase();

                    if (engine.Phase == GamePhase.Discard)
                    {
                        stats.CardsDiscarded += GreedyPlayer.DiscardWorst(engine);
                        engine.EndDiscardPhase();
                    }

                    engine.ResolveJudgment();
                }

                stats.Rounds += engine.Round;
                stats.FinalTypes += engine.Board.DistinctElementCount();
                if (engine.Phase == GamePhase.Victory) stats.Wins++;
            }

            return stats;
        }

        sealed class Stats
        {
            public int Wins;
            public int Rounds;
            public int CardsPlayed;
            public int CardsDiscarded;
            public int FinalTypes;

            public float AverageRounds => (float)Rounds / Games;
            public float AverageCardsPlayed => (float)CardsPlayed / Games;
            public float AverageCardsDiscarded => (float)CardsDiscarded / Games;
            public float AverageFinalElementTypes => (float)FinalTypes / Games;
        }
    }
}
