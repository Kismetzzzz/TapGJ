using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;

namespace TapGJ.Tests
{
    /// <summary>
    /// 第二～四章需要的四项「代码能力」：
    ///   · `Rules.RequireAllWinRules` —— 多个机制之间 **AND / OR** 的切换
    ///   · `Bottle.CardsGranted(元素)` —— **分元素**的瓶产牌计数（第三章的四个关卡全靠它）
    ///   · `ConvertRectToElement` / `ConvertBlockToElement` —— 固定矩形 / 随机方块范围的转换（第四章）
    ///   · `LevelOutro` —— 剧情/过渡文本的跨场景通道
    /// </summary>
    public class ChapterSupportTests
    {
        sealed class FakeRule : ILevelRule
        {
            public bool Win;
            public string Description = "假目标";
            public int WinMessage;

            public string GoalDescription => Description;
            public void OnLevelStart(GameEngine engine) { }
            public void OnRoundStart(GameEngine engine) { }
            public void OnEncounter(GameEngine engine, EncounterInfo info) { }
            public bool TryExtraWin(GameEngine engine, out string message)
            {
                WinMessage++;
                message = Win ? "假胜利：条件达成" : null;
                return Win;
            }
            public bool TryExtraLose(GameEngine engine, out string message) { message = null; return false; }
            public GoalProgress GetGoalProgress(GameEngine engine) => default;
        }

        static Rules MakeRules(int size = 4, BottleMode bottle = BottleMode.Enabled) => new Rules
        {
            BoardWidth = size, BoardHeight = size,
            CreepsPerElement = 0, InitialHandSize = 0, HandLimit = 5, RoundLimit = 10,
            Goal = GoalSpec.Custom(), BottleMode = bottle, BottleCapacity = 5,
        };

        static GameEngine NewEngine(Rules rules = null)
        {
            var engine = new GameEngine(rules ?? MakeRules(), 11);
            engine.Start();
            return engine;
        }

        // ---------------------------------------------------------------- AND / OR

        [Test]
        public void AND模式_所有机制都满足才算赢()
        {
            var rules = MakeRules();
            rules.RequireAllWinRules = true;
            var engine = NewEngine(rules);

            var a = new FakeRule { Win = true };
            var b = new FakeRule { Win = false };
            engine.AttachRules(new[] { a, b });

            Assert.IsFalse(engine.CheckWinCondition(out _), "B 没满足 → 整体不算赢");

            b.Win = true;
            Assert.IsTrue(engine.CheckWinCondition(out var reason), "两个都满足 → 赢");
            StringAssert.Contains("假胜利", reason);
        }

        [Test]
        public void OR模式_默认任一满足就算赢()
        {
            var rules = MakeRules();
            Assert.IsFalse(rules.RequireAllWinRules, "默认必须是 OR，否则老关卡会全变难");

            var engine = NewEngine(rules);
            engine.AttachRules(new[]
            {
                new FakeRule { Win = false },
                new FakeRule { Win = true },
            });

            Assert.IsTrue(engine.CheckWinCondition(out _), "有一个满足就赢");
        }

        [Test]
        public void AND模式下_一个机制都没有时不算赢()
        {
            var rules = MakeRules();
            rules.RequireAllWinRules = true;
            var engine = NewEngine(rules);

            Assert.IsFalse(engine.CheckWinCondition(out _), "没有机制不能白送胜利");
        }

        // ---------------------------------------------------------------- 分元素瓶产牌

        [Test]
        public void 瓶产牌按元素分别记数()
        {
            var engine = NewEngine();

            for (int i = 0; i < 5; i++) engine.TryPlaceAndRemoveForTest(Element.Wood);

            Assert.AreEqual(1, engine.Bottle.CardsGranted(Element.Wood), "木瓶产了 1 张");
            Assert.AreEqual(0, engine.Bottle.CardsGranted(Element.Fire), "火瓶没动");
            Assert.AreEqual(1, engine.Bottle.TotalCardsGranted, "总数对得上");
        }

