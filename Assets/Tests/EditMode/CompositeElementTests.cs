using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;

namespace TapGJ.Tests
{
    /// <summary>
    /// 复合元素 + 秘术牌（融合牌 / 分解牌）的规则层测试。
    ///
    /// 这一套钉的是图2 上半部分那张表，以及用户口述的那几条交互约定：
    ///   · 5 种复合元素的配方 = 相生链上的 5 对相邻元素（顺序无关）
    ///   · 融合牌：**场上没有复合元素**时才能用；先点元素1、再点**相邻**的元素2；
    ///     合成出来的复合元素落在**元素1 的位置**；两个素材被消耗（**不喂小瓶子**）
    ///   · 复合元素的效果：当回合立刻生效一次，之后**每回合开始**生效一次
    ///   · 复合元素占格、不移动、不受元素反应影响；只有分解牌能动它
    ///   · 分解牌：拆掉复合元素，给回两张对应元素的元素牌
    ///   · 秘术牌的比例由 Rules 上的**权重**控制（这就是「比例可控」）
    /// </summary>
    public class CompositeElementTests
    {
        // ------------------------------------------------------------ 工具

        /// <summary>
        /// 只摆测试自己给的东西的引擎（随机铺盘和起手牌都关掉）。
        /// ⚠ **默认把复合元素总开关打开、并且两张秘术牌都选上**：这个文件里的用例几乎都要那一层玩法，
        /// 只有「总开关关掉时什么都不该发生」那几条才传 false。运行时**不会**自动补牌池
        /// （面板勾开关时才会顺手补），所以这里必须显式写出来。
        /// </summary>
        static Rules BaseRules(int size = 8, bool useComposites = true)
        {
            return new Rules
            {
                BoardWidth = size,
                BoardHeight = size,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                UseCompositeElements = useComposites,
                SpecialCardPool = useComposites ? SpecialCardMask.All : SpecialCardMask.None,
            };
        }

        /// <summary>开了复合元素、但秘术牌一张都没选（用来单独验总开关与校验）。</summary>
        static Rules CompositeRulesWithoutCards(int size = 8)
        {
            var rules = BaseRules(size);
            rules.SpecialCardPool = SpecialCardMask.None;
            return rules;
        }

        static GameEngine Rig(Rules rules, params (Element element, Pos pos)[] pieces)
        {
            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            foreach (var (element, pos) in pieces) engine.PlaceCreep(element, pos);
            engine.BeginRound();
            return engine;
        }

        /// <summary>
        /// 直达玩家行动阶段的引擎。
        ///
        /// ⚠ 两条都是为了让用例**完全可控**：
        ///   · MaxMoveSteps = 0：准备阶段的小怪不会乱走，摆好的坐标就是用例要的坐标
        ///   · 小怪在准备阶段跑完之后才摆、起手牌明确画出来（不靠随机抽）
        /// </summary>
        static GameEngine RigAtPlayerPhase(Rules rules, (int kind, Element element)[] hand,
            params (Element element, Pos pos)[] pieces)
        {
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 7);
            engine.Start();
            engine.ClearBoard();

            engine.ClearHandForTest();
            foreach (var (kind, element) in hand)
            {
                if (kind == FusionCardSpec) engine.AddSpecialCardForTest(CardKind.FusionCard);
                else if (kind == DecomposeCardSpec) engine.AddSpecialCardForTest(CardKind.DecomposeCard);
                else engine.AddCardForTest(element);
            }

            engine.FreezeHandLimitForTest();
            engine.EndPreparationNow();
            if (engine.Phase == GamePhase.PlayerAction) engine.SetupBottleForTest(0);

            // 摆盘放在最后：这时已经没有任何自动移动会打乱坐标
            foreach (var (element, pos) in pieces) engine.PlaceCreep(element, pos);
            return engine;
        }

        /// <summary>把胜利条件换成一个永远不成立的，用来观察「回合是怎么推进的」而不被提前判胜打断。</summary>
        static void UseNeverWinningGoal(Rules rules)
        {
            rules.Goal = new GoalSpec { Kind = GoalKind.ElementAtMost, Target = Element.Water, TargetCount = -1 };
        }
        const int FusionCardSpec = 1;
        const int DecomposeCardSpec = 2;
        const int ElementCardSpec = 0;

        static (int, Element) Fusion() => (FusionCardSpec, Element.Metal);
        static (int, Element) Decompose() => (DecomposeCardSpec, Element.Metal);
        static (int, Element) Elemental(Element e) => (ElementCardSpec, e);

        static Card FusionCardInHand(GameEngine engine)
            => engine.Hand.FirstOrDefault(c => c.IsFusionCard);

        static Card DecomposeCardInHand(GameEngine engine)
            => engine.Hand.FirstOrDefault(c => c.IsDecomposeCard);

        // ------------------------------------------------------------ 配方表

        [Test]
        public void 配方表_五种复合元素都是相生链上相邻的一对()
        {
            // 图2 写的：烬(木+火)、熔岩(火+土)、矿晶(土+金)、寒钢(金+水)、灵泉(水+木)
            Assert.IsTrue(FusionRecipes.TryGetKind(Element.Wood, Element.Fire, out var ember));
            Assert.AreEqual(CompositeKind.Ember, ember);
            Assert.IsTrue(FusionRecipes.TryGetKind(Element.Fire, Element.Earth, out var lava));
            Assert.AreEqual(CompositeKind.Lava, lava);
            Assert.IsTrue(FusionRecipes.TryGetKind(Element.Earth, Element.Metal, out var ore));
            Assert.AreEqual(CompositeKind.Ore, ore);
            Assert.IsTrue(FusionRecipes.TryGetKind(Element.Metal, Element.Water, out var steel));
            Assert.AreEqual(CompositeKind.ColdSteel, steel);
            Assert.IsTrue(FusionRecipes.TryGetKind(Element.Water, Element.Wood, out var spring));
            Assert.AreEqual(CompositeKind.SpiritSpring, spring);
        }

        [Test]
        public void 配方表_顺序无关_同元素与相克对都不能合成()
        {
            Assert.IsTrue(FusionRecipes.CanFuse(Element.Fire, Element.Wood), "木+火 不分先后");
            Assert.IsTrue(FusionRecipes.CanFuse(Element.Wood, Element.Fire));
            Assert.IsTrue(FusionRecipes.CanFuse(Element.Metal, Element.Earth), "土+金 = 矿晶");
            Assert.IsTrue(FusionRecipes.CanFuse(Element.Earth, Element.Metal));

            Assert.IsFalse(FusionRecipes.CanFuse(Element.Metal, Element.Metal), "同元素不能合成");
            Assert.IsFalse(FusionRecipes.CanFuse(Element.Metal, Element.Wood), "金+木 不是配方（那是相克对）");
            Assert.IsFalse(FusionRecipes.CanFuse(Element.Water, Element.Fire), "水+火 不是配方");
            Assert.IsFalse(FusionRecipes.CanFuse(Element.Wood, Element.Earth), "木+土 不是配方");
        }

        [Test]
        public void 配方表_每种复合元素的两个组成元素都在配方里()
        {
            foreach (var kind in CompositeDefs.All)
            {
                var def = CompositeDefs.Of(kind);
                Assert.IsTrue(FusionRecipes.TryGetKind(def.A, def.B, out var back), $"{def.Name} 的配方查不回来");
                Assert.AreEqual(kind, back);
                Assert.AreNotEqual(def.A, def.B, $"{def.Name} 的两种组成元素不能相同");
                Assert.AreEqual(2, def.Ingredients.Length);
            }
        }

