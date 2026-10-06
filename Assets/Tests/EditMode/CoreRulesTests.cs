using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;

namespace TapGJ.Tests
{
    /// <summary>
    /// 规则层测试：全部直接 new GameEngine 跑，不需要进 Play 模式。
    /// 在 Unity 里打开 Window ▸ General ▸ Test Runner ▸ EditMode 点 Run All 即可。
    /// </summary>
    public class CoreRulesTests
    {
        // ------------------------------------------------------------ 工具

        /// <summary>造一个只含指定小怪的引擎（随机源由 SetNextRandomValues 控制）。</summary>
        static GameEngine Rig(Rules rules, params (Element element, Pos pos)[] pieces)
        {
            var engine = new GameEngine(rules, 1);
            engine.ClearBoard();
            foreach (var (element, pos) in pieces) engine.PlaceCreep(element, pos);
            engine.BeginRound();
            return engine;
        }

        static Rules BaseRules() => new Rules
        {
            BoardWidth = 8,
            BoardHeight = 8,
            CreepsPerElement = 0, // 由测试自己摆
            InitialHandSize = 0,
            RoundLimit = 15,
        };

        // ------------------------------------------------------------ 开局

        [Test]
        public void 开局_每种元素四个_且每格最多一个()
        {
            var engine = new GameEngine(new Rules(), 20240607);
            engine.Start();

            Assert.AreEqual(20, engine.CreepCount, "5 种元素 × 4 个 = 20 个");
            foreach (var e in ElementDefs.All)
                Assert.AreEqual(4, engine.Board.CountOf(e), $"{ElementDefs.Name(e)} 应该有 4 个");

            // 每格最多一个 + 坐标合法
            var seen = new System.Collections.Generic.HashSet<Pos>();
            foreach (var c in engine.Creeps)
            {
                Assert.IsTrue(engine.Board.InBounds(c.Pos), $"{c} 坐标越界");
                Assert.IsTrue(seen.Add(c.Pos), $"{c.Pos} 上出现了两个元素");
                Assert.AreSame(c, engine.Board.At(c.Pos));
            }

            Assert.AreEqual(5, engine.Hand.Count, "初始手牌 5 张");
            Assert.AreEqual(GamePhase.Preparation, engine.Phase);
            Assert.AreEqual(1, engine.Round);
        }

        [Test]
        public void 相同种子_完全复现()
        {
            var a = new GameEngine(new Rules(), 777);
            var b = new GameEngine(new Rules(), 777);
            a.Start();
            b.Start();

            for (int i = 0; i < 30 && a.Phase == GamePhase.Preparation; i++) a.AdvancePreparation();
            for (int i = 0; i < 30 && b.Phase == GamePhase.Preparation; i++) b.AdvancePreparation();

            Assert.AreEqual(a.StatusLine(), b.StatusLine(), "同种子的两次模拟结果必须一致");
        }

        // ------------------------------------------------------------ 图2 反应表

