using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Events;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 元素小瓶子的开关（`Rules.BottleMode`）。
    ///
    /// 钉住四条：
    ///   · 枚举排 0 = **Enabled**：老关卡资产反序列化出来（= 0）自动保持「有瓶」，行为不变
    ///   · 关掉之后：元素消失**不喂瓶**、**不产牌**、瓶子计数恒 0，但元素照样从棋盘消失（统计照记）
    ///   · 关瓶时「玉翁敛息」判不适用（抽到会被跳过，不占本回合），引擎的 ApplyBottleCapacity 也变成 no-op
    ///   · 开瓶时的老行为完全没变（5 个消失 → 1 张牌）
    /// </summary>
    public class BottleModeTests
    {
        static Rules MakeRules(BottleMode mode = BottleMode.Enabled)
            => new Rules
            {
                BoardWidth = 4, BoardHeight = 4,
                CreepsPerElement = 0, InitialHandSize = 0, HandLimit = 5,
                RoundLimit = 10,
                Goal = GoalSpec.Custom(),
                BottleMode = mode,
                BottleCapacity = 5,
            };

        static GameEngine NewEngine(BottleMode mode = BottleMode.Enabled)
        {
            var engine = new GameEngine(MakeRules(mode), 7);
            engine.Start();
            return engine;
        }

        /// <summary>造 N 次「某元素从棋盘上消失」，走的是和正常相克完全相同的移除路径。</summary>
        static void KillCreeps(GameEngine engine, Element element, int count)
        {
            for (int i = 0; i < count; i++)
                Assert.IsTrue(engine.TryPlaceAndRemoveForTest(element), $"第 {i + 1} 只应该能被移除");
        }

        // ---------------------------------------------------------------- 兼容性

        [Test]
        public void 默认有瓶_枚举排0保证老关卡行为不变()
        {
            Assert.AreEqual(0, (int)BottleMode.Enabled, "Enabled 必须是 0：老资产反序列化出来就是它");
            Assert.IsTrue(new Rules().BottlesEnabled, "默认必须是「有瓶」");
            Assert.IsFalse(new Rules { BottleMode = BottleMode.Disabled }.BottlesEnabled);
        }

        // ---------------------------------------------------------------- 开 vs 关

        [Test]
        public void 有瓶时_消失五个元素产一张牌()
        {
            var engine = NewEngine(BottleMode.Enabled);
            int hand = engine.Hand.Count;

            KillCreeps(engine, Element.Wood, 5);

            Assert.AreEqual(0, engine.Bottle.Count(Element.Wood), "满瓶后清零");
            Assert.AreEqual(1, engine.Bottle.TotalCardsGranted, "五换一");
            Assert.AreEqual(hand + 1, engine.Hand.Count, "手牌多了一张木牌");
        }

        [Test]
        public void 无瓶时_消失多少都不喂瓶也不产牌()
        {
            var engine = NewEngine(BottleMode.Disabled);
            int hand = engine.Hand.Count;

            KillCreeps(engine, Element.Wood, 7); // 超过容量 5，有瓶的话早就该产牌了

            for (int i = 0; i < ElementDefs.Count; i++)
                Assert.AreEqual(0, engine.Bottle.Count(ElementDefs.All[i]), "关瓶后计数恒 0");

            Assert.AreEqual(0, engine.Bottle.TotalCardsGranted, "一张都不该产");
            Assert.AreEqual(hand, engine.Hand.Count, "手牌不该变");
            Assert.AreEqual(7, engine.TotalRemoved, "但元素确实从棋盘消失了（统计照记）");
        }

        // ---------------------------------------------------------------- 事件

        [Test]
        public void 无瓶时_玉翁敛息判不适用_引擎里也变成noop()
        {
            var engine = NewEngine(BottleMode.Disabled);

            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            try
            {
                Assert.IsFalse(ev.IsApplicable(engine, out var reason), "本关没有瓶子，这条事件什么都不做");
                StringAssert.Contains("没有元素瓶", reason);

                Assert.AreEqual(0, engine.ApplyBottleCapacity(3), "结账应返回 0");
                Assert.AreEqual(5, engine.Bottle.Capacity, "容量也不该被改掉");
            }
            finally
            {
                Object.DestroyImmediate(ev);
            }
        }

        [Test]
        public void 有瓶时_玉翁敛息仍然有效()
        {
            var engine = NewEngine(BottleMode.Enabled);
            engine.SetupBottleForTest(4);

            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            try
            {
                Assert.IsTrue(ev.IsApplicable(engine, out _));

                int hand = engine.Hand.Count;
                Assert.AreEqual(5, engine.ApplyBottleCapacity(3), "五个瓶子各结账 1 张");
                Assert.AreEqual(3, engine.Bottle.Capacity);
                Assert.AreEqual(hand + 5, engine.Hand.Count);
            }
            finally
            {
                Object.DestroyImmediate(ev);
            }
        }
    }
}