        [Test]
        public void 元素池_只有一种元素时凑不出任何配方()
        {
            Assert.IsFalse(FusionRecipes.AnyPairAvailable(ElementMask.Metal), "只有金 → 没有配方");
            Assert.IsTrue(FusionRecipes.AnyPairAvailable(ElementMask.Metal | ElementMask.Water), "金水 → 寒钢");
            Assert.IsTrue(FusionRecipes.AnyPairAvailable(ElementMask.All));

            // 金、木、火：金木是相克对、木火是配方 → 有配方
            Assert.IsTrue(FusionRecipes.AnyPairAvailable(ElementMask.Metal | ElementMask.Wood | ElementMask.Fire),
                "木火是一对配方");
            // 金、水、火：金水是配方
            Assert.IsTrue(FusionRecipes.AnyPairAvailable(ElementMask.Metal | ElementMask.Water | ElementMask.Fire),
                "金水是一对配方");
        }

        // ------------------------------------------------------------ 复合元素的基本行为

        [Test]
        public void 复合元素_占格不移动且不会被元素反应吃掉()
        {
            // ⚠ 复合元素必须在 BeginRound **之前**摆上：它一上场就会先「产生」一批元素，
            //    那批元素会把这条用例想数的数量搅乱。
            var rules = BaseRules();
            rules.MaxMoveSteps = 3;
            rules.CompositeIngredientsPerRound = 1; // 每回合只产 1 份，方便数数量

            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Fire, new Pos(0, 0));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(1, 0));
            engine.BeginRound();

            // 回合开始：复合元素先产出 木+1、火+1
            var production = engine.AdvancePreparation();
            Assert.IsNotNull(production);
            Assert.IsTrue(production.IsCompositeEffect, "回合开始应该先让复合元素产生一次");

            int afterProduction = engine.CreepCount;
            Assert.Greater(afterProduction, 1, "复合元素产出了元素");

            Assert.AreEqual(1, engine.CompositeCount);
            Assert.IsFalse(engine.Board.IsEmpty(new Pos(1, 0)), "复合元素占着的格子不算空格");
            Assert.IsNull(engine.Board.At(new Pos(1, 0)), "At 只返回元素小怪，复合元素那格是 null");
            Assert.IsNotNull(engine.Board.OccupantAt(new Pos(1, 0)), "OccupantAt 才能拿到占格者");

            // 接下来每只小怪都朝 +X 走 3 格；(0,0) 那只右边紧邻就是复合元素，
            // 必须走不进去、也不发生任何反应。
            // ⚠ 这里顺带钉一个真实踩过的 bug：移动判定如果只看 Board.At() == null，
            //    就会把复合元素那一格当成空格，直接走进去（然后 Put 抛异常）。
            for (int round = 0; round < 3; round++)
            {
                engine.SetNextRandomValues(3, 1);
                while (engine.Phase == GamePhase.Preparation && engine.AdvancePreparation() != null) { }

                Assert.IsNotNull(engine.Board.At(new Pos(0, 0)), "第 " + (round + 1) + " 回合：(0,0) 上的小怪被挡住了，没走掉");
                Assert.AreEqual(1, engine.CompositeCount, "复合元素不会因为被撞而消失");
                Assert.AreEqual(CompositeKind.Ember, engine.CompositeAt(new Pos(1, 0)).Kind);
                // 最要紧的一条：复合元素那一格**永远不能被小怪占住**
                // （旧实现只看 At() == null，会把这一格当空格走进去，然后 Put 直接抛异常）
                Assert.IsNull(engine.Board.At(new Pos(1, 0)), "复合元素那一格上不该出现元素小怪");
                Assert.IsNotNull(engine.Board.OccupantAt(new Pos(1, 0)), "占着那一格的始终是复合元素");

                engine.EndPlayerPhase();
                engine.EndDiscardPhase();
                if (!engine.ResolveJudgment()) break;
            }
        }

        [Test]
        public void 复合元素_挡住的移动会记一条Bump_且不让小怪穿过去()
        {
            var rules = BaseRules(5);
            rules.MaxMoveSteps = 3;

            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceComposite(CompositeKind.Ore, new Pos(2, 1));
            engine.PlaceCreep(Element.Fire, new Pos(1, 1)); // 右边紧邻就是复合元素
            engine.BeginRound();

            // 回合开始时复合元素会先产出 土+4、金+4（这是设计要求的），
            // 所以前两步是「复合元素出现 / 复合元素产生」，之后才轮到小怪移动。
            var production = engine.AdvancePreparation();
            Assert.IsNotNull(production);
            Assert.IsTrue(production.IsCompositeEffect, "回合开始应该先让复合元素产生一次");
            Assert.Greater(engine.CreepCount, 1, "复合元素产生了元素");

            // 找到那只火（产出可能把它相生成了别的元素，但位置一定还在 (1,1)）
            var blocker = engine.Board.At(new Pos(1, 1));
            Assert.IsNotNull(blocker, "(1,1) 上应该还有东西");
            var blockerUid = blocker.Uid;

            engine.SetNextRandomValues(3, 1); // 走 3 格，方向 +X
            var step = engine.AdvancePreparation();
            Assert.IsNotNull(step);
            Assert.AreEqual(blockerUid, step.Uid, "这一步应该是 (1,1) 上那只小怪在走");

            Assert.IsTrue(step.Events.Any(e => e.Type == GameEventType.Bump),
                "被复合元素挡住的移动要记一条 Bump（表现层才知道要播「撞了一下」）");
            Assert.AreEqual(new Pos(1, 1), engine.Board.At(new Pos(1, 1))?.Pos, "它必须停在原地");
            Assert.AreEqual(1, engine.CompositeCount, "复合元素不会因为被撞而消失");
            Assert.IsFalse(step.Events.Any(e => e.Type == GameEventType.Move), "一步都不该走成");
        }

        [Test]
        public void 复合元素_元素牌打不到它()
        {
            var rules = BaseRules();
            var engine = RigAtPlayerPhase(rules, new[] { Elemental(Element.Fire) }, (Element.Water, new Pos(3, 3)));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(5, 5));

            var card = engine.Hand[0];
            Assert.IsTrue(card.IsElementCard);

            var record = engine.TryPlayCard(card.Id, new Pos(5, 5));
            Assert.IsNull(record, "元素牌不能打在复合元素上");
            StringAssert.Contains("复合元素", engine.FailReason);
            Assert.AreEqual(1, engine.Hand.Count, "失败不消耗手牌");
            Assert.AreEqual(1, engine.CompositeCount, "复合元素还在");
        }

        // ------------------------------------------------------------ 复合元素的「产生」效果

        [Test]
        public void 产生_范围内按组成元素各生成N份()
        {
            // 复合元素放在角落：范围内只剩 3 格，所以 3+3=6 份里有 3 份放得下。
            // 这样「产出受范围限制、不是全盘乱撒」这件事是确定的。
            var rules = BaseRules(5);
            rules.CompositeIngredientsPerRound = 3;
            rules.CompositeRange = 1;

            var engine = Rig(rules);
            engine.PlaceComposite(CompositeKind.Ember, new Pos(0, 0)); // 木 + 火

            var record = engine.AdvanceCompositeProduction();
            Assert.IsNotNull(record, "回合开始应该有一个「产生」步骤");
            Assert.IsTrue(record.IsCompositeEffect);

            int wood = engine.Board.CountOf(Element.Wood);
            int fire = engine.Board.CountOf(Element.Fire);
            Assert.AreEqual(3, engine.CompositeProducedSpawns, "角落只有 3 格，最多落地 3 份");
            Assert.AreEqual(6, engine.CompositeProducedCount, "但一共**尝试**产出 木3 + 火3 = 6 份");
            Assert.AreEqual(3, record.Events.Count(e => e.Type == GameEventType.Spawn));
            Assert.AreEqual(3, wood + fire, "落地的元素总数对得上");
            Assert.AreEqual(3, engine.SpawnBlockedCount, "另外 3 份放不下 → 各记一次出生失败");

            // 产出的元素必须落在范围内（切比雪夫距离 ≤ 1），而且不能压在复合元素自己那格
            foreach (var creep in engine.Creeps)
                Assert.LessOrEqual(Chebyshev(creep.Pos, new Pos(0, 0)), 1, $"{creep.Pos} 超出了范围");

            Assert.IsNotNull(engine.CompositeAt(new Pos(0, 0)), "复合元素自己那格不能被产出盖掉");

            // 同一次「产生」只跑一次：再推一次应该返回 null
            Assert.IsFalse(engine.HasPendingCompositeProduction());
            Assert.IsNull(engine.AdvanceCompositeProduction());
        }

        [Test]
        public void 产生_默认每回合每种组成元素各4份()
        {
            // 图2 表格那一列写的是「木+4、火+4」。这个数字**不能靠别的用例间接覆盖** ——
            // 之前所有用例都自己设了 1/2/3，默认值本身一直没被钉住（我就在探针里手写成 3 而出过一次错）。
            Assert.AreEqual(4, new Rules().CompositeIngredientsPerRound, "默认必须是 4（图2 的原文）");
            Assert.AreEqual(8, new Rules().CompositeProducedPerRound, "两种组成元素各 4 份 = 每回合 8 份");

            var rules = BaseRules(9); // 9×9，保证 8 份都放得下
            Assert.AreEqual(4, rules.CompositeIngredientsPerRound, "没设过 → 走默认值");

            var engine = Rig(rules);
            engine.PlaceComposite(CompositeKind.ColdSteel, new Pos(4, 4));

            var record = engine.AdvanceCompositeProduction();

            // 范围内正好 8 格、要产 8 份，同一批产出不会互相踩格 → 8 份全部落地。
            var spawns = record.Events.Where(e => e.Type == GameEventType.Spawn).ToList();
            Assert.AreEqual(8, spawns.Count, "每回合 4 + 4 = 8 条 Spawn 事件");
            Assert.AreEqual(4, spawns.Count(e => e.Element == Element.Metal), "金产出 4 份");
            Assert.AreEqual(4, spawns.Count(e => e.Element == Element.Water), "水产出 4 份");
            Assert.AreEqual(8, engine.CompositeProducedCount, "8 份产出动作");
            Assert.AreEqual(0, engine.SpawnBlockedCount, "8 格放 8 份，一份都不该被挡");
            Assert.AreEqual(4, engine.Board.CountOf(Element.Metal), "金 4 只都在场上");
            Assert.AreEqual(4, engine.Board.CountOf(Element.Water), "水 4 只都在场上");
        }

        [Test]
        public void 产生_外圈没有空格时也不会把元素落到范围外()
        {
            var rules = BaseRules(5);
            rules.CompositeIngredientsPerRound = 4;
            rules.CompositeRange = 1;

            var engine = Rig(rules);
            engine.PlaceComposite(CompositeKind.Ember, new Pos(0, 0));

            engine.AdvanceCompositeProduction();

            foreach (var creep in engine.Creeps)
                Assert.LessOrEqual(Chebyshev(creep.Pos, new Pos(0, 0)), 1,
                    $"{creep.Pos} 跑到范围外了 —— 图2 写的是「进入范围内生产」");

            Assert.AreEqual(3, engine.Board.CreepCount(), "角落里只放得下 3 个");
        }

        [Test]
        public void 产生_落在已有元素上时按图2的表反应一次()
        {
            // 烬（木 + 火）范围内放一只金：火产出来时会撞上金，
            // 火克金 → 金消失、火占住那一格，并记一次「产出相遇」。
            // 这条钉的是图2 那句「可与范围内已有的元素产生反应」。
            var rules = BaseRules(5);
            rules.CompositeIngredientsPerRound = 3;

            var engine = Rig(rules,
                (Element.Metal, new Pos(2, 3)),                                   // 范围内，等着被火克
                (Element.Water, new Pos(0, 0)), (Element.Water, new Pos(4, 4)),   // 离得远，不参与
                (Element.Earth, new Pos(0, 4)), (Element.Earth, new Pos(4, 0)));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(2, 2)); // 木 + 火

            var before = engine.RemovedOf(Element.Metal);
            engine.AdvanceCompositeProduction();

            Assert.Greater(engine.CompositeProducedEncounters, 0,
                "产出撞到范围内已有元素时必须发生一次图2 的反应");
            Assert.Greater(engine.RemovedOf(Element.Metal), before, "火克金 → 那只金应该被相克掉");
            Assert.AreNotEqual(Element.Metal, engine.Board.At(new Pos(2, 3))?.Element,
                "(2,3) 上的金已经被克掉了");
        }

        [Test]
        public void 产生_融合当回合立刻生效一次_之后每回合开始再生效()
        {
            var rules = BaseRules(8);
            rules.CompositeIngredientsPerRound = 1;
            rules.MaxMoveSteps = 0; // 准备阶段的小怪不乱跑，方便数数
            UseNeverWinningGoal(rules); // 寒钢产出的金+水会自相生，别让它顺手把「只剩水」判赢了

            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(1, 1)), (Element.Water, new Pos(2, 1)));

            var fusion = FusionCardInHand(engine);
            Assert.IsNotNull(fusion, "手里应该有一张融合牌");

            // —— 打出融合牌：复合元素出现，并且**当回合立刻产生一次** ——
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(1, 1), new Pos(2, 1));
            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(1, engine.CompositeCount);
            Assert.AreEqual(CompositeKind.ColdSteel, engine.CompositeAt(new Pos(1, 1)).Kind, "金+水 = 寒钢");

            Assert.AreEqual(2, engine.CompositeProducedCount, "融合当回合立刻生效一次（金 1 份 + 水 1 份 = 2 次产出）");
            Assert.AreEqual(2, engine.CompositeProducedSpawns, "两份都落在空格上，各自变成一只新元素");
            Assert.AreEqual(1, engine.CardsPlayedThisRound, "融合牌算一次出牌");

            // —— 结束这一回合，进下一回合的准备阶段：应该**再生效一次** ——
            engine.EndPlayerPhase();
            engine.EndDiscardPhase();
            // 这个目标（水元素数量 ≤ -1）永远不会成立，所以回合必须继续推进。
            bool continued = engine.ResolveJudgment();
            Assert.IsTrue(continued, "这个目标永远不会达成，回合必须继续推进");
            Assert.AreEqual(GamePhase.Preparation, engine.Phase, "应该进入下一回合的准备阶段");
            Assert.AreEqual(2, engine.Round);
            engine.AdvancePreparation();
            Assert.AreEqual(4, engine.CompositeProducedCount, "新回合开始时复合元素再生效一次（再 2 次产出）");
        }

        [Test]
        public void 产生_范围内没有空格时记一次出生失败而不是崩掉()
        {
            // 外圈 8 格全被占满 → 一份都产不出来，但绝不越界
            var rules = BaseRules(3);
            rules.CompositeIngredientsPerRound = 2;

            var engine = Rig(rules,
                (Element.Wood, new Pos(0, 0)), (Element.Wood, new Pos(1, 0)), (Element.Wood, new Pos(2, 0)),
                (Element.Fire, new Pos(0, 1)), (Element.Fire, new Pos(2, 1)),
                (Element.Wood, new Pos(0, 2)), (Element.Wood, new Pos(1, 2)), (Element.Fire, new Pos(2, 2)));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(1, 1));

            Assert.AreEqual(9, engine.Board.CreepCount() + engine.CompositeCount, "3x3 应该摆满了");

            var before = engine.SpawnBlockedCount;
            var record = engine.AdvanceCompositeProduction();

            Assert.IsNotNull(record);
            Assert.Greater(engine.SpawnBlockedCount, before, "范围内没地方放 → 记一次出生失败");
            Assert.AreEqual(9, engine.Board.CreepCount() + engine.CompositeCount, "总数不会因为失败而变化");
        }

        [Test]
        public void 产生_组成元素不在元素池里时不会偷偷生成池外元素()
        {
            var rules = BaseRules(5);
            rules.CompositeIngredientsPerRound = 2;
            rules.ElementPool = ElementMask.Wood | ElementMask.Fire; // 只允许木火
            var engine = Rig(rules);
            engine.PlaceComposite(CompositeKind.Ember, new Pos(2, 2)); // 木 + 火，两种都在池里

            engine.AdvanceCompositeProduction();

            foreach (var creep in engine.Creeps)
                Assert.IsTrue(rules.ElementPool.Has(creep.Element), $"{creep} 是池外元素");

            // 换成「灵泉（水+木）」，水不在池里 → 只有木生成得出来
            var engine2 = Rig(rules);
            engine2.PlaceComposite(CompositeKind.SpiritSpring, new Pos(2, 2));
            engine2.AdvanceCompositeProduction();

            Assert.AreEqual(2, engine2.Board.CountOf(Element.Wood), "木在池里，照常生成");
            Assert.AreEqual(0, engine2.Board.CountOf(Element.Water), "水不在池里，一份都不该生成");
            Assert.Greater(engine2.SpawnBlockedCount, 0, "被挡掉的要有记录");
            foreach (var creep in engine2.Creeps)
                Assert.IsTrue(rules.ElementPool.Has(creep.Element));
        }

        // ------------------------------------------------------------ 融合牌

        [Test]
        public void 融合牌_正交相邻两元素合成复合元素_落在元素1的位置_素材被消耗()
        {
            var rules = BaseRules(8);
            rules.CompositeIngredientsPerRound = 1;
            rules.MaxMoveSteps = 0;

            // 金(2,2) 与 水(2,3) 是**上下紧挨着**（十字相邻）
            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal), Elemental(Element.Metal) },
                (Element.Metal, new Pos(2, 2)), (Element.Water, new Pos(2, 3)));

            var fusion = FusionCardInHand(engine);
            Assert.IsNotNull(fusion);

            int handBefore = engine.Hand.Count;
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(2, 2), new Pos(2, 3));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(handBefore - 1, engine.Hand.Count, "融合牌被打出去了");
            Assert.AreEqual(1, engine.TotalFusions);
            Assert.AreEqual(1, engine.CompositeCount);

            // 两个素材本身被消耗掉了（元素1、元素2 那两格都空了）；
            // 复合元素当回合立刻生效一次，各自产出 1 份金、1 份水，
            // 而且同一批产出**不会互相踩格**，所以两格上各留 1 只。
            var composite = engine.CompositeAt(new Pos(2, 2));
            Assert.IsNotNull(composite, "复合元素必须落在**元素1**的位置");
            Assert.AreEqual(CompositeKind.ColdSteel, composite.Kind);
            Assert.IsNull(engine.Board.At(new Pos(2, 3)), "元素2 那一格被腾空了（素材被消耗）");

            Assert.AreEqual(2, engine.Board.CreepCount(), "素材没了，只剩「产生」效果的结果：金 1 + 水 1");
            Assert.AreEqual(1, engine.Board.CountOf(Element.Metal), "产出的金没有被产出的水吃掉");
            Assert.AreEqual(1, engine.Board.CountOf(Element.Water));
            Assert.AreEqual(0, engine.CompositeProducedEncounters,
                "同一批产出之间不该自己和自己反应（各自占空格）");

            // 素材是「被消耗」，不是被相克湮灭 → 不该喂小瓶子
            Assert.AreEqual(0, engine.Bottle.Count(Element.Metal), "融合不该给金瓶子 +1");
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water), "融合不该给水瓶子 +1");
        }

        [Test]
        public void 融合牌_不相邻的两个元素合不了()
        {
            var rules = BaseRules(8);
            rules.MaxMoveSteps = 0;
            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Water) },
                (Element.Metal, new Pos(0, 0)), (Element.Water, new Pos(4, 0)));

            var fusion = FusionCardInHand(engine);
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(0, 0), new Pos(4, 0));

            Assert.IsNull(record);
            StringAssert.Contains("相邻", engine.FailReason);
            Assert.AreEqual(2, engine.Hand.Count, "失败不消耗手牌");
            Assert.AreEqual(0, engine.CompositeCount);
            Assert.IsNotNull(engine.Board.At(new Pos(0, 0)), "素材还在原地");

            // 而且第一段点击时就不该把 (4,0) 高亮出来
            Assert.IsEmpty(engine.CellsForFusionSecond(new Pos(0, 0)), "不相邻的元素不该出现在「元素2」候选里");
        }

        [Test]
        public void 融合牌_斜角不算相邻_只有十字四格算()
        {
            // 图2 的示意图给的是**十字范围**：只有上下左右紧挨着才能融合，
            // 斜角摆着的两只元素合不了（这条钉的就是「九宫格 vs 十字」这个区别）。
            var rules = BaseRules(8);
            rules.MaxMoveSteps = 0;

            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal), Elemental(Element.Metal), Elemental(Element.Metal) },
                (Element.Metal, new Pos(3, 3)),
                (Element.Water, new Pos(4, 4)),   // 斜角：不算相邻
                (Element.Water, new Pos(4, 2)),   // 另一个斜角
                (Element.Water, new Pos(5, 3)));  // 距离 2：也不行

            Assert.IsEmpty(engine.CellsForFusionSecond(new Pos(3, 3)),
                "四个斜角 + 距离 2 都不该出现在「元素2」候选里");
            Assert.IsFalse(engine.CanFusePair(engine.Board.At(new Pos(3, 3)), engine.Board.At(new Pos(4, 4))),
                "斜角的两只不算相邻");

            var fusion = FusionCardInHand(engine);
            Assert.IsNull(engine.TryPlayFusionCard(fusion.Id, new Pos(3, 3), new Pos(4, 4)));
            StringAssert.Contains("十字相邻", engine.FailReason);
        }

        [Test]
        public void 融合牌_十字四格都能当元素2_斜角不行()
        {
            var rules = BaseRules(9);
            rules.MaxMoveSteps = 0;

            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Water) },
                (Element.Metal, new Pos(4, 4)),
                (Element.Water, new Pos(4, 5)),   // 上
                (Element.Water, new Pos(5, 4)),   // 右
                (Element.Water, new Pos(4, 3)),   // 下
                (Element.Water, new Pos(3, 4)),   // 左
                (Element.Water, new Pos(3, 3)),   // 斜角：不算
                (Element.Water, new Pos(5, 5)));  // 斜角：不算

            var second = engine.CellsForFusionSecond(new Pos(4, 4));
            CollectionAssert.AreEquivalent(
                new[] { new Pos(4, 5), new Pos(5, 4), new Pos(4, 3), new Pos(3, 4) }, second,
                "元素2 只能是十字四格，两个斜角必须被排除");

            Assert.IsTrue(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(4, 5)));
            Assert.IsTrue(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(3, 4)));
            Assert.IsFalse(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(3, 3)));
            Assert.IsFalse(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(5, 5)));
            Assert.IsFalse(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(4, 6)));
            Assert.IsFalse(GameEngine.IsOrthogonallyAdjacent(new Pos(4, 4), new Pos(4, 4)));
        }

        [Test]
        public void 融合牌_关掉相邻要求就变成全盘任意两只都能合()
        {
            var rules = BaseRules(8);
            rules.MaxMoveSteps = 0;
            rules.FusionRequiresAdjacency = false;

            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(0, 0)), (Element.Water, new Pos(5, 5)));

            var second = engine.CellsForFusionSecond(new Pos(0, 0));
            CollectionAssert.Contains(second, new Pos(5, 5), "关掉开关之后远处的元素也该能当元素2");

            var fusion = FusionCardInHand(engine);
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(0, 0), new Pos(5, 5));
            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(CompositeKind.ColdSteel, engine.CompositeAt(new Pos(0, 0)).Kind);
        }

        [Test]
        public void 融合牌_配不上的两种元素合不了_并给出配方()
        {
            var rules = BaseRules(8);
            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(0, 0)), (Element.Wood, new Pos(1, 0))); // 金+木不是配方

            var fusion = FusionCardInHand(engine);
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(0, 0), new Pos(1, 0));

            Assert.IsNull(record);
            StringAssert.Contains("配不成", engine.FailReason);
            StringAssert.Contains("寒钢", engine.FailReason); // 提示里带配方表
            Assert.IsEmpty(engine.CellsForFusionSecond(new Pos(0, 0)), "金旁边没有能配对的元素");
        }

        [Test]
        public void 融合牌_场上已经有复合元素时用不出去()
        {
            var rules = BaseRules(8);
            rules.MaxMoveSteps = 0;
            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(0, 0)), (Element.Water, new Pos(1, 0)));

            engine.PlaceComposite(CompositeKind.Ore, new Pos(6, 6));
            Assert.IsFalse(engine.CanFuseNow, "场上已经有复合元素");
            Assert.IsEmpty(engine.CellsForFusionFirst(), "所有格子都不该被高亮");
            Assert.IsEmpty(engine.CellsForFusionSecond(new Pos(0, 0)));

            var fusion = FusionCardInHand(engine);
            var record = engine.TryPlayFusionCard(fusion.Id, new Pos(0, 0), new Pos(1, 0));

            Assert.IsNull(record);
            StringAssert.Contains("已经有", engine.FailReason);
            StringAssert.Contains("矿晶", engine.FailReason);
            Assert.AreEqual(2, engine.Hand.Count, "失败不消耗手牌");
            Assert.IsNull(engine.CompositeAt(new Pos(0, 0)));
        }

        [Test]
        public void 融合牌_开局没有复合元素时是拿到复合元素的唯一路径()
        {
            // 随机铺盘 + 没有初始复合元素 → 场上一个复合元素都没有，
            // 只能靠融合牌现场合一个出来。这条钉的就是用户那句话。
            var rules = new Rules
            {
                BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 4,
                InitialHandSize = 5, MaxMoveSteps = 0,
                UseCompositeElements = true,
                SpecialCardPool = SpecialCardMask.Fusion | SpecialCardMask.Decompose,
            };
            var engine = new GameEngine(rules, 20240607);
            engine.Start();

            Assert.AreEqual(0, engine.CompositeCount, "开局不该凭空冒出复合元素");
            Assert.IsTrue(engine.CanFuseNow);

            int guard = 0;
            int fused = 0;
            while (engine.CompositeCount == 0 && guard++ < 400)
            {
                if (engine.Phase != GamePhase.PlayerAction)
                {
                    engine.EndPreparationNow();
                    if (engine.Phase != GamePhase.PlayerAction) break;
                }

                var fusion = FusionCardInHand(engine);
                if (fusion == null)
                {
                    engine.AddSpecialCardForTest(CardKind.FusionCard); // 没抽到就补给一张，专测融合路径
                    continue;
                }

                var first = engine.CellsForFusionFirst();
                if (first.Count == 0) break; // 这一局真的没得合

                var seconds = engine.CellsForFusionSecond(first[0]);
                if (engine.TryPlayFusionCard(fusion.Id, first[0], seconds[0]) != null) fused++;
            }

            Assert.Greater(fused, 0, "融合牌必须能真的合出复合元素来");
            Assert.AreEqual(1, engine.CompositeCount);
            Assert.AreEqual(1, engine.TotalFusions);
        }

        [Test]
        public void 融合牌_第一段能被高亮的格子必须真的存在有效配对()
        {
            var rules = BaseRules(6);
            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(0, 0)), (Element.Water, new Pos(1, 0)),
                (Element.Metal, new Pos(5, 5))); // 这只金周围没有能配对的

            var first = engine.CellsForFusionFirst();
            // 金(0,0) 和水(1,0) 彼此配对，所以两个都可以当「元素1」（点完再点另一个）；
            // 金(5,5) 周围一只可配对的都没有 → 不该出现在候选里。
            CollectionAssert.AreEquivalent(new[] { new Pos(0, 0), new Pos(1, 0) }, first,
                "只有真正存在有效配对的那两只该被高亮；金(5,5) 不该出现");

            var second = engine.CellsForFusionSecond(new Pos(0, 0));
            CollectionAssert.AreEqual(new[] { new Pos(1, 0) }, second, "元素1 点金(0,0) 时，元素2 只能是水(1,0)");
            CollectionAssert.AreEqual(new[] { new Pos(0, 0) }, engine.CellsForFusionSecond(new Pos(1, 0)));
            Assert.IsEmpty(engine.CellsForFusionSecond(new Pos(5, 5)), "孤立的那只金没有元素2 可选");
        }

        // ------------------------------------------------------------ 相邻复合元素

        [Test]
        public void 相邻复合元素_产生效果不会互相踩格也不会崩()
        {
            // ⚠ 这条钉的是一个真实的崩溃：两个复合元素相邻时，「同元素撞车 → 换一格」那条路
            //    原来用的是 Board.FindSpawnCell，而它不认识复合元素 —— 会把新元素直接放到
            //    隔壁复合元素那一格上，然后在 Board.Put 里抛 ArgumentException，整局游戏崩掉。
            var rules = BaseRules(6);
            rules.CompositeIngredientsPerRound = 3;

            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceComposite(CompositeKind.Ember, new Pos(2, 2)); // 木 + 火
            engine.PlaceComposite(CompositeKind.Lava, new Pos(3, 2));  // 火 + 土
            engine.BeginRound();

            // 两个复合元素各自产生一次，一共推两步（顺序按 uid）
            var first = engine.AdvanceCompositeProduction();
            Assert.IsNotNull(first);
            var second = engine.AdvanceCompositeProduction();
            Assert.IsNotNull(second, "第二个复合元素也该有自己的产生步骤");

            Assert.AreEqual(2, engine.CompositeCount, "两个复合元素都还在");
            Assert.AreEqual(CompositeKind.Ember, engine.CompositeAt(new Pos(2, 2)).Kind);
            Assert.AreEqual(CompositeKind.Lava, engine.CompositeAt(new Pos(3, 2)).Kind);

            // 最要紧的一条：复合元素那一格上**永远不能有元素小怪**
            foreach (var composite in engine.Composites)
                Assert.IsNull(engine.Board.At(composite.Pos),
                    $"{composite.Pos} 上的复合元素被元素小怪压住了（Put 会抛异常）");

            // 全场扫一遍，一格最多一个占格者
            var seen = new HashSet<Pos>();
            foreach (var creep in engine.Creeps)
                Assert.IsTrue(seen.Add(creep.Pos), $"{creep.Pos} 上出现了两只小怪");
            foreach (var composite in engine.Composites)
                Assert.IsTrue(seen.Add(composite.Pos), $"{composite.Pos} 上出现了两个占格者");

            // 两个复合元素都可能产出（扣掉互相挡住的格子）
            Assert.Greater(engine.CompositeProducedCount, 0, "相邻也要照常产出");
        }

        [Test]
        public void 相邻复合元素_产生效果只和范围内的元素反应_不和隔壁复合元素反应()
        {
            var rules = BaseRules(6);
            rules.CompositeIngredientsPerRound = 1; // 每种只产 1 份，结果确定

            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceComposite(CompositeKind.Ember, new Pos(2, 2)); // 先产木、再产火
            engine.PlaceComposite(CompositeKind.Lava, new Pos(3, 2));
            engine.BeginRound();

            engine.AdvanceCompositeProduction(); // 烬：木 1 份 + 火 1 份

            // 同一批产出各自占空格（木在离中心最近的那格、火在下一格），**不会互相反应**。
            Assert.AreEqual(1, engine.Board.CountOf(Element.Wood), "木 1 只");
            Assert.AreEqual(1, engine.Board.CountOf(Element.Fire), "火 1 只");
            Assert.AreEqual(2, engine.CreepCount);
            Assert.AreEqual(0, engine.CompositeProducedEncounters, "同一批产出之间不该自己撞自己");
            Assert.IsNull(engine.Board.At(new Pos(2, 2)), "烬自己那格不该被它产出的元素占住");
            Assert.IsNull(engine.Board.At(new Pos(3, 2)), "隔壁熔岩那格也不该被占住");
            Assert.AreEqual(2, engine.CompositeCount, "复合元素不会因为被撞而消失");
        }

        [Test]
        public void 相邻复合元素_后者会撞上前者刚产出的元素并反应()
        {
            // 「两个复合元素相邻时生成元素会不会直接反应」这个问题分两半：
            //   · 产出的元素**不会**和复合元素本身反应（复合元素是惰性的）
            //   · 但它会和**另一个复合元素刚产出的元素**反应 —— 那条路照旧走图2 的表
            var rules = BaseRules(6);
            rules.CompositeIngredientsPerRound = 3;

            var engine = new GameEngine(rules, 7);
            engine.ClearBoard();
            engine.PlaceComposite(CompositeKind.Ember, new Pos(2, 2));
            engine.PlaceComposite(CompositeKind.Lava, new Pos(3, 2));
            engine.BeginRound();

            engine.AdvanceCompositeProduction();                    // 烬先产 木3 火3
            var encBefore = engine.CompositeProducedEncounters;
            engine.AdvanceCompositeProduction();                    // 熔岩再产 火3 土3，会撞上烬产出的东西

            Assert.Greater(engine.CompositeProducedEncounters, encBefore,
                "熔岩产出的元素撞上烬产出的元素时，必须按图2 的表反应一次");
            Assert.AreEqual(2, engine.CompositeCount);

            foreach (var composite in engine.Composites)
                Assert.IsNull(engine.Board.At(composite.Pos), "复合元素那格始终不能被元素占住");
        }

        // ------------------------------------------------------------ 分解牌

        [Test]
        public void 分解牌_拆掉复合元素并给回两张对应元素的元素牌()
        {
            var rules = BaseRules(8);
            // 手牌里放一张分解牌（构造器随机发牌，这张是明确加上去的）
            var engine = Rig(rules, (Element.Metal, new Pos(3, 3)));
            engine.DrawSpecialCard(CardKind.DecomposeCard);
            engine.DrawElementCard(Element.Fire);
            engine.PlaceComposite(CompositeKind.SpiritSpring, new Pos(5, 5)); // 水 + 木

            // 直达玩家行动阶段（不经过抽牌阶段，手牌就保持上面这三张）
            engine.FreezeHandLimitForTest();
            engine.EndPreparationNow();
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);

            var decompose = DecomposeCardInHand(engine);
            Assert.IsNotNull(decompose);

            int handBefore = engine.Hand.Count;
            var record = engine.TryPlayDecomposeCard(decompose.Id, new Pos(5, 5));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(0, engine.CompositeCount, "复合元素被拆掉了");
            Assert.IsNull(engine.CompositeAt(new Pos(5, 5)));

            // 手牌：−1（分解牌）+2（水牌、木牌）
            Assert.AreEqual(handBefore + 1, engine.Hand.Count);
            Assert.AreEqual(1, engine.Hand.Count(c => c.IsElementCard && c.Element == Element.Water));
            Assert.AreEqual(1, engine.Hand.Count(c => c.IsElementCard && c.Element == Element.Wood));
            Assert.AreEqual(0, engine.TotalFusions);
            Assert.AreEqual(1, engine.TotalDecompositions);
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water), "拆复合元素不喂小瓶子");

            // 拆完之后又能融合了
            Assert.IsTrue(engine.CanFuseNow);
        }

        [Test]
        public void 分解牌_只能作用于复合元素()
        {
            var rules = BaseRules(8);
            var engine = RigAtPlayerPhase(rules,
                new[] { Decompose(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(1, 1)));

            var decompose = DecomposeCardInHand(engine);
            int handBefore = engine.Hand.Count;

            Assert.IsNull(engine.TryPlayDecomposeCard(decompose.Id, new Pos(1, 1)), "那格上是金元素，不是复合元素");
            StringAssert.Contains("没有复合元素", engine.FailReason);
            Assert.IsNull(engine.TryPlayDecomposeCard(decompose.Id, new Pos(6, 6)), "空格也不行");
            Assert.AreEqual(handBefore, engine.Hand.Count, "失败不消耗手牌");
            Assert.AreEqual(1, engine.Hand.Count(c => c.IsDecomposeCard));

            Assert.IsEmpty(engine.CellsForDecompose(), "场上没有复合元素 → 没有可点的格子");
        }

        [Test]
        public void 分解牌_可以关掉给牌_变成纯解场()
        {
            var rules = BaseRules(8);
            rules.DecomposeGrantsElementCards = false;

            var engine = RigAtPlayerPhase(rules,
                new[] { Decompose(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(1, 1)));
            engine.PlaceComposite(CompositeKind.Ore, new Pos(4, 4));

            var decompose = DecomposeCardInHand(engine);
            int handBefore = engine.Hand.Count;

            Assert.IsNotNull(engine.TryPlayDecomposeCard(decompose.Id, new Pos(4, 4)));
            Assert.AreEqual(handBefore - 1, engine.Hand.Count, "只少了那张分解牌，没有补元素牌");
            Assert.AreEqual(0, engine.CompositeCount);
            Assert.IsTrue(engine.CanFuseNow, "拆完又能融合");
        }

        // ------------------------------------------------------------ 秘术牌的种类与抽牌

        [Test]
        public void 手牌_秘术牌没有牌面元素_名字和字都对()
        {
            var fusion = new GameEngine(CompositeRulesWithoutCards(), 1);
            fusion.Start();
            var fusionCard = fusion.AddSpecialCardForTest(CardKind.FusionCard);
            var decomposeCard = fusion.AddSpecialCardForTest(CardKind.DecomposeCard);
            var elementCard = fusion.AddCardForTest(Element.Fire);

            Assert.IsTrue(fusionCard.IsFusionCard);
            Assert.IsFalse(fusionCard.IsElementCard);
            Assert.AreEqual("融合牌", fusionCard.Label);
            Assert.AreEqual("融合", fusionCard.Glyph);

            Assert.IsTrue(decomposeCard.IsDecomposeCard);
            Assert.AreEqual("分解牌", decomposeCard.Label);
            Assert.AreEqual("分解", decomposeCard.Glyph);

            Assert.IsTrue(elementCard.IsElementCard);
            Assert.AreEqual("火牌", elementCard.Label);
            Assert.AreEqual("火", elementCard.Glyph);

            // 三种牌的配色互不相同（表现层靠它区分）
            Assert.AreNotEqual(fusionCard.Tint, decomposeCard.Tint);
            Assert.AreNotEqual(fusionCard.Tint, elementCard.Tint);
        }

        [Test]
        public void 抽牌_秘术牌池关着时一张都抽不到()
        {
            var rules = CompositeRulesWithoutCards();
            rules.InitialHandSize = 5;
            rules.HandLimit = 5;

            var engine = new GameEngine(rules, 99);
            engine.Start();

            Assert.AreEqual(0, rules.SpecialCardKindCount);
            Assert.IsEmpty(rules.DrawableSpecialCards());

            for (int round = 0; round < 6 && !engine.IsOver; round++)
            {
                engine.EndPreparationNow();
                foreach (var card in engine.Hand)
                    Assert.IsTrue(card.IsElementCard, $"{card.Label} 不该出现在牌池关着的手牌里");

                engine.EndPlayerPhase();
                engine.EndDiscardPhase();
                if (!engine.ResolveJudgment()) break;
            }
        }

        [Test]
        public void 抽牌_融合牌权重越高抽到的越多()
        {
            int LowWeight() => CountFusionCardsInHand(2);
            int HighWeight() => CountFusionCardsInHand(30);

            Assert.Greater(HighWeight(), LowWeight(),
                "融合牌权重从 2 提到 30，同样种子下抽到的融合牌应该更多（比例可控）");
        }

        static int CountFusionCardsInHand(int fusionWeight)
        {
            var rules = new Rules
            {
                BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 0, InitialHandSize = 0,
                HandLimit = 5, MaxMoveSteps = 0, RoundLimit = 3,
                UseCompositeElements = true,
                SpecialCardPool = SpecialCardMask.Fusion,
                ElementCardWeight = 10,
                FusionCardWeight = fusionWeight,
            };

            var engine = new GameEngine(rules, 20240607);
            engine.Start();

            int fusionCards = 0;
            for (int round = 0; round < 3 && !engine.IsOver; round++)
            {
                engine.EndPreparationNow();
                fusionCards += engine.Hand.Count(c => c.IsFusionCard);
                engine.EndPlayerPhase();
                engine.EndDiscardPhase();
                if (!engine.ResolveJudgment()) break;
            }
            return fusionCards;
        }

        [Test]
        public void 抽牌_元素池凑不出配方时融合牌不进牌堆()
        {
            // 只有金一种元素 → 融合需要两种不同元素，融合牌是死牌，自动跳过
            var rules = new Rules
            {
                BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 2, InitialHandSize = 5,
                HandLimit = 5, ElementPool = ElementMask.Metal, CardPool = ElementMask.Metal,
                UseCompositeElements = true,
                SpecialCardPool = SpecialCardMask.All,
            };
            rules.Validate();

            Assert.IsTrue(rules.HasSpecialCard(CardKind.FusionCard), "配置上是开着的");
            Assert.IsEmpty(rules.DrawableSpecialCards(), "但抽牌时应该自动跳过（合不出来）");

            var engine = new GameEngine(rules, 5);
            engine.Start();
            for (int i = 0; i < 20; i++)
            {
                var card = engine.DrawRandomCardForTest();
                Assert.IsFalse(card.IsFusionCard, "凑不出配方时不该抽到融合牌");
            }
        }

        [Test]
        public void 抽牌_牌堆全空时不崩且退化成元素牌()
        {
            var rules = CompositeRulesWithoutCards();
            rules.CardPool = ElementMask.None;    // Validate 会把它修回「全部」
            rules.ElementCardWeight = 0;          // 元素牌权重 0
            rules.InitialHandSize = 3;

            var engine = new GameEngine(rules, 3);
            engine.Start();

            Assert.AreEqual(ElementMask.All, engine.Rules.CardPool, "空牌池被修成全部");
            Assert.AreEqual(3, engine.Hand.Count, "权重全 0 也要给得出牌，不能死循环");
            Assert.IsTrue(engine.Hand.All(c => c.IsElementCard));
        }

        // ------------------------------------------------------------ 总开关（UseCompositeElements）

        [Test]
        public void 总开关_默认关闭_抽牌与复合元素全都不出现()
        {
            // 这是「旧关卡资产」的形态：只填了棋盘和目标，别的都没动。
            var rules = new Rules { BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 2, InitialHandSize = 5 };
            Assert.IsFalse(rules.UseCompositeElements, "默认必须是关的（否则老玩法会被静默改掉）");

            rules.Validate();
            Assert.IsEmpty(rules.DrawableSpecialCards(), "关着时一张秘术牌都不该进牌堆");
            Assert.IsFalse(rules.CanEverGetComposite);
            Assert.IsFalse(rules.HasSpecialCard(CardKind.FusionCard), "总开关关着 → 连池子都不算开");
            Assert.AreEqual(0, rules.SpecialCardKindCount);

            var engine = new GameEngine(rules, 20240607);
            engine.Start();
            Assert.AreEqual(0, engine.CompositeCount, "开局不该有复合元素");

            for (int round = 0; round < 6 && !engine.IsOver; round++)
            {
                engine.EndPreparationNow();
                foreach (var card in engine.Hand)
                    Assert.IsTrue(card.IsElementCard, $"{card.Label} 不该在总开关关着时出现");

                engine.EndPlayerPhase();
                engine.EndDiscardPhase();
                if (!engine.ResolveJudgment()) break;
            }
        }

        [Test]
        public void 总开关_关着时初始复合元素一个都不摆_并记一条日志()
        {
            var rules = BaseRules(6, useComposites: false);
            rules.SpecialCardPool = SpecialCardMask.All;          // 故意配好，看它会不会偷偷生效
            rules.InitialHandSize = 3;
            rules.InitialComposites = new[] { new CompositePlacement(2, 2, CompositeKind.Lava) };

            var engine = new GameEngine(rules, 11);
            engine.Start();

            Assert.AreEqual(0, engine.CompositeCount, "总开关没勾 → 初始复合元素必须被忽略");
            Assert.IsTrue(engine.Log.Any(e => e.Type == GameEventType.Message && e.Text.Contains("总开关没勾")),
                "被忽略这件事要留下一条说明，不能静默");
        }

        [Test]
        public void 总开关_关着时融合牌与分解牌都用不出去_并说明原因()
        {
            var rules = BaseRules(8, useComposites: false);
            rules.MaxMoveSteps = 0;

            var engine = RigAtPlayerPhase(rules,
                new[] { Fusion(), Decompose(), Elemental(Element.Metal) },
                (Element.Metal, new Pos(1, 1)), (Element.Water, new Pos(2, 1)));
            engine.PlaceComposite(CompositeKind.Ore, new Pos(6, 6)); // 沙盒可以直接摆，用来验"牌仍然用不了"

            var fusion = FusionCardInHand(engine);
            var decompose = DecomposeCardInHand(engine);
            Assert.IsNotNull(fusion, "沙盒可以往手里塞牌");
            Assert.IsNotNull(decompose);

            Assert.IsNull(engine.TryPlayFusionCard(fusion.Id, new Pos(1, 1), new Pos(2, 1)));
            StringAssert.Contains("没有开启", engine.FailReason);
            Assert.AreEqual(3, engine.Hand.Count, "失败不消耗手牌");

            Assert.IsNull(engine.TryPlayDecomposeCard(decompose.Id, new Pos(6, 6)));
            StringAssert.Contains("没有开启", engine.FailReason);
            Assert.AreEqual(3, engine.Hand.Count);
        }

        [Test]
        public void 总开关_勾上之后就是能玩的默认配比()
        {
            // 「只勾总开关」不能是一张牌都发不出来的空配置。运行时不做自动补，
            // 所以这里钉的是：**面板/编辑器勾开关时会顺手配上**（见 RulesInspectorWindow 与
            // LevelDefinitionInspector 的「启用复合元素（用默认配比）」），
            // 以及这个默认配比本身是零问题的。
            var rules = new Rules { BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 4, InitialHandSize = 5 };
            rules.UseCompositeElements = true;                 // 勾开关
            rules.SpecialCardPool = SpecialCardMask.All;       // 面板会顺手做的一步
            rules.Validate();

            Assert.IsTrue(rules.HasSpecialCard(CardKind.FusionCard));
            Assert.IsTrue(rules.HasSpecialCard(CardKind.DecomposeCard));
            Assert.IsTrue(rules.CanEverGetComposite, "元素池是全部 → 配方凑得出来");
            Assert.IsFalse(rules.CompositeFeatureEnabledButInert);
            Assert.IsEmpty(Issues(rules), string.Join(" / ", Issues(rules)));

            var engine = new GameEngine(rules, 20240607);
            engine.Start();
            Assert.AreEqual(0, engine.CompositeCount, "开局没有初始复合元素");
            Assert.IsTrue(engine.CanFuseNow);
        }

        [Test]
        public void 总开关_默认值就是没碰过这层玩法的干净状态()
        {
            // 刚建的关卡：开关关着、池子也是空的 —— 一条问题都不该报，
            // 否则「新建关卡」出来就是一片红，报的还是「你配了秘术牌却忘了勾开关」。
            var rules = BaseRules(8, useComposites: false);
            Assert.AreEqual(SpecialCardMask.None, rules.SpecialCardPool, "池子默认必须是空的");
            Assert.IsEmpty(rules.InitialComposites);

            Assert.IsEmpty(Issues(rules), string.Join(" / ", Issues(rules)));
        }

        [Test]
        public void 总开关_勾上但秘术牌全关且没有初始复合元素_要提醒这个开关白勾了()
        {
            var rules = CompositeRulesWithoutCards();
            Assert.IsTrue(rules.CompositeFeatureEnabledButInert);

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("等于白勾")), string.Join(" / ", issues));

            // 摆一个初始复合元素就不再是空的了
            rules.InitialComposites = new[] { new CompositePlacement(3, 3, CompositeKind.Ember) };
            Assert.IsFalse(rules.CompositeFeatureEnabledButInert);
            Assert.IsFalse(Issues(rules).Any(i => i.Contains("等于白勾")));
        }

        [Test]
        public void 总开关_没勾却配了秘术牌或初始复合元素_要提醒忘勾了()
        {
            var rules = BaseRules(8, useComposites: false);
            rules.SpecialCardPool = SpecialCardMask.All;

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("总开关") && i.Contains("没勾")), string.Join(" / ", issues));

            // 只配初始复合元素也要提醒
            var rules2 = BaseRules(8, useComposites: false);
            rules2.SpecialCardPool = SpecialCardMask.None;
            rules2.InitialComposites = new[] { new CompositePlacement(1, 1, CompositeKind.Ore) };
            Assert.IsTrue(Issues(rules2).Any(i => i.Contains("总开关") && i.Contains("没勾")),
                string.Join(" / ", Issues(rules2)));

            // 什么都没配就不该报（不能给干净的老关卡刷屏）
            var clean = BaseRules(8, useComposites: false);
            clean.SpecialCardPool = SpecialCardMask.None;
            Assert.IsEmpty(Issues(clean), string.Join(" / ", Issues(clean)));
        }

        [Test]
        public void 秘术牌集合的人话_两个都勾上时不能漏说分解牌()
        {
            // Unity 的多选下拉框在「两个都勾上」时写进来的是 -1（所有位都置 1，含没定义的高位），
            // 所以显示名字必须按位判 —— 用整体比较会把它看成「只有融合」，报错文案就会漏说分解牌。
            Assert.AreEqual("（空）", SpecialCardMask.None.Names());
            Assert.AreEqual("融合", SpecialCardMask.Fusion.Names());
            Assert.AreEqual("分解", SpecialCardMask.Decompose.Names());
            Assert.AreEqual("融合+分解", SpecialCardMask.All.Names());
            Assert.AreEqual("融合+分解", ((SpecialCardMask)(-1)).Names(), "Inspector 全勾 = -1");
            Assert.AreEqual("融合+分解", (SpecialCardMask.All | (SpecialCardMask)1024).Names(), "多出来的高位不该影响名字");
        }

        [Test]
        public void 总开关_面板摘要一眼看得出开没开()
        {
            var off = new Rules();
            Assert.AreEqual("关", TapGJ.Levels.LevelDefinition.CompositeFeatureText(off));

            var on = new Rules { UseCompositeElements = true };
            var text = TapGJ.Levels.LevelDefinition.CompositeFeatureText(on);
            StringAssert.Contains("不发秘术牌", text, "只勾总开关、不选秘术牌时要说清楚一张都不发");

            on.SpecialCardPool = SpecialCardMask.All;
            text = TapGJ.Levels.LevelDefinition.CompositeFeatureText(on);
            StringAssert.StartsWith("开", text);
            StringAssert.Contains("融合牌", text);
            StringAssert.Contains("分解牌", text);
            StringAssert.Contains("权重 10/10/5", text);

            on.InitialComposites = new[]
            {
                new CompositePlacement(1, 1, CompositeKind.Ember),
                new CompositePlacement(2, 2, CompositeKind.Lava),
            };
            StringAssert.Contains("开局 2 个", TapGJ.Levels.LevelDefinition.CompositeFeatureText(on));
        }

        // ------------------------------------------------------------ 配置校验

        [Test]
        public void 校验_开了融合牌但元素池凑不出配方要报警()
        {
            var rules = CompositeRulesWithoutCards();
            rules.SpecialCardPool = SpecialCardMask.Fusion;
            rules.ElementPool = ElementMask.Metal;
            rules.CardPool = ElementMask.Metal;

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("融合") && i.Contains("永远用不出去")),
                string.Join(" / ", issues));
        }

        [Test]
        public void 校验_开了分解牌但拿不到复合元素要报警()
        {
            var rules = CompositeRulesWithoutCards();
            rules.SpecialCardPool = SpecialCardMask.Decompose; // 只有分解牌，没有融合牌
            rules.ElementPool = ElementMask.All;

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("分解") && i.Contains("永远用不出去")),
                string.Join(" / ", issues));
        }

        [Test]
        public void 校验_秘术牌权重为零等于关掉_要报警()
        {
            var rules = CompositeRulesWithoutCards();
            rules.SpecialCardPool = SpecialCardMask.Fusion;
            rules.FusionCardWeight = 0;

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("融合") && i.Contains("权重是 0")), string.Join(" / ", issues));
        }

        [Test]
        public void 校验_元素池齐全且开了两张秘术牌时没有问题()
        {
            var rules = CompositeRulesWithoutCards();
            rules.SpecialCardPool = SpecialCardMask.All;
            rules.ElementPool = ElementMask.All;
            rules.CardPool = ElementMask.All;

            Assert.IsEmpty(Issues(rules), string.Join(" / ", Issues(rules)));
        }

        [Test]
        public void 校验_初始复合元素越界要报警()
        {
            var rules = CompositeRulesWithoutCards();
            rules.BoardWidth = 4;
            rules.BoardHeight = 4;
            rules.InitialComposites = new[] { new CompositePlacement(9, 9, CompositeKind.Ember) };

            var issues = Issues(rules);
            Assert.IsTrue(issues.Any(i => i.Contains("初始复合元素") && i.Contains("越界")), string.Join(" / ", issues));
        }

        [Test]
        public void 校验_数值非法要被Validate挡住()
        {
            var rules = CompositeRulesWithoutCards();
            rules.CompositeRange = 0;
            Assert.Throws<System.ArgumentException>(() => rules.Validate());

            rules = CompositeRulesWithoutCards();
            rules.CompositeIngredientsPerRound = 0;
            Assert.Throws<System.ArgumentException>(() => rules.Validate());

            rules = CompositeRulesWithoutCards();
            rules.FusionCardWeight = -1;
            Assert.Throws<System.ArgumentException>(() => rules.Validate());
        }

        // ------------------------------------------------------------ 初始复合元素

        [Test]
        public void 初始复合元素_开局就摆在棋盘上_并且占住那格()
        {
            var rules = BaseRules(6);
            rules.SpecialCardPool = SpecialCardMask.All;
            rules.InitialHandSize = 3;
            rules.InitialComposites = new[] { new CompositePlacement(2, 2, CompositeKind.Lava) };

            var engine = new GameEngine(rules, 11);
            engine.Start();

            Assert.AreEqual(1, engine.CompositeCount);
            Assert.AreEqual(CompositeKind.Lava, engine.CompositeAt(new Pos(2, 2)).Kind);
            Assert.IsFalse(engine.Board.IsEmpty(new Pos(2, 2)), "初始复合元素要占住格子");
            Assert.IsFalse(engine.CanFuseNow, "开局就有复合元素 → 融合牌一开始用不出去");
        }

        [Test]
        public void 初始复合元素_坐标冲突时跳过并记一条日志_不抛异常()
        {
            var rules = BaseRules(6);
            rules.InitialHandSize = 0;
            rules.InitialComposites = new[]
            {
                new CompositePlacement(1, 1, CompositeKind.Ember),
                new CompositePlacement(1, 1, CompositeKind.Lava),  // 和上一条抢同一格
                new CompositePlacement(99, 99, CompositeKind.Ore), // 越界
            };

            var engine = new GameEngine(rules, 11);
            engine.Start();

            Assert.AreEqual(1, engine.CompositeCount, "只该成功摆上一个");
            Assert.AreEqual(CompositeKind.Ember, engine.CompositeAt(new Pos(1, 1)).Kind, "先来的那个赢");
            Assert.IsTrue(engine.Log.Any(e => e.Type == GameEventType.Message && e.Text.Contains("已跳过")),
                "冲突的条目要留下一条说明");
        }

        // ------------------------------------------------------------ 辅助

        static List<string> Issues(Rules rules)
        {
            var list = new List<string>();
            var copy = rules.Clone();
            try { copy.Validate(); }
            catch (System.Exception e) { list.Add("规则数值非法：" + e.Message); return list; }
            copy.CollectIssues(list);
            return list;
        }

        static int Chebyshev(Pos a, Pos b)
            => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));
    }
}