        [Test]
        public void 反应表_双向一致_且同元素不反应()
        {
            var table = new ReactionTable();

            foreach (var a in ElementDefs.All)
            {
                foreach (var b in ElementDefs.All)
                {
                    var def = table.Get(a, b);

                    if (a == b)
                    {
                        Assert.AreEqual(ReactionRule.NoReaction, def.Rule,
                            $"同元素({ElementDefs.Name(a)})相遇不应该发生反应");
                        continue;
                    }

                    // 图2 是按「行元素 遇 列元素」双向等价写的，所以 (a,b) 与 (b,a) 必须描述同一件事
                    var mirror = table.Get(b, a);
                    Assert.AreEqual(def.Rule, mirror.Rule,
                        $"{ElementDefs.Name(a)}×{ElementDefs.Name(b)} 与 {ElementDefs.Name(b)}×{ElementDefs.Name(a)} 规则不一致");
                    if (def.CreatesCreep)
                        Assert.AreEqual(def.Spawn, mirror.Spawn,
                            $"{ElementDefs.Name(a)}×{ElementDefs.Name(b)} 两个方向生成/消失的元素不一致");
                }
            }

            // 抽查图2 里写明的几条（括号里是原文）
            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Water, Element.Wood).Rule, "水：与木牌相遇木元素+1");
            Assert.AreEqual(Element.Wood, table.Get(Element.Water, Element.Wood).Spawn);

            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Fire, Element.Wood).Rule, "火：与木牌相遇火元素+1");
            Assert.AreEqual(Element.Fire, table.Get(Element.Fire, Element.Wood).Spawn);

            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Water, Element.Earth).Rule, "水：与土元素相遇，该水元素消失");
            Assert.AreEqual(Element.Earth, table.Get(Element.Water, Element.Earth).Spawn);

            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Metal, Element.Wood).Rule, "金：与木元素相遇，该木元素消失");
            Assert.AreEqual(Element.Metal, table.Get(Element.Metal, Element.Wood).Spawn);

            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Earth, Element.Metal).Rule, "土：与金牌相遇金元素+1 → 金赢、土输");
            Assert.AreEqual(Element.Metal, table.Get(Element.Earth, Element.Metal).Spawn);
            Assert.AreEqual(ReactionRule.SpawnOne, table.Get(Element.Fire, Element.Metal).Rule, "火：与金元素相遇，该金元素消失（点名金 → 火输、金赢）");
            Assert.AreEqual(Element.Metal, table.Get(Element.Fire, Element.Metal).Spawn);

            // 原表两种句式看起来打架，但按「被点名的元素是输家」读就全部自洽。
            // 下面 10 对逐对钉住赢家，防止以后改表改歪。
            var allPairs = new (Element a, Element b, Element winner, string why)[]
            {
                (Element.Metal, Element.Wood, Element.Metal, "金：与木元素相遇，该木元素消失"),
                (Element.Metal, Element.Water, Element.Water, "金：与水牌相遇水元素+1 → 金消失变成水"),
                (Element.Metal, Element.Earth, Element.Metal, "金：与土牌相遇金元素+1 → 土输"),
                (Element.Fire, Element.Metal, Element.Metal, "火：与金元素相遇，该金元素消失 → 火输"),
                (Element.Water, Element.Wood, Element.Wood, "水：与木牌相遇木元素+1 → 水输"),
                (Element.Wood, Element.Fire, Element.Fire, "木：与火牌相遇火元素+1 → 木输"),
                (Element.Wood, Element.Earth, Element.Wood, "木：与土元素相遇，该土元素消失"),
                (Element.Water, Element.Fire, Element.Water, "水：与火元素相遇，该火元素消失"),
                (Element.Water, Element.Earth, Element.Earth, "水：与土元素相遇，该水元素消失"),
                (Element.Fire, Element.Earth, Element.Earth, "火：与土牌相遇土元素+1 → 火输"),
            };

            Assert.AreEqual(10, allPairs.Length, "跨元素组合一共 10 对");
            foreach (var (a, b, winner, why) in allPairs)
            {
                var d = table.Get(a, b);
                Assert.AreEqual(ReactionRule.SpawnOne, d.Rule, $"{ElementDefs.Name(a)}×{ElementDefs.Name(b)} 必须有明确赢家");
                Assert.AreEqual(winner, d.Spawn,
                    $"{ElementDefs.Name(a)}×{ElementDefs.Name(b)} 的赢家应该是{ElementDefs.Name(winner)}（{why}）");

                // 反向必须给出同一个赢家
                Assert.AreEqual(d.Rule, table.Get(b, a).Rule, "反向规则必须一致");
                Assert.AreEqual(winner, table.Get(b, a).Spawn, "反向赢家必须一致");
            }

            // 再确认一遍：25 格里没有一格是「没有明确赢家」的跨元素组合
            foreach (var a in ElementDefs.All)
            {
                foreach (var b in ElementDefs.All)
                {
                    if (a == b) continue;
                    Assert.AreNotEqual(ReactionRule.BothVanish, table.Get(a, b).Rule,
                        $"{ElementDefs.Name(a)}×{ElementDefs.Name(b)} 不该是湮灭型规则 —— 10 对都应该有赢家");
                }
            }
        }

        // ------------------------------------------------------------ 移动与相遇

        [Test]
        public void 移动到空格_位置更新且格子唯一()
        {
            var engine = Rig(BaseRules(), (Element.Water, new Pos(0, 0)));
            engine.SetNextRandomValues(2, 1); // 走 2 格，方向 +X

            var step = engine.AdvancePreparation();

            Assert.AreEqual(new Pos(2, 0), step.To, "水应该从 (0,0) 走到 (2,0)");
            Assert.IsNull(engine.Board.At(new Pos(0, 0)), "起点必须清空");
            Assert.IsNotNull(engine.Board.At(new Pos(2, 0)), "终点必须有该元素");
            Assert.AreEqual(1, engine.CreepCount, "数量不变（没有反应）");
        }

        [Test]
        public void 同元素相遇_禁止移动_双方都留在原地()
        {
            var engine = Rig(BaseRules(),
                (Element.Water, new Pos(0, 0)),
                (Element.Water, new Pos(2, 0)));
            engine.SetNextRandomValues(3, 1); // 想走 3 格，方向 +X

            var step = engine.AdvancePreparation();

            // 走到 (1,0) 之后紧挨着同元素，于是「禁止此次移动」：
            // 已走的那 1 格保留，但不会继续前进、也不会撞进对方的格子。
            Assert.AreEqual(new Pos(1, 0), step.To, "同元素相遇后应该停在相邻空格，不能继续推进");
            Assert.IsNotNull(engine.Board.At(new Pos(1, 0)));
            Assert.IsNotNull(engine.Board.At(new Pos(2, 0)), "被撞上的同元素必须留在原地");
            Assert.AreEqual(2, engine.CreepCount, "同元素相遇不产生任何反应");

            bool hasBump = false;
            foreach (var e in step.Events) if (e.Type == GameEventType.Bump) hasBump = true;
            Assert.IsTrue(hasBump, "应该记录一次「禁止移动」事件");
        }

        /// <summary>
        /// 让 uid 最小的那只小怪按指定「步数 + 方向」走一步，其余小怪全部原地不动，
        /// 然后把这一回合走完。方向：0=+Y 1=+X 2=-Y 3=-X。
        ///
        /// 为什么要给每只小怪都塞一对取值：准备阶段按 uid 升序推进，每只消耗
        /// (1 次预算 + 1 次方向) 共两次取值，而脚本随机是循环取用的 ——
        /// 只写一对取值的话，第二只小怪会拿到同一对、跟着一起走，测试就不再可复现。
        /// </summary>
        static StepRecord StepOnce(GameEngine engine, int steps, int direction)
        {
            var values = new List<int> { steps, direction };
            for (int i = 1; i < engine.CreepCount; i++) values.AddRange(new[] { 0, 0 });

            engine.SetNextRandomValues(values.ToArray());
            var record = engine.AdvancePreparation();
            engine.EndPreparationNow(); // 其余小怪原地待命
            return record;
        }

        [Test]
        public void 不同元素相遇_水木相遇_水消失而木加一()
        {
            // 水行「与木牌相遇木元素+1」→ 木赢：撞上去的水消失，木+1。
            // 注意被撞上的木同时会消失，所以净效果是「原来的木消失 + 新生一个木」。
            var engine = Rig(BaseRules(),
                (Element.Water, new Pos(0, 0)),
                (Element.Wood, new Pos(2, 0)));

            var step = StepOnce(engine, 3, 1);

            Assert.IsNotNull(step);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Water), "水消失");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Wood), "木+1：原来的木被撞掉，另生成一个新的木");
            Assert.AreEqual(2, engine.CreepCount);
            Assert.IsTrue(step.CreepWentAway, "消失的是主动撞上去的水");

            bool hasSpawn = false;
            Pos spawnAt = Pos.None;
            foreach (var e in step.Events)
                if (e.Type == GameEventType.Spawn) { hasSpawn = true; spawnAt = e.To; }
            Assert.IsTrue(hasSpawn, "应该记录一次生成事件");
            Assert.AreNotEqual(new Pos(2, 0), spawnAt, "新元素不能盖在原有的木上");
            Assert.IsTrue(Board.Manhattan(new Pos(2, 0), spawnAt) == 1, "新元素应该紧贴反应发生点");
        }

        [Test]
        public void 不同元素相遇_水撞火_火消失而水加一()
        {
            // 水行「与火元素相遇，该火元素消失」→ 水赢、火输。
            // 按「被点名的元素是输家」读：「该火元素消失」点名的是火，所以火消失。
            var engine = Rig(BaseRules(),
                (Element.Water, new Pos(0, 0)),
                (Element.Fire, new Pos(2, 0)));

            var step = StepOnce(engine, 3, 1);

            Assert.IsNotNull(step);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "被点名的火消失");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Water), "水赢 → 水+1（撞上去的那只 + 新生成的一只）");
            Assert.AreEqual(2, engine.CreepCount);
            Assert.IsFalse(step.CreepWentAway, "消失的是被撞上的火，不是主动撞上去的水");
        }

        [Test]
        public void 不同元素相遇_火撞金_火消失而金加一()
        {
            // 火行「与金元素相遇，该金元素消失」；土行也写「与金牌相遇金元素+1」——
            // 两处都让金赢，所以火 × 金的结果是火消失、金 +1。
            var engine = Rig(BaseRules(),
                (Element.Fire, new Pos(0, 0)),
                (Element.Metal, new Pos(2, 0)));

            var step = StepOnce(engine, 3, 1);

            Assert.IsNotNull(step);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "火消失");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Metal), "金赢 → 金+1");
            Assert.AreEqual(2, engine.CreepCount);
            Assert.IsTrue(step.CreepWentAway, "消失的是主动撞上去的火");
        }

        [Test]
        public void 不同元素相遇_水和土_土赢水输()
        {
            // 水行：「与土元素/土牌相遇，该水元素消失」；土行：「与水元素相遇，该水元素消失」。
            // 两条都只说水消失 → 赢家是土，能唯一确定。
            var engine = Rig(BaseRules(),
                (Element.Water, new Pos(0, 0)),
                (Element.Earth, new Pos(2, 0)));

            var step = StepOnce(engine, 3, 1);

            Assert.IsNotNull(step);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Water), "水撞上去后消失");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Earth), "土赢 → 土+1");
            Assert.AreEqual(2, engine.CreepCount, "原来的土 + 新生成的土");
            Assert.IsTrue(step.CreepWentAway, "消失的是主动撞上去的水");
        }

        [Test]
        public void 反应生成_撞上去的一方消失并生成一个新的赢家()
        {
            // 火 × 金：金赢、火输。所以撞上去的火消失，并在金旁边生成一个新的金。
            // 顺带验证出生统计：成功生成 1 个、出生失败 0 次。
            var engine = new GameEngine(BaseRules(), 1);
            engine.ClearBoard();

            // 火放最前面（uid 最小）→ 第一只被推进的就是它
            var fire = engine.PlaceCreep(Element.Fire, new Pos(2, 0));
            Assert.IsNotNull(fire, "火应该放得下");

            var metal = engine.PlaceCreep(Element.Metal, new Pos(2, 1));
            Assert.IsNotNull(metal, "金应该放得下");

            var bystander = engine.PlaceCreep(Element.Earth, new Pos(7, 7));
            Assert.IsNotNull(bystander, "旁观元素应该放得下");
            engine.BeginRound();

            // 火向上走 1 格撞上金；其余小怪原地不动
            StepOnce(engine, 1, 0);

            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "火撞上去后消失");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Metal), "金赢 → 金+1（原来的金 + 新生成的金）");
            Assert.AreEqual(3, engine.CreepCount, "两个金 + 一个旁观元素");
            Assert.AreEqual(1, engine.TotalSpawned, "成功生成 1 个新元素");
            Assert.AreEqual(0, engine.SpawnBlockedCount, "棋盘很空，不该出生失败");

            bool vanished = false;
            foreach (var s in engine.History)
                foreach (var e in s.Events)
                    if (e.Type == GameEventType.Vanish) vanished = true;
            Assert.IsTrue(vanished, "应该有 Vanish 事件");

            var seen = new System.Collections.Generic.HashSet<Pos>();
            foreach (var c in engine.Creeps) Assert.IsTrue(seen.Add(c.Pos), $"{c.Pos} 被占了两次");
        }

        // ------------------------------------------------------------ 小瓶子与卡牌

        [Test]
        public void 每消失一个元素_对应瓶子加一_满瓶产出卡牌()
        {
            var rules = BaseRules();
            rules.BottleCapacity = 5;
            var engine = Rig(rules, (Element.Water, new Pos(0, 0)));

            // 连续 4 次消失：瓶子 1..4，不发牌
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Water), $"第 {i + 1} 次消失");
            }
            Assert.AreEqual(4, engine.Bottle.Count(Element.Water));
            Assert.AreEqual(0, engine.Hand.Count, "还没满瓶，不应有牌");

            // 第 5 次消失：满瓶 → 出一张水牌 + 计数归零
            Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Water));
            Assert.AreEqual(0, engine.Bottle.Count(Element.Water), "满瓶后计数归零");
            Assert.AreEqual(1, engine.Hand.Count, "满瓶应产出 1 张牌");
            Assert.AreEqual(Element.Water, engine.Hand[0].Element);
        }

        [Test]
        public void 出牌_把牌当作虚拟元素与目标反应_目标消失并有新元素生成()
        {
            var rules = BaseRules();
            rules.InitialHandSize = 1;
            rules.CardEffect = CardEffectMode.CardAsVirtualElement;

            var engine = new GameEngine(rules, 5);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(3, 3));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Wood }); // 水 遇 木 → 木+1
            engine.FreezeHandLimitForTest();                 // 本用例要精确控制手牌，别让抽牌阶段补牌
            engine.SetupBottleForTest(0);                    // 本用例不测瓶子，避免满瓶发牌干扰
            engine.SetNextRandomValues(0, 0);                // 准备阶段别把小怪挪走
            engine.EndPreparationNow();

            Assert.AreEqual(1, engine.Hand.Count, "手牌应该只有刚发的那张");
            Assert.IsNotNull(engine.Board.At(new Pos(3, 3)), "目标小怪应该在原位");
            var card = engine.Hand[0];

            var record = engine.TryPlayCard(card.Id, new Pos(3, 3));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(0, engine.Hand.Count, "出牌后手牌 -1");
            Assert.AreEqual(0, engine.Board.CountOf(Element.Water), "水被牌消灭");
            Assert.AreEqual(1, engine.Board.CountOf(Element.Wood), "按图2「水 遇 木 → 木+1」生成 1 个木");
            Assert.AreEqual(1, engine.CreepCount, "消失与生成各一次，总数仍为 1");

            bool vanished = false, spawned = false;
            foreach (var e in record.Events)
            {
                if (e.Type == GameEventType.Vanish) vanished = true;
                if (e.Type == GameEventType.Spawn) spawned = true;
            }
            Assert.IsTrue(vanished, "应该记录一次消失");
            Assert.IsTrue(spawned, "应该记录一次生成");
        }

        [Test]
        public void 出牌_水牌打火小怪_火消失并生成一个新水()
        {
            var rules = BaseRules();
            rules.CardEffect = CardEffectMode.CardAsVirtualElement;

            var engine = new GameEngine(rules, 5);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Fire, new Pos(2, 2));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Water }); // 火 × 水 → 水赢、火消失
            engine.FreezeHandLimitForTest();
            engine.SetupBottleForTest(0);
            engine.SetNextRandomValues(0, 0);                // 准备阶段别把小怪挪走
            engine.EndPreparationNow();

            Assert.IsNotNull(engine.Board.At(new Pos(2, 2)), "目标小怪应该在原位");
            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(2, 2));

            Assert.IsNotNull(record, engine.FailReason);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "火消失");
            Assert.AreEqual(1, engine.Board.CountOf(Element.Water), "水赢 → 生成 1 个水");
            Assert.AreEqual(1, engine.CreepCount);
        }

        [Test]
        public void 出牌_不构成反应时被拒绝()
        {
            var rules = BaseRules();
            rules.CardEffect = CardEffectMode.CardAsVirtualElement;

            var engine = new GameEngine(rules, 5);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(3, 3));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Water }); // 水 × 水 不发生反应
            engine.FreezeHandLimitForTest();
            engine.SetupBottleForTest(0);
            engine.EndPreparationNow();

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(3, 3));

            Assert.IsNull(record, "同元素之间不发生反应，这次出牌应该被拒绝");
            Assert.IsNotNull(engine.FailReason);
            Assert.AreEqual(1, engine.Hand.Count, "被拒绝时不该消耗手牌");
            Assert.AreEqual(1, engine.CreepCount);
        }

        [Test]
        public void 出牌_只能作用于棋盘上真实存在的元素()
        {
            var rules = BaseRules();
            var engine = new GameEngine(rules, 5);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Earth, new Pos(4, 4));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Water });
            engine.FreezeHandLimitForTest();
            engine.SetupBottleForTest(0);
            engine.SetNextRandomValues(0, 0);
            engine.EndPreparationNow();

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(0, 0)); // 空格

            Assert.IsNull(record);
            StringAssert.Contains("没有元素", engine.FailReason);
        }

        // ------------------------------------------------------------ 回合与胜负

        [Test]
        public void 回合推进_准备阶段结束进入玩家行动阶段()
        {
            var engine = Rig(BaseRules(), (Element.Water, new Pos(0, 0)));
            engine.SetNextRandomValues(0, 0); // 唯一一只小怪不移动

            Assert.AreEqual(GamePhase.Preparation, engine.Phase);

            var first = engine.AdvancePreparation();
            Assert.IsNotNull(first, "第一只小怪应该被推进");
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase, "只有一只小怪，走完就该进入玩家行动阶段");

            var second = engine.AdvancePreparation();
            Assert.IsNull(second, "准备阶段已经结束，再推只会得到 null");
        }

        [Test]
        public void 整回合流程_准备到判定再到下一回合()
        {
            var engine = Rig(BaseRules(), (Element.Water, new Pos(0, 0)), (Element.Fire, new Pos(7, 7)));
            engine.SetNextRandomValues(0, 0); // 两只都不移动

            var a = engine.AdvancePreparation();
            Assert.IsNotNull(a, "第 1 只小怪");

            var b = engine.AdvancePreparation();
            Assert.IsNotNull(b, "第 2 只小怪");
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase, "两只小怪走完 → 抽牌 → 玩家行动");

            engine.EndPlayerPhase();
            Assert.AreEqual(GamePhase.Discard, engine.Phase, "出牌阶段结束 → 弃牌阶段");

            engine.EndDiscardPhase();
            Assert.AreEqual(GamePhase.Judgment, engine.Phase, "弃牌阶段结束 → 判定阶段");

            bool continued = engine.ResolveJudgment();
            Assert.IsTrue(continued, "没达成胜利条件，应该继续");
            Assert.AreEqual(2, engine.Round, "回合数 +1");
            Assert.AreEqual(GamePhase.Preparation, engine.Phase);
        }

        // ------------------------------------------------------------ 抽牌 / 弃牌阶段

        [Test]
        public void 抽牌阶段_自动把手牌补到上限()
        {
            var rules = BaseRules();
            rules.HandLimit = 5;
            rules.InitialHandSize = 2;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 11);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.Start(); // Start 会发 2 张初始手牌
            Assert.AreEqual(2, engine.Hand.Count, "初始手牌 2 张");

            engine.EndPreparationNow();

            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);
            Assert.AreEqual(5, engine.Hand.Count, "抽牌阶段应该补到上限 5 张");
            Assert.AreEqual(3, engine.CardsDrawnThisRound, "本次补了 3 张");
        }

        [Test]
        public void 抽牌阶段_手牌已满时一张都不抽()
        {
            var rules = BaseRules();
            rules.HandLimit = 5;
            rules.InitialHandSize = 5;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 12);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.Start();
            Assert.AreEqual(5, engine.Hand.Count);

            engine.EndPreparationNow();

            Assert.AreEqual(5, engine.Hand.Count, "本来就满 → 不抽牌");
            Assert.AreEqual(0, engine.CardsDrawnThisRound);
        }

        [Test]
        public void 抽牌阶段_瓶子产出的牌不会被抽牌覆盖()
        {
            // 手牌超过上限时不应该被抽牌阶段「削掉」——抽牌只补不删
            var rules = BaseRules();
            rules.HandLimit = 3;
            rules.InitialHandSize = 0;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 13);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Metal, Element.Wood, Element.Fire, Element.Earth, Element.Water });
            Assert.AreEqual(5, engine.Hand.Count);

            engine.SetNextRandomValues(0, 0);
            engine.EndPreparationNow();

            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);
            Assert.AreEqual(5, engine.Hand.Count, "超过上限也不会被抽牌阶段删掉");
            Assert.AreEqual(0, engine.CardsDrawnThisRound, "已经超过上限 → 不补牌");
        }

        [Test]
        public void 弃牌阶段_弃牌减少手牌且收到上限内()
        {
            var rules = BaseRules();
            rules.HandLimit = 3;
            rules.MaxDiscardsPerRound = 5;
            rules.InitialHandSize = 0;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 14);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Metal, Element.Wood, Element.Fire, Element.Earth, Element.Water });
            engine.FreezeHandLimitForTest(); // 本用例要精确控制手牌，别让抽牌阶段补牌
            engine.SetNextRandomValues(0, 0);
            engine.EndPreparationNow();
            Assert.AreEqual(5, engine.Hand.Count, "抽牌阶段不补也不删");

            engine.EndPlayerPhase();
            Assert.AreEqual(GamePhase.Discard, engine.Phase);

            var firstId = engine.Hand[0].Id;
            Assert.IsTrue(engine.TryDiscardCard(firstId), engine.FailReason);
            Assert.AreEqual(4, engine.Hand.Count, "弃牌后手牌 -1");
            Assert.IsNull(engine.FindCard(firstId), "弃掉的牌应该离开手牌");
            Assert.AreEqual(1, engine.DiscardsUsed);

            // 弃到上限以内就够本回合用；再弃也允许（图1 写「可以全部弃完」，不强制停在上限）
            Assert.IsTrue(engine.TryDiscardCard(engine.Hand[0].Id));
            Assert.IsTrue(engine.TryDiscardCard(engine.Hand[0].Id));
            Assert.AreEqual(2, engine.Hand.Count, "5 张弃掉 3 张 → 剩 2 张");
            Assert.AreEqual(3, engine.DiscardsUsed);
            Assert.AreEqual(2, engine.DiscardsRemaining, "上限 5 - 已弃 3 = 还可弃 2 张");
        }

        [Test]
        public void 弃牌阶段_每回合弃牌数量有上限()
        {
            var rules = BaseRules();
            rules.HandLimit = 10;
            rules.MaxDiscardsPerRound = 2;    // 每回合最多弃 2 张
            rules.InitialHandSize = 0;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 15);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Metal, Element.Wood, Element.Fire, Element.Earth });
            engine.FreezeHandLimitForTest(); // 冻在当前 4 张，抽牌阶段不会补到 10
            engine.SetNextRandomValues(0, 0);
            engine.EndPreparationNow();
            Assert.AreEqual(4, engine.Hand.Count);

            engine.EndPlayerPhase();

            Assert.IsTrue(engine.TryDiscardCard(engine.Hand[0].Id));
            Assert.IsTrue(engine.TryDiscardCard(engine.Hand[0].Id));
            Assert.AreEqual(0, engine.DiscardsRemaining);

            int id = engine.Hand[0].Id;
            Assert.IsFalse(engine.TryDiscardCard(id), "超过每回合上限就该被拒绝");
            Assert.AreEqual(2, engine.DiscardsUsed);
            Assert.AreEqual(2, engine.Hand.Count, "被拒绝时不该消耗手牌");
        }

        [Test]
        public void 弃牌阶段_不能出牌()
        {
            var rules = BaseRules();
            rules.HandLimit = 5;
            rules.InitialHandSize = 0;
            rules.CreepsPerElement = 0;
            rules.MaxMoveSteps = 0;

            var engine = new GameEngine(rules, 16);
            engine.ClearBoard();
            var water = engine.PlaceCreep(Element.Water, new Pos(0, 0));
            var fire = engine.PlaceCreep(Element.Fire, new Pos(1, 0));
            Assert.IsNotNull(water);
            Assert.IsNotNull(fire);
            engine.BeginRound();
            engine.ForceHandForTest(new[] { Element.Water });
            engine.FreezeHandLimitForTest(); // 只留这一张，别让抽牌阶段补满
            engine.SetNextRandomValues(0, 0);
            engine.EndPreparationNow();

            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase);
            Assert.AreEqual(1, engine.Hand.Count);

            // 出牌阶段能出牌：水牌打火小怪 → 水赢、火消失
            var played = engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 0));
            Assert.IsNotNull(played, engine.FailReason);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Fire), "火应该被水牌清掉");

            engine.EndPlayerPhase();
            Assert.AreEqual(GamePhase.Discard, engine.Phase);

            // 弃牌阶段不能再出牌
            engine.ForceHandForTest(new[] { Element.Wood });
            var blocked = engine.TryPlayCard(engine.Hand[0].Id, new Pos(0, 0));
            Assert.IsNull(blocked, "弃牌阶段不该能出牌");
            StringAssert.Contains("玩家行动阶段", engine.FailReason);
        }

        [Test]
        public void 胜利条件_只剩水元素()
        {
            var rules = BaseRules();
            rules.WinCondition = WinCondition.WaterOnly;

            var engine = Rig(rules, (Element.Water, new Pos(0, 0)), (Element.Water, new Pos(1, 0)));
            Assert.IsTrue(engine.CheckWinCondition(out var reason), reason);

            var engine2 = Rig(rules, (Element.Water, new Pos(0, 0)), (Element.Fire, new Pos(1, 0)));
            Assert.IsFalse(engine2.CheckWinCondition(out _), "场上还有火，不算胜利");
        }

        [Test]
        public void 失败条件_回合数达到上限()
        {
            var rules = BaseRules();
            rules.RoundLimit = 2;
            rules.CreepsPerElement = 0;
            rules.WinCondition = WinCondition.WaterOnly;

            var engine = new GameEngine(rules, 3);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.PlaceCreep(Element.Fire, new Pos(7, 7)); // 有火在，永远达不到「只剩水」
            engine.BeginRound();

            for (int i = 0; i < rules.RoundLimit; i++)
            {
                engine.SetNextRandomValues(0, 0, 0, 0);
                engine.SimulateRound();
            }

            Assert.AreEqual(GamePhase.Defeat, engine.Phase, "第 2 回合结束仍未胜利 → 失败");
            Assert.IsTrue(engine.DefeatByRoundLimit);
            Assert.AreEqual(rules.RoundLimit, engine.Round, "回合数不应该超过上限");
        }

        [Test]
        public void 长时间跑批_任意种子都不抛异常且必然终局()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var rules = new Rules { RoundLimit = 15 };
                var engine = new GameEngine(rules, seed);
                engine.Start();

                int guard = 0;
                while (!engine.IsOver && guard++ < 100)
                {
                    // 一步一步推进，好在重叠出现的那一步就把现场打出来
                    int stepGuard = 0;
                    while (engine.Phase == GamePhase.Preparation && stepGuard++ < 500)
                    {
                        var step = engine.AdvancePreparation();
                        if (step == null) break;
                        AssertNoOverlap(engine, seed, step);
                    }

                    if (engine.Phase == GamePhase.PlayerAction) engine.EndPlayerPhase();
                    if (engine.Phase == GamePhase.Discard) engine.EndDiscardPhase();
                    engine.ResolveJudgment();
                    AssertNoOverlap(engine, seed, null);
                }

                Assert.IsTrue(engine.IsOver, $"seed={seed} 跑不完 {rules.RoundLimit} 回合");
                Assert.LessOrEqual(engine.Round, rules.RoundLimit, $"seed={seed} 回合数超上限");
            }
        }

        static void AssertNoOverlap(GameEngine engine, int seed, StepRecord step)
        {
            var seen = new System.Collections.Generic.Dictionary<Pos, Creep>();
            foreach (var c in engine.Creeps)
            {
                if (seen.TryGetValue(c.Pos, out var other))
                {
                    string detail = step == null ? "(玩家/判定阶段)" : step.ToString();
                    var events = new System.Text.StringBuilder();
                    if (step != null)
                        foreach (var e in step.Events) events.Append(" | ").Append(e.ToString());
                    Assert.Fail($"seed={seed} 回合{engine.Round} {c.Pos} 重叠：{other} 与 {c}；步骤={detail}{events}");
                }
                seen[c.Pos] = c;
            }
        }
    }
}