        [Test]
        public void 玉翁结账也计入分元素统计()
        {
            var engine = NewEngine();
            engine.SetupBottleForTest(4); // 五个瓶子各 4

            Assert.AreEqual(5, engine.ApplyBottleCapacity(3), "五个瓶子各结 1 张");
            foreach (var e in ElementDefs.All)
                Assert.AreEqual(1, engine.Bottle.CardsGranted(e), $"{ElementDefs.Name(e)}应该记 1 张");
        }

        [Test]
        public void 关瓶时不会记任何瓶产牌()
        {
            var engine = NewEngine(MakeRules(bottle: BottleMode.Disabled));
            for (int i = 0; i < 12; i++) engine.TryPlaceAndRemoveForTest(Element.Wood);

            Assert.AreEqual(0, engine.Bottle.CardsGranted(Element.Wood));
            Assert.AreEqual(0, engine.Bottle.TotalCardsGranted);
        }

        // ---------------------------------------------------------------- 固定 / 随机范围转换

        [Test]
        public void 固定矩形只作用在那块区域()
        {
            var engine = NewEngine(MakeRules(8));

            int changed = engine.ConvertRectToElement(Element.Fire, 2, 2, 3, 3);

            Assert.AreEqual(9, changed, "3×3 空棋盘 = 9 格");
            Assert.AreEqual(9, engine.Board.CountOf(Element.Fire));
            Assert.AreEqual(Element.Fire, engine.Board.At(new Pos(2, 2)).Element, "左下角在矩形里");
            Assert.AreEqual(Element.Fire, engine.Board.At(new Pos(4, 4)).Element, "右上角在矩形里");
            Assert.IsNull(engine.Board.At(new Pos(1, 2)), "左边一格不该被碰");
            Assert.IsNull(engine.Board.At(new Pos(5, 4)), "右边一格不该被碰");
        }

        [Test]
        public void 随机方块是完整的一块且不越界()
        {
            var engine = NewEngine(MakeRules(8));

            int changed = engine.ConvertBlockToElement(Element.Earth, 3);
            Assert.AreEqual(9, changed);

            int minX = 99, maxX = -1, minY = 99, maxY = -1;
            foreach (var c in engine.Board.Creeps())
            {
                Assert.AreEqual(Element.Earth, c.Element);
                Assert.GreaterOrEqual(c.Pos.X, 0); Assert.Less(c.Pos.X, 8);
                Assert.GreaterOrEqual(c.Pos.Y, 0); Assert.Less(c.Pos.Y, 8);
                minX = System.Math.Min(minX, c.Pos.X); maxX = System.Math.Max(maxX, c.Pos.X);
                minY = System.Math.Min(minY, c.Pos.Y); maxY = System.Math.Max(maxY, c.Pos.Y);
            }

            Assert.AreEqual(2, maxX - minX, "横向 3 格");
            Assert.AreEqual(2, maxY - minY, "纵向 3 格");
        }

        [Test]
        public void 范围转换遇到墙和复合元素会跳过()
        {
            var engine = NewEngine(MakeRules(6));
            engine.BlockCell(new Pos(2, 2), true);
            engine.PlaceComposite(CompositeKind.Lava, new Pos(3, 2));

            int changed = engine.ConvertRectToElement(Element.Fire, 2, 2, 2, 1);

            Assert.AreEqual(0, changed, "两格分别是墙和复合元素");
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire));
            Assert.IsTrue(engine.Board.HasComposite(new Pos(3, 2)), "复合元素没被拆");
        }

        // ---------------------------------------------------------------- 剧情通道

        [Test]
        public void 剧情文本的存取与清空()
        {
            LevelOutro.Clear();
            Assert.IsFalse(LevelOutro.HasText);

            LevelOutro.Set("第一章结束，下一章：复合元素");
            Assert.IsTrue(LevelOutro.HasText);
            StringAssert.Contains("复合元素", LevelOutro.Current);

            LevelOutro.Set("");
            Assert.IsFalse(LevelOutro.HasText, "空字符串等于没有剧情");
        }
    }
}
