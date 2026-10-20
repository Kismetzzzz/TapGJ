using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;

namespace TapGJ.Tests
{
    /// <summary>
    /// 「达成即结算」（<see cref="AutoFinish"/>）的边界测试。
    ///
    /// 这条是为了修一个真实的体验问题：胜负原本只在**判定阶段**算，玩家明明已经满足目标，
    /// 还得点「结束出牌」（有时还要点「结束弃牌」）才看到结算 —— 测试反馈「过关就卡了」。
    ///
    /// 它是个纯函数，所以这里不需要协程、不需要场景：只验「什么时候该抢答、什么时候不该」。
    /// </summary>
    public class AutoFinishTests
    {
        /// <summary>空棋盘 + 目标「水 ≤ 0」→ 开局就达成，方便验「已经赢了会怎样」。</summary>
        static GameEngine Won()
        {
            var engine = new GameEngine(MakeRules(GoalSpec.ElementAtMost(Element.Water, 0)), 7);
            engine.Start();
            engine.EndPreparationNow();   // 准备阶段 → 玩家行动阶段
            return engine;
        }

        /// <summary>目标还没达成（水要到 3 个才算赢）。</summary>
        static GameEngine NotYet()
        {
            var engine = new GameEngine(MakeRules(GoalSpec.ElementAtLeast(Element.Water, 3)), 7);
            engine.Start();
            engine.EndPreparationNow();
            return engine;
        }

        static Rules MakeRules(GoalSpec goal) => new Rules
        {
            BoardWidth = 5,
            BoardHeight = 5,
            CreepsPerElement = 0,
            InitialHandSize = 0,
            HandLimit = 5,
            RoundLimit = 10,
            Goal = goal,
        };

        [Test]
        public void 玩家阶段达成目标_立刻结算()
        {
            var engine = Won();
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);

            Assert.IsTrue(AutoFinish.ShouldFinish(engine, busy: false, paused: false, out var reason),
                "目标已经达成就该直接结算，不用玩家再点「结束出牌」");
            StringAssert.Contains("水", reason, "给的理由应该来自引擎的胜利文案");
        }

        [Test]
        public void 目标没达成_不结算()
        {
            var engine = NotYet();
            Assert.IsFalse(AutoFinish.ShouldFinish(engine, false, false));
        }

        [Test]
        public void 动画播放中或暂停中_不抢()
        {
            var engine = Won();

            Assert.IsFalse(AutoFinish.ShouldFinish(engine, busy: true, paused: false),
                "还有步骤动画在播，这时候弹结算会打断表现");
            Assert.IsFalse(AutoFinish.ShouldFinish(engine, busy: false, paused: true),
                "玩家在设置面板里，别在他背后把关卡结算掉");
        }

        [Test]
        public void 准备阶段_不抢()
        {
            var engine = new GameEngine(MakeRules(GoalSpec.ElementAtMost(Element.Water, 0)), 7);
            engine.Start();

            Assert.AreEqual(GamePhase.Preparation, engine.Phase);
            Assert.IsFalse(AutoFinish.ShouldFinish(engine, false, false),
                "准备阶段还在推进元素/事件，结算要等玩家阶段");
        }

        [Test]
        public void 弃牌阶段达成目标_也自动结算()
        {
            var engine = Won();
            engine.EndPlayerPhase();   // → 弃牌阶段
            Assert.AreEqual(GamePhase.Discard, engine.Phase);

            Assert.IsTrue(AutoFinish.ShouldFinish(engine, false, false),
                "玩家弃完牌刚好达成目标，同样不该让他再点一次「结束弃牌」");
        }

        [Test]
        public void 已经结束的局_不再触发()
        {
            var engine = Won();
            engine.EndPlayerPhase();
            engine.EndDiscardPhase();
            engine.ResolveJudgment();

            Assert.AreEqual(GamePhase.Victory, engine.Phase);
            Assert.IsFalse(AutoFinish.ShouldFinish(engine, false, false), "已经赢了就别重复触发");
        }

        [Test]
        public void 撑过回合类目标_交给判定阶段_不抢答()
        {
            // SurviveRounds 的判定是 Round >= Rounds → 第 1 回合的玩家阶段就已经成立。
            // 抢答会让玩家一张牌都没出就被判通关，所以这一类必须留给判定阶段。
            var engine = new GameEngine(MakeRules(new GoalSpec
            {
                Kind = GoalKind.SurviveRounds,
                Rounds = 1,
            }), 7);
            engine.Start();
            engine.EndPreparationNow();

            Assert.AreEqual(1, engine.Round);
            Assert.IsTrue(engine.CheckWinCondition(out _), "引擎自己认为「已经撑过 1 回合」");

            Assert.IsFalse(AutoFinish.ShouldFinish(engine, false, false),
                "但这一类不能在玩家阶段抢答 —— 玩家还没动手呢");
        }
    }
}
