using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Levels.Mechanics;

namespace TapGJ.Tests
{
    /// <summary>
    /// 第二章要用的三个胜利条件组件（都测它们的**静态纯函数**，不用建 GameObject）：
    ///   · <see cref="FusionCountRule"/>   —— 累计融合 N 次（「合成教学」）
    ///   · <see cref="DecomposeCountRule"/> —— 累计分解 N 次，可要求「场上不再剩复合元素」（「分解五种」）
    ///   · <see cref="ElementKindsRule"/>  —— 场上恰好剩 N 种元素（「只剩两种元素」，不指定是哪两种）
    /// </summary>
    public class Chapter2RuleTests
    {
        static Rules MakeRules() => new Rules
        {
            BoardWidth = 6, BoardHeight = 6,
            CreepsPerElement = 0, InitialHandSize = 0, HandLimit = 5, RoundLimit = 10,
            Goal = GoalSpec.Custom(),
            UseCompositeElements = true,
            SpecialCardPool = SpecialCardMask.All,
            FusionCardWeight = 10, DecomposeCardWeight = 10,
            FusionRequiresAdjacency = true,
        };

        static GameEngine NewEngine()
        {
            var engine = new GameEngine(MakeRules(), 5);
            engine.Start();
            engine.EndPreparationNow(); // 直接进玩家行动阶段
            return engine;
        }

        // ---------------------------------------------------------------- 融合

        [Test]
        public void 融合计数_没融合过不算赢_融合一次就满足()
        {
            var engine = NewEngine();
            Assert.IsFalse(FusionCountRule.IsSatisfied(engine, 1, out _), "一次都没融合过");

            engine.PlaceCreep(Element.Metal, new Pos(2, 2));
            engine.PlaceCreep(Element.Water, new Pos(3, 2)); // 金 + 水 = 寒钢
            var card = engine.AddSpecialCardForTest(CardKind.FusionCard);

            Assert.IsNotNull(engine.TryPlayFusionCard(card.Id, new Pos(2, 2), new Pos(3, 2)), "相邻的金水应该能合");
            Assert.AreEqual(1, engine.TotalFusions);
            Assert.IsTrue(FusionCountRule.IsSatisfied(engine, 1, out var message));
            StringAssert.Contains("融合", message);
            Assert.IsFalse(FusionCountRule.IsSatisfied(engine, 2, out _), "要 2 次的话还不够");
        }

        // ---------------------------------------------------------------- 分解

        [Test]
        public void 分解计数_可要求场上不再剩复合元素()
        {
            var engine = NewEngine();
            engine.PlaceComposite(CompositeKind.Lava, new Pos(2, 2));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(4, 4));

            var card = engine.AddSpecialCardForTest(CardKind.DecomposeCard);
            Assert.IsNotNull(engine.TryPlayDecomposeCard(card.Id, new Pos(2, 2)));

            Assert.AreEqual(1, engine.TotalDecompositions);
            Assert.AreEqual(1, engine.CompositeCount, "场上还剩一个");

            Assert.IsTrue(DecomposeCountRule.IsSatisfied(engine, 1, false, out _), "只要求次数时已经满足");
            Assert.IsFalse(DecomposeCountRule.IsSatisfied(engine, 1, true, out _), "要求场上不剩复合元素 → 还不满足");

            var card2 = engine.AddSpecialCardForTest(CardKind.DecomposeCard);
            Assert.IsNotNull(engine.TryPlayDecomposeCard(card2.Id, new Pos(4, 4)));

            Assert.AreEqual(2, engine.TotalDecompositions);
            Assert.AreEqual(0, engine.CompositeCount);
            Assert.IsTrue(DecomposeCountRule.IsSatisfied(engine, 2, true, out var message));
            StringAssert.Contains("不再有复合元素", message);
        }

        // ---------------------------------------------------------------- 元素种类

        [Test]
        public void 元素种类_恰好N种才算赢_空场不算()
        {
            var engine = NewEngine();

            Assert.IsFalse(ElementKindsRule.IsSatisfied(engine, 2, out var empty),
                "空棋盘是 0 种，精确相等下天然不算赢 —— 「清空全场」不会成为捷径");
            StringAssert.Contains("只剩 0 种", empty);

            engine.PlaceCreep(Element.Metal, new Pos(0, 0));
            engine.PlaceCreep(Element.Water, new Pos(1, 1));
            Assert.IsTrue(ElementKindsRule.IsSatisfied(engine, 2, out var two), "金 + 水 = 恰好两种");
            StringAssert.Contains("金", two);

            engine.PlaceCreep(Element.Wood, new Pos(2, 2));
            Assert.IsFalse(ElementKindsRule.IsSatisfied(engine, 2, out var three), "多了一种就不算");
            StringAssert.Contains("3 种", three);
        }
    }
}
