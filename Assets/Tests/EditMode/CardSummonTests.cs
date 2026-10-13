using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 图1 玩家行动阶段的第 2 条：**「玩家可以对空的棋盘格使用卡牌，可以在该格召唤该元素小怪」**。
    ///
    /// 这一套钉的是「元素牌打在空格上」这条新玩法，以及它和原有玩法的边界：
    ///   · 空格 → 在该格召唤 1 个**牌面元素**的小怪（位置就是点的那一格）
    ///   · 它**不发生任何反应**：不转换、不生成、不喂小瓶子、不通知关卡机制（不是一次相遇）
    ///   · 但它是**一次正常出牌**：消耗这张牌、占每回合出牌上限
    ///   · 墙格 / 复合元素占着的格子 / 越界都不是空格，两种用法都用不了
    ///   · 关掉这条规则的开关（<see cref="Rules.EmptyCellCard"/> = Reject）就是加它之前的行为
    /// </summary>
    public class CardSummonTests
    {
        // ------------------------------------------------------------ 工具

        /// <summary>只摆测试自己给的东西：小怪不动、起手牌自己画。</summary>
        static Rules BaseRules(int size = 8, EmptyCellCardMode emptyCell = EmptyCellCardMode.Summon)
            => new Rules
            {
                BoardWidth = size,
                BoardHeight = size,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                MaxMoveSteps = 0,
                EmptyCellCard = emptyCell,
            };

        /// <summary>
        /// 直达玩家行动阶段的引擎：手牌是画出来的，棋盘上的小怪在**进入行动阶段之后**才摆，
        /// 这样坐标不会被准备阶段的移动打乱。
        /// </summary>
        static GameEngine Rig(Rules rules, Element[] hand, params (Element element, Pos pos)[] pieces)
        {
            var engine = new GameEngine(rules, 20240607);
            engine.Start();
            engine.ClearBoard();

            engine.ClearHandForTest();
            foreach (var e in hand) engine.AddCardForTest(e);
            engine.FreezeHandLimitForTest(); // 别让抽牌阶段把画好的手牌冲掉
            engine.EndPreparationNow();
            engine.SetupBottleForTest(0);    // 不测瓶子，避免满瓶发牌干扰

            foreach (var (element, pos) in pieces) engine.PlaceCreep(element, pos);
            return engine;
        }

        static Card WaterCardInHand(GameEngine engine) => engine.Hand.First(c => c.Element == Element.Water);

        // ------------------------------------------------------------ 召唤本身

        [Test]
        public void 召唤_元素牌打在空格上_在该格召唤牌面元素()
        {
            var engine = Rig(BaseRules(), new[] { Element.Water }, (Element.Fire, new Pos(2, 2)));
            var card = WaterCardInHand(engine);

            var record = engine.TryPlayCard(card.Id, new Pos(5, 5));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.IsTrue(record.IsSummon, "这一步要标成召唤（表现层 / 调试靠它区分）");
            Assert.IsTrue(record.IsPlayerCard);
            Assert.AreEqual(new Pos(5, 5), record.To, "召唤到点的那一格");
            Assert.AreEqual(Pos.None, record.From, "牌不是从棋盘上某一格来的");
            Assert.IsFalse(record.CreepWentAway);

            Assert.AreEqual(0, engine.Hand.Count, "召唤也是一次出牌 → 手牌 -1");
            Assert.AreEqual(1, engine.CardsPlayedThisRound, "占本回合出牌次数");

            var summoned = engine.Board.At(new Pos(5, 5));
            Assert.IsNotNull(summoned, "空格上应该出现一只小怪");
            Assert.AreEqual(Element.Water, summoned.Element, "召唤出来的必须是牌面元素");

            Assert.AreEqual(2, engine.CreepCount, "原有那只火还在，场上共 2 只");

            // 事件里要有一条 Spawn（表现层靠它播「弹出来」的动画）
            var spawn = record.Events.FirstOrDefault(e => e.Type == GameEventType.Spawn);
            Assert.IsNotNull(spawn, "召唤必须记一条 Spawn 事件");
            Assert.AreEqual(summoned.Uid, spawn.Uid);
            Assert.AreEqual(new Pos(5, 5), spawn.To);
            StringAssert.Contains("召唤", spawn.Text);
        }

        [Test]
        public void 召唤_不发生任何反应_不动旁边的元素_也不喂小瓶子()
        {
            // 目标格上下左右都摆上会和「水」反应的元素：相生（金）、相克（火）。
            // 召唤不是相遇，所以它们必须原封不动。
            var engine = Rig(BaseRules(), new[] { Element.Water },
                (Element.Metal, new Pos(4, 5)),
                (Element.Fire, new Pos(6, 5)),
                (Element.Metal, new Pos(5, 4)));

            var card = WaterCardInHand(engine);
            Assert.IsNotNull(engine.TryPlayCard(card.Id, new Pos(5, 5)), engine.FailReason);

            Assert.AreEqual(Element.Metal, engine.Board.At(new Pos(4, 5)).Element, "旁边的金不该被相生");
            Assert.AreEqual(Element.Fire, engine.Board.At(new Pos(6, 5)).Element, "旁边的火不该被相克");
            Assert.AreEqual(Element.Metal, engine.Board.At(new Pos(5, 4)).Element);

            Assert.AreEqual(0, engine.TotalTransformed, "召唤不是相生");
            Assert.AreEqual(0, engine.TotalRemoved, "召唤不消灭任何元素");
            Assert.AreEqual(0, engine.TotalSpawned, "召唤不算「反应生成」，单独记账");
            Assert.AreEqual(1, engine.TotalSummoned);
            Assert.AreEqual(1, engine.SummonedOf(Element.Water));

            foreach (var e in ElementDefs.All)
                Assert.AreEqual(0, engine.Bottle.Count(e), $"{ElementDefs.Name(e)}瓶子不该因为召唤而 +1");
        }

        [Test]
        public void 召唤_不通知关卡机制_因为它不是一次相遇()
        {
            var engine = Rig(BaseRules(), new[] { Element.Water });
            var rule = new CountingRule();
            engine.AttachRule(rule);

            Assert.IsNotNull(engine.TryPlayCard(WaterCardInHand(engine).Id, new Pos(5, 5)), engine.FailReason);

            Assert.AreEqual(0, rule.Encounters, "召唤没有相遇，机制不该收到相遇通知");
        }

        [Test]
        public void 召唤_不影响打在元素格上的原有路径()
        {
            var engine = Rig(BaseRules(), new[] { Element.Water, Element.Water }, (Element.Fire, new Pos(2, 2)));

            // 第一张：空格召唤
            Assert.IsNotNull(engine.TryPlayCard(engine.Hand[0].Id, new Pos(5, 5)), engine.FailReason);
            // 第二张：还是老玩法 —— 水牌打火小怪，火消失
            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(2, 2));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.IsFalse(record.IsSummon, "打在元素格上是反应，不是召唤");
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "火应该被水牌清掉");
            Assert.AreEqual(1, engine.TotalRemoved);
            Assert.AreEqual(1, engine.Bottle.Count(Element.Fire), "相克照旧喂小瓶子");
            Assert.AreEqual(1, engine.TotalSummoned, "只有第一次是召唤");
            Assert.AreEqual(2, engine.CardsPlayedThisRound, "两次出牌各占一次");
        }

        // ------------------------------------------------------------ 边界

        [Test]
        public void 召唤_墙上与复合元素上都不能用()
        {
            var rules = BaseRules();
            rules.UseCompositeElements = true;   // PlaceComposite 是沙盒接口，不看总开关；这里顺手开上更贴近实战
            rules.SpecialCardPool = SpecialCardMask.All;

            var engine = Rig(rules, new[] { Element.Water, Element.Water });
            engine.Board.SetBlocked(new Pos(0, 0), true);
            engine.PlaceComposite(CompositeKind.Ember, new Pos(1, 1));

            var blocked = engine.TryPlayCard(engine.Hand[0].Id, new Pos(0, 0));
            Assert.IsNull(blocked, "墙格不是空格，不能召唤");
            StringAssert.Contains("墙", engine.FailReason);

            var composite = engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1));
            Assert.IsNull(composite, "复合元素占着的格子不是空格");
            StringAssert.Contains("复合元素", engine.FailReason);

            Assert.AreEqual(2, engine.Hand.Count, "两次失败都不该消耗手牌");
            Assert.AreEqual(0, engine.CardsPlayedThisRound);
            Assert.AreEqual(0, engine.TotalSummoned);
            Assert.AreEqual(1, engine.CompositeCount, "复合元素原封不动");
        }

        [Test]
        public void 召唤_越界被拒绝()
        {
            var engine = Rig(BaseRules(), new[] { Element.Water });

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(8, 8));

            Assert.IsNull(record, "棋盘外的格子不是合法目标");
            StringAssert.Contains("棋盘", engine.FailReason);
            Assert.AreEqual(1, engine.Hand.Count, "失败不消耗手牌");
        }

        [Test]
        public void 召唤_池外元素牌不能召唤()
        {
            // 元素池只有金，牌池却给了水（故意配错）→ 连召唤都不该放行，
            // 否则「本关只出现金」这个约定会被凭空冒出来的水破坏。
            var rules = BaseRules();
            rules.ElementPool = ElementMask.Metal;
            rules.CardPool = ElementMask.Metal | ElementMask.Water;

            var engine = Rig(rules, new[] { Element.Water });
            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(5, 5));

            Assert.IsNull(record);
            StringAssert.Contains("本关可用元素", engine.FailReason);
            Assert.AreEqual(1, engine.Hand.Count, "失败不消耗手牌");
            Assert.AreEqual(0, engine.CreepCount, "空格上不该冒出池外元素");
        }

        [Test]
        public void 召唤_占每回合出牌上限()
        {
            var rules = BaseRules();
            rules.MaxCardsPerRound = 1;

            var engine = Rig(rules, new[] { Element.Water, Element.Water });

            Assert.IsNotNull(engine.TryPlayCard(engine.Hand[0].Id, new Pos(5, 5)), engine.FailReason);
            Assert.AreEqual(0, engine.PlaysRemaining);

            var second = engine.TryPlayCard(engine.Hand[0].Id, new Pos(6, 6));
            Assert.IsNull(second, "本回合出牌次数已经用完");
            StringAssert.Contains("上限", engine.FailReason);
            Assert.AreEqual(1, engine.Hand.Count, "被拒绝时不该消耗手牌");
            Assert.AreEqual(1, engine.TotalSummoned);
        }

        // ------------------------------------------------------------ 该高亮哪些格子

        [Test]
        public void 高亮_元素格加空格_不含墙与复合元素()
        {
            var rules = BaseRules();
            rules.UseCompositeElements = true;
            rules.SpecialCardPool = SpecialCardMask.All;

            var engine = Rig(rules, new[] { Element.Water },
                (Element.Fire, new Pos(2, 2)),   // 水克火 → 能打
                (Element.Water, new Pos(4, 4)),  // 同元素 → 打不出去
                (Element.Earth, new Pos(5, 5))); // 土克水 → 牌面元素是输家，打不出去
            engine.Board.SetBlocked(new Pos(0, 0), true);
            engine.PlaceComposite(CompositeKind.Ember, new Pos(1, 1));

            var cells = engine.CellsForElementCard(WaterCardInHand(engine));

            CollectionAssert.Contains(cells, new Pos(2, 2), "能发生反应的元素格要亮");
            CollectionAssert.DoesNotContain(cells, new Pos(4, 4), "同元素之间不发生反应，不该亮");
            CollectionAssert.DoesNotContain(cells, new Pos(5, 5), "相克里牌面元素是输家，打不出去，不该亮");
            CollectionAssert.DoesNotContain(cells, new Pos(0, 0), "墙格不是空格");
            CollectionAssert.DoesNotContain(cells, new Pos(1, 1), "复合元素占着的格子不是空格");
            CollectionAssert.Contains(cells, new Pos(7, 7), "空格可以召唤，要亮");

            // 空格数 = 总格数 − 3 只小怪 − 1 堵墙 − 1 个复合元素，再加上那 1 个能反应的元素格
            Assert.AreEqual(64 - 3 - 1 - 1 + 1, cells.Count, "高亮格数不对");
            CollectionAssert.AllItemsAreUnique(cells, "同一个格子不该被高亮两次");
        }

        [Test]
        public void 高亮_关掉召唤之后空格不再亮()
        {
            var engine = Rig(BaseRules(emptyCell: EmptyCellCardMode.Reject), new[] { Element.Water },
                (Element.Fire, new Pos(2, 2)));

            var cells = engine.CellsForElementCard(WaterCardInHand(engine));

            CollectionAssert.AreEqual(new[] { new Pos(2, 2) }, cells, "只剩能发生反应的那一格");
            Assert.IsFalse(engine.CanSummonOnEmptyCell);
            Assert.IsFalse(engine.CanSummonCardAt(WaterCardInHand(engine), new Pos(5, 5)));
        }

        [Test]
        public void 高亮_秘术牌不参与元素牌那套高亮()
        {
            var rules = BaseRules();
            rules.UseCompositeElements = true;
            rules.SpecialCardPool = SpecialCardMask.All;

            var engine = Rig(rules, new[] { Element.Water });
            var fusion = engine.AddSpecialCardForTest(CardKind.FusionCard);

            Assert.IsEmpty(engine.CellsForElementCard(fusion), "融合牌没有牌面元素，不该走元素牌的高亮");
        }

        // ------------------------------------------------------------ 表现层

        [Test]
        public void 表现层_选中元素牌时空格也高亮_召唤后出现显示对象()
        {
            var engine = Rig(BaseRules(), new[] { Element.Water, Element.Water }, (Element.Water, new Pos(4, 4)));
            var card = WaterCardInHand(engine);

            var root = new GameObject("summon-board");
            try
            {
                var view = root.AddComponent<BoardView>();
                view.Setup(engine);
                view.Sync();

                view.RefreshHighlights(card);

                AssertHighlighted(view, new Pos(0, 0), true, "空格是可召唤的目标，要亮");
                AssertHighlighted(view, new Pos(4, 4), false, "同元素小怪不是合法目标，不该亮");

                // 真的召唤一次：表现层 Sync 之后要出现这个 uid 的显示对象，且总数仍然对得上
                var summoned = engine.TryPlayCard(card.Id, new Pos(0, 0));
                Assert.IsNotNull(summoned, engine.FailReason);
                view.Sync();

                var creepView = view.CreepViewAt(new Pos(0, 0));
                Assert.IsNotNull(creepView, "召唤出来的小怪要有显示对象");
                Assert.AreEqual(engine.Board.At(new Pos(0, 0)).Uid, creepView.Uid, "显示对象要跟引擎里的 uid 对上");
                Assert.AreEqual(engine.CreepCount, view.CreepObjectCount, "表现对象数必须等于元素数");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------------ 面板摘要

        [Test]
        public void 摘要_写出空格出牌的玩法状态()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            try
            {
                level.levelId = "summon_summary";
                level.rules = new Rules();

                StringAssert.Contains("空格出牌 召唤", level.Summary());

                level.rules.EmptyCellCard = EmptyCellCardMode.Reject;
                StringAssert.Contains("空格出牌 拒绝", level.Summary());
                StringAssert.DoesNotContain("召唤", level.Summary(), "关掉之后摘要里不该再写「召唤」");
            }
            finally
            {
                Object.DestroyImmediate(level);
            }
        }

        // ------------------------------------------------------------ 助手

        static void AssertHighlighted(BoardView view, Pos pos, bool expected, string message)
        {
            CellView cell = null;
            foreach (var c in view.GetComponentsInChildren<CellView>())
                if (c.Pos == pos) cell = c;

            Assert.IsNotNull(cell, $"找不到格子 {pos}");
            Assert.IsNotNull(cell.Highlight, "格子必须有高亮层");

            bool on = cell.Highlight.color.a > 0.01f;
            Assert.AreEqual(expected, on, $"{message}（{pos} 实际 alpha={cell.Highlight.color.a}）");
        }

        /// <summary>只数相遇通知的机制替身。</summary>
        sealed class CountingRule : ILevelRule
        {
            public int Encounters;

            public string GoalDescription => null;
            public void OnLevelStart(GameEngine engine) { }
            public void OnRoundStart(GameEngine engine) { }
            public void OnEncounter(GameEngine engine, EncounterInfo info) => Encounters++;

            public bool TryExtraWin(GameEngine engine, out string message)
            {
                message = null;
                return false;
            }

            public bool TryExtraLose(GameEngine engine, out string message)
            {
                message = null;
                return false;
            }

            public GoalProgress GetGoalProgress(GameEngine engine) => default;
        }
    }
}
