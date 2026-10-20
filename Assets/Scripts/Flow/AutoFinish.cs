using TapGJ.Core;

namespace TapGJ.Flow
{
    /// <summary>
    /// 「达成即结算」的判定（**纯函数**，方便 EditMode 直接测，不用起协程）。
    ///
    /// 为什么需要它：胜负原本只在**判定阶段**算，于是玩家明明已经满足目标，
    /// 还得点一下「结束出牌」（有时候还要再点「结束弃牌」）才看到结算 —— 测试反馈
    /// 「过关就卡了」「完了」，就是以为游戏卡死了。
    ///
    /// 现在的做法：在玩家阶段 / 弃牌阶段每帧问一次这里，成立就自动走完剩余阶段。
    ///
    /// 三个「不结算」的例外（都是刻意的）：
    ///   · `busy` —— 还有步骤动画在播，这时候弹结算会打断表现；
    ///   · `paused` —— 玩家在设置面板里；
    ///   · 阶段不是玩家阶段 / 弃牌阶段 —— 准备阶段的移动、判定阶段的收尾都不该被抢答。
    ///
    /// 外加一类**目标本身是时间条件**的关卡：`GoalKind.SurviveRounds`。
    /// 它的判定是 `Round >= Rounds`，所以在第 1 回合的玩家阶段就已经成立了 —— 抢答会让玩家
    /// 还没动手就被判通关。这一类只在判定阶段结算（`AutoFinishTests` 里钉住了）。
    /// </summary>
    public static class AutoFinish
    {
        /// <summary>现在该不该直接结算？</summary>
        public static bool ShouldFinish(GameEngine engine, bool busy, bool paused)
            => ShouldFinish(engine, busy, paused, out _);

        /// <summary>现在该不该直接结算？成立时 <paramref name="reason"/> 是引擎给的胜利文案。</summary>
        public static bool ShouldFinish(GameEngine engine, bool busy, bool paused, out string reason)
        {
            reason = null;

            if (engine == null) return false;
            if (engine.IsOver) return false;       // 已经赢了/输了，别重复触发
            if (busy || paused) return false;      // 动画中 / 暂停中不抢

            if (engine.Phase != GamePhase.PlayerAction && engine.Phase != GamePhase.Discard)
                return false;

            // 「撑过 N 回合」是时间条件：第 1 回合的玩家阶段就已经 Round >= N 了。
            // 抢答会让玩家连一张牌都没出就通关，所以这一类交给判定阶段。
            if (engine.Rules.Goal.Kind == GoalKind.SurviveRounds) return false;

            return engine.CheckWinCondition(out reason);
        }
    }
}
