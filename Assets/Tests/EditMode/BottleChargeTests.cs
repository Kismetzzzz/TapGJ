using System;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;

namespace TapGJ.Tests
{
    public class BottleChargeTests
    {
        static GameEngine Engine(int capacity = 5)
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0,
                HandLimit = 0, MaxMoveSteps = 0, BottleCapacity = capacity }, 12);
            engine.Start();
            engine.EndPreparationNow();
            return engine;
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(10)]
        public void 集齐当前门槛才发一张对应牌_事件先充能后发牌(int capacity)
        {
            var engine = Engine(capacity);
            engine.AddBottleCharge(Element.Water, capacity - 1);
            Assert.AreEqual(capacity - 1, engine.Bottle.Count(Element.Water));
            Assert.IsEmpty(engine.Hand);
            engine.AddBottleCharge(Element.Water, 1);
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water));
            Assert.AreEqual(1, engine.Hand.Count);
            Assert.AreEqual(Element.Water, engine.Hand[0].Element);
            var events = engine.History.SelectMany(step => step.Events)
                .Where(e => e.Type == GameEventType.BottleFill || e.Type == GameEventType.GainCard).ToArray();
            Assert.AreEqual(GameEventType.BottleFill, events[events.Length - 2].Type);
            Assert.AreEqual(capacity, events[events.Length - 2].BottleCount);
            Assert.AreEqual(engine.Hand[0].Id, events.Last().CardId);
        }

        [Test]
        public void 一次超过两个门槛_发两张牌且保留余数()
        {
            var engine = Engine();
            engine.AddBottleCharge(Element.Fire, 12);
            Assert.AreEqual(2, engine.Hand.Count);
            Assert.IsTrue(engine.Hand.All(card => card.Element == Element.Fire));
            Assert.AreEqual(2, engine.Bottle.Count(Element.Fire));
            Assert.AreEqual(2, engine.Bottle.TotalCardsGranted);
        }

        [Test]
        public void 当回合临时门槛减少_兑换已有点数并在下回合恢复()
        {
            var engine = Engine(10);
            engine.AddBottleCharge(Element.Metal, 8);
            engine.SetBottleCapacityForRound(3);
            Assert.AreEqual(2, engine.Hand.Count);
            Assert.AreEqual(2, engine.Bottle.Count(Element.Metal));
            engine.BeginRound();
            Assert.AreEqual(10, engine.Bottle.Capacity);
            Assert.AreEqual(2, engine.Bottle.Count(Element.Metal));
        }

        [Test]
        public void 无效事件参数不能改变进度_重新开局清空进度()
        {
            var engine = Engine();
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.AddBottleCharge(Element.Water, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.AddBottleCharge((Element)99, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => engine.SetBottleCapacityForRound(0));
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water));
            engine.AddBottleCharge(Element.Water, 7);
            engine.Start();
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water));
            Assert.AreEqual(0, engine.Bottle.TotalCardsGranted);
        }
    }
}
