using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 复合元素 + 秘术牌在**表现层**的测试。
    ///
    /// 这里钉的是「点击逻辑」的表现部分：融合牌两段式选点时，棋盘上到底高亮哪些格子。
    /// 高亮是纯数据（一批 Pos），所以不需要进 Play 就能验证。
    ///
    /// 完整的「点手牌 → 点元素1 → 点元素2 → 右键取消」状态机在 GameSession 里，
    /// 它把「该高亮哪些格子」全部委托给引擎的 CellsForFusion* / CellsForDecompose，
    /// 那几个方法在 CompositeElementTests 里已经逐条钉死了。
    /// </summary>
    public class CompositeViewTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        /// <summary>
        /// 表现层用例基本都要那一层玩法，所以默认把复合元素总开关打开。
        ///
        /// ⚠ 光勾总开关**不够**：运行时和 <see cref="GameEngine.TryPlayFusionCard"/> 还会查
        /// <see cref="Rules.SpecialCardPool"/>（面板上勾开关时会顺手补成两张都要，代码里不会自动补），
        /// 池子留空的话「打出融合牌」会被拒成「本关没有启用融合牌」。这里显式写上，和
        /// CompositeElementTests.BaseRules 保持一致。
        /// </summary>
        static Rules BaseRules(int size = 8)
        {
            return new Rules
            {
                BoardWidth = size, BoardHeight = size,
                CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 0,
                UseCompositeElements = true,
                SpecialCardPool = SpecialCardMask.All,
            };
        }

        static BoardView MakeView(GameEngine engine, string name)
        {
            var root = new GameObject(name);
            var view = root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();
            return view;
        }

        [Test]
        public void 表现层_复合元素有自己的显示对象_名字配色和光环都不一样()
        {
            var rules = BaseRules();
            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Fire, new Pos(1, 1));
            engine.BeginRound();
            engine.PlaceComposite(CompositeKind.SpiritSpring, new Pos(4, 4));

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            // 不变量：表现对象数 = 元素数 + 复合元素数
            Assert.AreEqual(engine.CreepCount + engine.CompositeCount, view.CreepObjectCount);
            Assert.AreEqual(1, view.CompositeObjectCount);
            Assert.AreEqual(engine.CreepCount, view.CreepViewAt(new Pos(1, 1)) != null ? 1 : 0);

            var compositeView = view.CompositeViewAt(new Pos(4, 4));
            Assert.IsNotNull(compositeView, "复合元素必须有自己的显示对象");
            Assert.IsTrue(compositeView.IsComposite);
            Assert.AreEqual(CompositeKind.SpiritSpring, compositeView.CompositeKind);
            Assert.AreEqual("灵泉", compositeView.Label.text, "牌面上要写复合元素的名字");
            Assert.AreEqual(CompositeDefs.Tint(CompositeKind.SpiritSpring), compositeView.Body.color,
                "用复合元素的专用配色，不能和五行配色混");
            Assert.IsNotNull(compositeView.Ring, "复合元素要有一圈光环，和普通元素一眼区分");
            Assert.IsTrue(compositeView.Ring.enabled);

            // 复合元素那格的「元素」查询仍然是空的（At 只返回元素小怪）
            Assert.IsNull(view.CreepViewAt(new Pos(4, 4)));
            Assert.IsNotNull(view.OccupantViewAt(new Pos(4, 4)), "OccupantViewAt 才拿得到它");

            // 顺带把棋盘挪一下：复合元素也必须跟着格子走（和普通小怪同一条路径）
            view.transform.position = new Vector3(3f, -1.5f, 0f);
            view.Sync();
            var cell = FindCell(view, new Pos(4, 4));
            Assert.IsNotNull(cell);
            Assert.Less((compositeView.transform.position - cell.transform.position).magnitude, 1e-4f,
                "棋盘挪动后复合元素必须仍然落在它的格子上");
        }

        [Test]
        public void 表现层_复合元素消失后显示对象被真的销毁()
        {
            var rules = BaseRules();
            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.BeginRound();
            engine.PlaceCreep(Element.Metal, new Pos(0, 0));
            engine.PlaceCreep(Element.Water, new Pos(1, 0));
            engine.PlaceComposite(CompositeKind.Ore, new Pos(4, 4));

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            var vanishing = view.CompositeViewAt(new Pos(4, 4));
            Assert.IsNotNull(vanishing);
            var vanishingGo = vanishing.gameObject;
            Assert.AreEqual(3, view.CreepObjectCount, "2 个元素 + 1 个复合元素");

            // 用分解的正常路径把复合元素拆掉
            var composite = engine.CompositeAt(new Pos(4, 4));
            Assert.IsNotNull(engine.DecomposeComposite(composite, null, countAsPlayed: false));

            view.Sync();

            Assert.AreEqual(0, engine.CompositeCount);
            Assert.AreEqual(engine.CreepCount + engine.CompositeCount, view.CreepObjectCount,
                "对账之后表现对象数必须跟上");
            Assert.IsNull(view.CompositeViewAt(new Pos(4, 4)));
            Assert.IsNull(view.OccupantViewAt(new Pos(4, 4)));
            // 必须写成 == null 走 UnityEngine.Object 的重载：只丢引用、不销毁是查不出来的
            Assert.IsTrue(vanishingGo == null, "复合元素的显示对象必须被真的销毁");
            AssertNoCellLeftovers(view);
        }

        [Test]
        public void 高亮_融合牌两段式选点该亮哪些格子()
        {
            var rules = BaseRules(6);
            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.BeginRound();
            engine.PlaceCreep(Element.Metal, new Pos(0, 0));
            engine.PlaceCreep(Element.Water, new Pos(1, 0));
            engine.PlaceCreep(Element.Metal, new Pos(5, 5)); // 孤立，配不上

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            // 第一段：只有「真的存在有效配对」的两格该亮
            var first = engine.CellsForFusionFirst();
            view.RefreshHighlights(first, Pos.None);
            CollectionAssert.AreEquivalent(new[] { new Pos(0, 0), new Pos(1, 0) }, first);
            AssertHighlighted(view, new Pos(0, 0), true);
            AssertHighlighted(view, new Pos(1, 0), true);
            AssertHighlighted(view, new Pos(5, 5), false, "孤立的那只金不该被高亮");

            // 第二段：点了金(0,0) 之后，只有水(1,0) 该亮；金(0,0) 自己是「已选中」的暖色
            view.RefreshHighlights(engine.CellsForFusionSecond(new Pos(0, 0)), new Pos(0, 0));
            AssertHighlighted(view, new Pos(1, 0), true);
            AssertHighlighted(view, new Pos(0, 0), true, "元素1 自己是「已选中」的高亮");
            AssertHighlighted(view, new Pos(5, 5), false);

            // 右键取消：所有高亮清掉
            view.ClearHighlights();
            AssertHighlighted(view, new Pos(0, 0), false);
            AssertHighlighted(view, new Pos(1, 0), false);
            AssertHighlighted(view, new Pos(5, 5), false);
        }

        [Test]
        public void 高亮_分解牌只亮场上的复合元素()
        {
            var rules = BaseRules(6);
            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.BeginRound();
            engine.PlaceCreep(Element.Fire, new Pos(0, 0));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(3, 3));
            engine.PlaceComposite(CompositeKind.Lava, new Pos(4, 4));

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            var cells = engine.CellsForDecompose();
            CollectionAssert.AreEquivalent(new[] { new Pos(3, 3), new Pos(4, 4) }, cells,
                "分解牌的目标只有复合元素占的格子");

            view.RefreshHighlights(cells, Pos.None);
            AssertHighlighted(view, new Pos(3, 3), true);
            AssertHighlighted(view, new Pos(4, 4), true);
            AssertHighlighted(view, new Pos(0, 0), false, "元素小怪不是分解牌的目标");

            // 拆掉一个之后，高亮跟着数据走
            engine.DecomposeComposite(engine.CompositeAt(new Pos(3, 3)), null, countAsPlayed: false);
            view.Sync();
            view.RefreshHighlights(engine.CellsForDecompose(), Pos.None);
            AssertHighlighted(view, new Pos(3, 3), false);
            AssertHighlighted(view, new Pos(4, 4), true);
        }

        [Test]
        public void 表现层_融合牌出完之后手牌和棋盘同时对账()
        {
            var rules = BaseRules(8);
            rules.CompositeIngredientsPerRound = 1;

            var engine = new GameEngine(rules, 7);
            engine.Start();
            engine.ClearBoard();
            engine.ClearHandForTest();
            engine.AddSpecialCardForTest(CardKind.FusionCard);
            engine.AddCardForTest(Element.Metal);
            engine.FreezeHandLimitForTest();
            engine.EndPreparationNow();
            engine.PlaceCreep(Element.Metal, new Pos(2, 2));
            engine.PlaceCreep(Element.Water, new Pos(2, 3)); // 金(2,2) 的**上**边：十字相邻

            _root = new GameObject("Root");
            var board = _root.AddComponent<BoardView>();
            board.Setup(engine);

            var handRoot = new GameObject("HandRoot");
            handRoot.transform.SetParent(_root.transform, false);
            var hand = handRoot.AddComponent<HandView>();
            hand.Setup(engine);

            board.Sync();
            Assert.AreEqual(2, hand.CardObjectCount, "融合牌 + 金牌");

            var fusion = engine.Hand[0];
            Assert.IsTrue(fusion.IsFusionCard);
            Assert.IsNotNull(engine.TryPlayFusionCard(fusion.Id, new Pos(2, 2), new Pos(2, 3)), engine.FailReason);

            board.Sync();
            hand.Sync();

            Assert.AreEqual(engine.CreepCount + engine.CompositeCount, board.CreepObjectCount,
                "棋盘表现对象数必须等于元素数 + 复合元素数");
            Assert.AreEqual(1, board.CompositeObjectCount);
            Assert.AreEqual(engine.Hand.Count, hand.CardObjectCount, "融合牌被打出去之后手牌对象要少一个");
            Assert.AreEqual(1, hand.CardObjectCount);
            AssertNoCellLeftovers(board);

            // 融合牌的卡面：字是「融合」，没有牌面元素的含义
            var cardView = hand.Cards[0];
            Assert.IsFalse(cardView.IsSpecialCard, "剩下的是金牌");
            Assert.AreEqual(Element.Metal, cardView.Element);
        }

        [Test]
        public void 手牌_秘术牌的卡面字与配色和元素牌区分开()
        {
            var rules = BaseRules(8);
            var engine = new GameEngine(rules, 7);
            engine.Start();
            engine.ClearBoard();
            engine.ClearHandForTest();
            engine.AddSpecialCardForTest(CardKind.FusionCard);
            engine.AddSpecialCardForTest(CardKind.DecomposeCard);
            engine.AddCardForTest(Element.Fire);
            engine.FreezeHandLimitForTest();

            _root = new GameObject("HandPrefabRoot");
            var hand = _root.AddComponent<HandView>();
            hand.Setup(engine);

            Assert.AreEqual(3, hand.CardObjectCount);

            var fusionView = ViewOf(hand, engine, CardKind.FusionCard);
            var decomposeView = ViewOf(hand, engine, CardKind.DecomposeCard);

            Assert.IsTrue(fusionView.IsSpecialCard);
            Assert.AreEqual("融合", LabelOf(fusionView));
            Assert.IsTrue(decomposeView.IsSpecialCard);
            Assert.AreEqual("分解", LabelOf(decomposeView));

            // 三张牌的字都不一样，而且秘术牌的字比元素牌小（两个字要装得下）
            var fusionText = fusionView.GetComponentInChildren<TextMesh>();
            var elementText = ElementCardText(hand);
            Assert.Less(fusionText.transform.localScale.x, elementText.transform.localScale.x,
                "两个字的秘术牌要把字形缩小才装得进牌面");
        }

        // ------------------------------------------------------------------ 助手

        static CardView ViewOf(HandView hand, GameEngine engine, CardKind kind)
        {
            foreach (var card in engine.Hand)
                if (card.Kind == kind) return hand.FindView(card.Id);
            return null;
        }

        static string LabelOf(CardView view)
        {
            var text = view.GetComponentInChildren<TextMesh>();
            return text != null ? text.text : null;
        }

        static TextMesh ElementCardText(HandView hand)
        {
            foreach (var card in hand.Cards)
                if (!card.IsSpecialCard) return card.GetComponentInChildren<TextMesh>();
            return null;
        }

        static void AssertHighlighted(BoardView view, Pos pos, bool expected, string message = null)
        {
            var cell = FindCell(view, pos);
            Assert.IsNotNull(cell, $"找不到格子 {pos}");
            Assert.IsNotNull(cell.Highlight, "格子必须有高亮层");

            bool on = cell.Highlight.color.a > 0.01f;
            Assert.AreEqual(expected, on,
                message ?? $"{pos} 的高亮状态不对（期望 {(expected ? "亮" : "不亮")}，实际 alpha={cell.Highlight.color.a}）");
        }

        static CellView FindCell(BoardView view, Pos pos)
        {
            foreach (var c in view.GetComponentsInChildren<CellView>())
                if (c.Pos == pos) return c;
            return null;
        }

        /// <summary>格子下面不许出现残留对象（和 BoardViewTests 里那条不变量同一套检查）。</summary>
        static void AssertNoCellLeftovers(BoardView view)
        {
            foreach (var cell in view.GetComponentsInChildren<CellView>())
            {
                int sprites = cell.GetComponentsInChildren<SpriteRenderer>().Length;
                Assert.LessOrEqual(sprites, 2, $"{cell.Pos} 格子下出现了残留色块");
                foreach (Transform child in cell.transform)
                    Assert.IsFalse(child.name.StartsWith("Creep_"), $"{cell.Pos} 格子下还挂着 {child.name}");
            }

            int named = 0;
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Creep_")) named++;

            Assert.AreEqual(view.CreepObjectCount, named,
                "场景里的 Creep_* 对象数必须和 BoardView 记录的一致（多出来的是孤儿对象）");
        }
    }
}
