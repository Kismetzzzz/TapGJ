using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;

namespace TapGJ.Tests
{
    /// <summary>
    /// 「秘术牌从第几回合开始进牌堆」（<see cref="Rules.SpecialCardUnlockRound"/>）的测试。
    ///
    /// 这一项是给**教学关节奏**用的：第 1 回合只发元素牌，让玩家先看清楚复合元素每回合都在产什么，
    /// 第 2 回合再把分解牌发下来（第二章 ch2_02 就是这么配的）。
    ///
    /// 为了不依赖权重概率，用例把 ElementCardWeight 设成 0、DecomposeCardWeight 设成 10：
    /// 于是结果只有两种 —— 「没解锁 → 退回发元素牌」「解锁了 → 全是分解牌」，可以逐个断言。
    /// </summary>
    public class SpecialCardUnlockTests
    {
        static Rules MakeRules(int unlockRound, int roundLimit = 10) => new Rules
        {
            BoardWidth = 6,
            BoardHeight = 6,
            CreepsPerElement = 0,
            InitialHandSize = 5,
            HandLimit = 5,
            RoundLimit = roundLimit,
            UseCompositeElements = true,
            SpecialCardPool = SpecialCardMask.Decompose,
            ElementCardWeight = 0,   // 没有元素牌可抽 → 解锁前只剩「退回发元素牌」这一条路
            DecomposeCardWeight = 10,
            SpecialCardUnlockRound = unlockRound,

            // 分解牌只有「本关可能拿到复合元素」时才进牌堆（见 Rules.DrawableSpecialCards）：
            // 这里摆一个初始复合元素，否则分解牌是死牌、抽牌阶段会直接跳过它。
            InitialComposites = new[] { new CompositePlacement(1, 1, CompositeKind.Ember) },
        };

        static int CountDecompose(GameEngine engine)
        {
            int n = 0;
            foreach (var card in engine.Hand)
                if (card.IsDecomposeCard) n++;
            return n;
        }

        [Test]
        public void 解锁回合2_第1回合起手没有分解牌_第2回合抽牌阶段才发()
        {
            var engine = new GameEngine(MakeRules(2), 20240607);
            engine.Start();

            Assert.AreEqual(1, engine.Round);
            Assert.AreEqual(5, engine.Hand.Count, "起手牌还是满的，只是全是元素牌");
            Assert.AreEqual(0, CountDecompose(engine), "第 1 回合的起手牌里不该有分解牌");

            engine.ClearHandForTest();
            engine.BeginRound();        // 进入第 2 回合
            engine.EndPreparationNow(); // 走完准备阶段 + 抽牌阶段

            Assert.AreEqual(2, engine.Round);
            Assert.AreEqual(5, engine.Hand.Count, "抽牌阶段把手牌补回上限");
            Assert.AreEqual(5, CountDecompose(engine), "第 2 回合起分解牌进牌堆 → 补的 5 张全是它");
        }

        [Test]
        public void 默认值1_与加这个字段之前完全一致_第1回合就能抽到()
        {
            Assert.AreEqual(1, new Rules().SpecialCardUnlockRound, "默认值必须是 1，老关卡才不会变");

            var engine = new GameEngine(MakeRules(1), 20240607);
            engine.Start();
            Assert.AreEqual(5, CountDecompose(engine), "解锁回合 = 1 → 起手就该抽到分解牌");
        }

        [Test]
        public void 回合号判定_起手抽牌按第1回合算()
        {
            var locked = MakeRules(2);
            Assert.IsFalse(locked.SpecialCardsAvailableAtRound(0), "起手抽牌发生在 Round=0，但它就是第 1 回合的牌");
            Assert.IsFalse(locked.SpecialCardsAvailableAtRound(-3), "负数也按第 1 回合算");
            Assert.IsFalse(locked.SpecialCardsAvailableAtRound(1));
            Assert.IsTrue(locked.SpecialCardsAvailableAtRound(2));
            Assert.IsTrue(locked.SpecialCardsAvailableAtRound(9));

            var open = MakeRules(1);
            Assert.IsTrue(open.SpecialCardsAvailableAtRound(0));
            Assert.IsTrue(open.SpecialCardsAvailableAtRound(1));
        }

        [Test]
        public void 解锁回合大于回合上限_配置校验会报出来()
        {
            var rules = MakeRules(5, roundLimit: 3);
            var issues = new List<string>();
            rules.CollectIssues(issues);

            Assert.IsTrue(issues.Exists(s => s.Contains("一张秘术牌都抽不到")),
                "解锁回合比回合上限还大 = 这一关永远抽不到秘术牌，必须报出来。实际：" + string.Join(" / ", issues));
        }

        [Test]
        public void 解锁回合小于1_校验直接抛()
        {
            Assert.Throws<System.ArgumentException>(() => MakeRules(0).Validate());
        }
    }
}
