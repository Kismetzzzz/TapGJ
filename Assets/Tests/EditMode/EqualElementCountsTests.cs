using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Levels.Mechanics;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 「场上元素数量相等」这个自定义胜利条件的测试。
    ///
    /// 它有两种读法，用 `requireAllPoolElements` 区分，两种都要钉住：
    ///   · 不勾（默认）：只比较**场上出现**的元素 —— 所以「只剩一种元素」也算赢
    ///   · 勾上：要求**元素池**里的每种都在场且数量相等 —— 玩家没法靠「删到只剩一种」走捷径
    ///
    /// 还有两条容易写错的边界：空场不算赢（否则第 1 回合判定就赢了），
    /// 以及「必须在场」不受 tolerance 影响（差 1 个 ≠ 一个都没有）。
    /// </summary>
    public class EqualElementCountsTests
    {
        static GameEngine Engine(ElementMask pool, int seed = 7, GoalSpec? goal = null)
        {
            var rules = new Rules
            {
                BoardWidth = 6,
                BoardHeight = 6,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                RoundLimit = 15,
                ElementPool = pool,
                CardPool = pool,
                Goal = goal ?? GoalSpec.Custom(),
            };
            var engine = new GameEngine(rules, seed);
            engine.Start();
            return engine;
        }

        /// <summary>按「元素:个数」摆盘，从 (0,0) 开始顺序铺。</summary>
        static GameEngine With(ElementMask pool, params (Element element, int count)[] spec)
        {
            var engine = Engine(pool);
            int x = 0, y = 0;
            foreach (var (element, count) in spec)
            {
                for (int i = 0; i < count; i++)
                {
                    engine.PlaceCreep(element, new Pos(x, y));
                    if (++x >= engine.Board.Width) { x = 0; y++; }
                }
            }
            return engine;
        }

        static bool Balanced(GameEngine engine, bool requireAllPool, int tolerance, out string detail)
            => EqualElementCountsRule.IsBalanced(engine, requireAllPool, tolerance, out detail);

        // ---------------------------------------------------------------- 默认读法：只看场上出现的元素

        [Test]
        public void 默认读法_只剩一种元素也算相等()
        {
            var engine = With(ElementMask.All, (Element.Metal, 5));
            Assert.IsTrue(Balanced(engine, false, 0, out var detail), detail);
            StringAssert.Contains("只剩金", detail);
        }

        [Test]
        public void 默认读法_两三种元素数量相等就赢()
        {
            Assert.IsTrue(Balanced(With(ElementMask.All, (Element.Metal, 3), (Element.Water, 3)), false, 0, out _));
            Assert.IsTrue(Balanced(With(ElementMask.All,
                (Element.Metal, 2), (Element.Water, 2), (Element.Fire, 2)), false, 0, out _));
        }

        [Test]
        public void 默认读法_数量不等就不赢()
        {
            var engine = With(ElementMask.All, (Element.Metal, 2), (Element.Water, 3));
            Assert.IsFalse(Balanced(engine, false, 0, out var detail));
            StringAssert.Contains("差 1", detail);
        }

        [Test]
        public void 默认读法_空场不算赢()
        {
            // 这一条很关键：不特判的话「场上出现过的元素都相等」在空集上是真命题，
            // 于是第 1 回合的判定阶段就直接赢了。
            Assert.IsFalse(Balanced(Engine(ElementMask.All), false, 0, out var detail));
            StringAssert.Contains("没有元素", detail);
        }

        [Test]
        public void 默认读法_元素池外的元素也参与比较()
        {
            // 池子只有金水，但场上被塞了火（正常配置不会发生）。默认读法看的是"场上出现过的"，
            // 所以火也要参与 —— 金2水2火3 不相等。
            var engine = With(ElementMask.Metal | ElementMask.Water,
                (Element.Metal, 2), (Element.Water, 2), (Element.Fire, 3));
            Assert.IsFalse(Balanced(engine, false, 0, out _));
        }

        // ---------------------------------------------------------------- tolerance

        [Test]
        public void 差额容差_放宽数量但不放宽出现()
        {
            var twoThree = With(ElementMask.All, (Element.Metal, 2), (Element.Water, 3));
            Assert.IsFalse(Balanced(twoThree, false, 0, out _));
            Assert.IsTrue(Balanced(twoThree, false, 1, out _), "差 1 在容差 1 之内");

            var twoFour = With(ElementMask.All, (Element.Metal, 2), (Element.Water, 4));
            Assert.IsFalse(Balanced(twoFour, false, 1, out _), "差 2 超出容差 1");
            Assert.IsTrue(Balanced(twoFour, false, 2, out _));

            // 勾了「必须都在场」时，缺的那个元素即使在容差内也不算 —— 差 1 个和一个都没有不是一回事
            var missing = With(ElementMask.Metal | ElementMask.Water | ElementMask.Fire,
                (Element.Metal, 2), (Element.Water, 2));
            Assert.IsFalse(Balanced(missing, true, 5, out var detail));
            StringAssert.Contains("还缺火", detail);
        }

        // ---------------------------------------------------------------- 元素池读法

        [Test]
        public void 元素池读法_必须每种都在场()
        {
            var pool = ElementMask.Metal | ElementMask.Water | ElementMask.Fire;

            Assert.IsFalse(Balanced(With(pool, (Element.Metal, 2), (Element.Water, 2)), true, 0, out _),
                "缺火，不算赢");
            Assert.IsTrue(Balanced(With(pool,
                (Element.Metal, 2), (Element.Water, 2), (Element.Fire, 2)), true, 0, out _));
        }

        [Test]
        public void 元素池读法_池子只有一种元素时立刻成立()
        {
            var engine = With(ElementMask.Metal, (Element.Metal, 5));
            Assert.IsTrue(Balanced(engine, true, 0, out var detail), detail);
            StringAssert.Contains("只剩金", detail);
        }

        [Test]
        public void 元素池读法_不看池外的元素()
        {
            // 池子只有金水；场上多出的火不参与比较（它本来就不该出现，池子是硬约束）
            var engine = With(ElementMask.Metal | ElementMask.Water,
                (Element.Metal, 2), (Element.Water, 2), (Element.Fire, 7));
            Assert.IsTrue(Balanced(engine, true, 0, out var detail), detail);
        }

        // ---------------------------------------------------------------- 进度条

        [Test]
        public void 进度条_相等填满_不等按最少比最多()
        {
            var go = new GameObject("equal-counts");
            try
            {
                var rule = go.AddComponent<EqualElementCountsRule>();

                var equal = With(ElementMask.All, (Element.Metal, 2), (Element.Water, 2));
                var p1 = rule.GetGoalProgress(equal);
                Assert.AreEqual(1f, p1.Fill, 0.001f, "已经赢了就该填满");
                StringAssert.Contains("金2 水2", p1.Label);
                StringAssert.Contains("差 0", p1.Label);

                var uneven = With(ElementMask.All, (Element.Metal, 2), (Element.Water, 4));
                var p2 = rule.GetGoalProgress(uneven);
                Assert.AreEqual(0.5f, p2.Fill, 0.001f, "2/4");
                StringAssert.Contains("差 2", p2.Label);

                var empty = Engine(ElementMask.All);
                var p3 = rule.GetGoalProgress(empty);
                Assert.AreEqual(0f, p3.Fill, 0.001f);
                StringAssert.Contains("没有元素", p3.Label);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void 文案_两种读法各说各的()
        {
            var go = new GameObject("equal-counts-text");
            try
            {
                var rule = go.AddComponent<EqualElementCountsRule>();
                StringAssert.Contains("只剩一种", rule.GoalDescription);

                var so = new UnityEditor.SerializedObject(rule);
                so.FindProperty("requireAllPoolElements").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();

                StringAssert.Contains("每种可用元素都有", rule.GoalDescription);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // ---------------------------------------------------------------- 和引擎接起来

        [Test]
        public void 接进引擎_判定阶段按这个条件判赢()
        {
            var engine = Engine(ElementMask.Metal | ElementMask.Water);
            var go = new GameObject("equal-counts-engine");
            try
            {
                var rule = go.AddComponent<EqualElementCountsRule>();
                engine.AttachRule(rule);

                // 目标是 Custom：内置判定完全不参与，只看这个组件
                Assert.AreEqual(GoalKind.Custom, engine.Rules.Goal.Kind);
                Assert.IsFalse(engine.CheckWinCondition(out _), "空场不该赢");

                engine.PlaceCreep(Element.Metal, new Pos(0, 0));
                engine.PlaceCreep(Element.Water, new Pos(1, 0));
                engine.PlaceCreep(Element.Metal, new Pos(2, 0));
                engine.PlaceCreep(Element.Water, new Pos(3, 0));

                Assert.IsTrue(engine.CheckWinCondition(out var reason), "金2 水2 应该赢");
                StringAssert.Contains("金2 水2", reason);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
