using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.EditorTools;
using TapGJ.Levels;
using TapGJ.Levels.Mechanics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 关卡可配置面测试：可用元素（ElementPool）、可抽卡牌（CardPool）、
    /// 字符画的池外元素拒绝、以及「这一关能不能玩」的配置检查。
    ///
    /// 核心不变量：
    ///   · 配置正确时，棋盘上**永远只出现池内元素**（随机铺盘 / 出牌相生 / 机制生成 都不例外）
    ///   · 池子留空（旧数据）等价于「全部元素」，行为与加字段之前逐位一致
    ///   · 死局配置（目标要水但池里没水 / 要撑的回合超过回合上限 …）必须被指出来
    /// </summary>
    public class LevelConfigTests
    {
        const ElementMask MetalWater = ElementMask.Metal | ElementMask.Water;

        // ---------------------------------------------------------------- 工具

        static Rules MakeRules(ElementMask elements, ElementMask cards, int size = 4,
            int creepsPerElement = 0, int initialHand = 0, GoalSpec? goal = null)
            => new Rules
            {
                BoardWidth = size,
                BoardHeight = size,
                CreepsPerElement = creepsPerElement,
                InitialHandSize = initialHand,
                HandLimit = 30,
                RoundLimit = 15,
                ElementPool = elements,
                CardPool = cards,
                Goal = goal ?? GoalSpec.Survive(999), // 默认给个不会提前结束的目标，方便观察棋盘演化
            };

        static GameEngine NewEngine(Rules rules, int seed = 5)
        {
            var engine = new GameEngine(rules, seed);
            engine.Start();
            return engine;
        }

        static List<string> Issues(Rules rules)
        {
            var into = new List<string>();
            rules.CollectIssues(into);
            return into;
        }

        static LevelDefinition NewLevel(System.Action<LevelDefinition> setup = null)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.levelId = "cfg_test";
            level.displayName = "配置测试关";
            level.rules = new Rules();
            setup?.Invoke(level);
            return level;
        }

        // ---------------------------------------------------------------- ElementMask

        [Test]
        public void 元素掩码_取值与显示()
        {
            Assert.IsTrue(ElementMask.All.Has(Element.Fire));
            Assert.IsFalse(ElementMask.None.Has(Element.Fire));
            Assert.AreEqual(ElementMask.Water, ElementSet.Of(Element.Water));
            Assert.AreEqual(5, ElementMask.All.Count());
            Assert.AreEqual(2, MetalWater.Count());

            Assert.AreEqual("金水", MetalWater.Names());
            Assert.AreEqual("全部", ElementMask.All.Names());
            Assert.AreEqual("（空）", ElementMask.None.Names());

            // 展开顺序固定是金木水火土（随机可复现靠它）
            CollectionAssert.AreEqual(new[] { Element.Metal, Element.Water }, MetalWater.ToArray());
            Assert.AreEqual(ElementMask.Water | ElementMask.Fire,
                ElementSet.ToMask(new[] { Element.Fire, Element.Water }));
        }

        [Test]
        public void 元素池_留空等于全部的兼容行为()
        {
            Assert.AreEqual(ElementMask.All, new Rules().ElementPool, "新字段的初始值必须是全部");
            Assert.AreEqual(ElementMask.All, new Rules().CardPool);

            // 加字段之前存下来的数据反序列化出来是 None，Validate 要修成「全部」
            var old = new Rules { ElementPool = ElementMask.None, CardPool = ElementMask.None };
            old.Validate();
            Assert.AreEqual(ElementMask.All, old.ElementPool);
            Assert.AreEqual(ElementMask.All, old.CardPool);
        }

        // ---------------------------------------------------------------- 随机铺盘 / 抽牌

        [Test]
        public void 可用元素_随机铺盘只用池内元素()
        {
            var rules = MakeRules(MetalWater, MetalWater, 6, creepsPerElement: 3);
            Assert.AreEqual(6, rules.InitialCreepCount, "每种可用元素 3 个 × 2 种");

            var engine = NewEngine(rules);
            Assert.AreEqual(6, engine.CreepCount);
            foreach (var creep in engine.Creeps)
                Assert.IsTrue(MetalWater.Has(creep.Element), $"不该出现{ElementDefs.Name(creep.Element)}");
        }

        [Test]
        public void 可抽卡牌_只抽池内的牌()
        {
            var rules = MakeRules(MetalWater, ElementMask.Water | ElementMask.Fire, 4, initialHand: 30);
            var engine = NewEngine(rules);

            Assert.AreEqual(30, engine.Hand.Count);
            var allowed = new[] { Element.Water, Element.Fire };
            foreach (var card in engine.Hand)
                CollectionAssert.Contains(allowed, card.Element, $"{card.Label} 不该在牌池里");

            // 两种都抽到过（30 次二选一全同的概率可以忽略）
            Assert.IsTrue(engine.Hand.Any(c => c.Element == Element.Water));
            Assert.IsTrue(engine.Hand.Any(c => c.Element == Element.Fire));
        }

        // ---------------------------------------------------------------- 生成 / 出牌的硬约束

        [Test]
        public void 可用元素_机制生成池外元素被拒绝()
        {
            var engine = NewEngine(MakeRules(MetalWater, MetalWater));
            var cell = new Pos(1, 1);

            Assert.IsNull(engine.SpawnAt(cell, Element.Fire), "火不在池里，不能生成");
            Assert.AreEqual(0, engine.CreepCount);

            Assert.IsNotNull(engine.SpawnAt(cell, Element.Water));
            Assert.AreEqual(1, engine.CreepCount);
        }

        [Test]
        public void 可用元素_池外的牌打不出去()
        {
            // 元素池只有金水，牌池却给了水（故意配错：牌池必须是元素池的子集）
            var rules = MakeRules(ElementMask.Metal, ElementMask.Water);
            var engine = NewEngine(rules);

            engine.EndPreparationNow();
            engine.PlaceCreep(Element.Metal, new Pos(1, 1));
            engine.ForceHandForTest(new[] { Element.Water });

            Assert.IsNull(engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1)),
                "水牌打出去会把金变成水（池外），必须拒绝");
            StringAssert.Contains("本关可用元素", engine.FailReason);
            Assert.AreEqual(Element.Metal, engine.Board.At(new Pos(1, 1)).Element, "拒绝之后棋盘不该变");
        }

        [Test]
        public void 可用元素_相生本应生成池外元素时记为出生失败()
        {
            // 池里是金水木，棋盘上却有一只火 —— 正常路径不会这样（字符画会拦、随机铺盘不会铺），
            // 只有直接塞的 LevelSetup 才会（它被信任，不做过滤），所以这是防御性分支。
            //
            // 木牌打火：木×火 是相生，而且**火赢** → 目标不变，但要在旁边生成 1 个火；
            // 火不在池里 → 应该记一次出生失败，并说明是「本关不用这个元素」。
            var rules = MakeRules(ElementMask.Metal | ElementMask.Water | ElementMask.Wood, ElementMask.Wood);
            var engine = NewEngine(rules);

            engine.EndPreparationNow();
            engine.PlaceCreep(Element.Fire, new Pos(1, 1));
            engine.ForceHandForTest(new[] { Element.Wood });

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1));
            Assert.IsNotNull(record, engine.FailReason);

            Assert.AreEqual(1, engine.SpawnBlockedCount, "池外元素应该被拦下并记一次出生失败");
            Assert.IsTrue(record.Events.Any(e => e.Type == GameEventType.SpawnBlocked
                                                 && e.Text.Contains("本关可用元素")),
                "出生失败要说明是「本关不用这个元素」，而不是「周围没空格」");
            Assert.AreEqual(1, engine.CreepCount, "火没被生成出来，场上还是那一只");
            Assert.AreEqual(Element.Fire, engine.Board.At(new Pos(1, 1)).Element, "目标元素也不该被改掉");
        }

        [Test]
        public void 不变量_限制元素池后棋盘上不出现池外元素()
        {
            var rules = MakeRules(MetalWater, MetalWater, 6, creepsPerElement: 3);
            var engine = NewEngine(rules);

            for (int round = 0; round < 15 && !engine.IsOver; round++)
            {
                engine.SimulateRound();
                foreach (var creep in engine.Creeps)
                    Assert.IsTrue(MetalWater.Has(creep.Element),
                        $"第 {engine.Round} 回合棋盘上出现了池外的{ElementDefs.Name(creep.Element)}");
            }
        }

        // ---------------------------------------------------------------- 字符画 + 元素池

        [Test]
        public void 字符画_池外元素被拒绝并指出行列()
        {
            const string text = "金火水\n...\n...";

            Assert.IsTrue(BoardLayoutParser.TryParse(text, 3, 3, out _, out var ok), ok);

            Assert.IsFalse(BoardLayoutParser.TryParse(text, 3, 3, MetalWater, out _, out var error),
                "「火」不在可用元素里，应该拒绝");
            StringAssert.Contains("第 1 行第 2 列", error);
            StringAssert.Contains("火", error);
            StringAssert.Contains("金水", error);

            Assert.IsFalse(BoardLayoutParser.TryParse("金X水\n...\n...", 3, 3, MetalWater, out _, out var badChar),
                "未知字符仍然要按原来的方式拒绝");
            StringAssert.Contains("不是合法字符", badChar);
        }

        // ---------------------------------------------------------------- 配置检查（死局）

        [Test]
        public void 配置检查_牌池必须是元素池的子集()
        {
            var rules = MakeRules(MetalWater, MetalWater | ElementMask.Fire);
            var issues = Issues(rules);

            Assert.IsTrue(issues.Any(i => i.Contains("火") && i.Contains("不在可用元素")),
                "要把「牌池里有元素池没有的元素」指出来：" + string.Join(" / ", issues));

            // 子集就没问题（注意给一个本身合法的目标，别让默认目标 999 回合 自己变成问题）
            Assert.IsEmpty(Issues(MakeRules(MetalWater, ElementMask.Water, goal: GoalSpec.Survive(10))));
        }

        [Test]
        public void 配置检查_各种死局都能指出来()
        {
            // 只剩水，但池里没水
            var noWater = MakeRules(ElementMask.Metal | ElementMask.Wood, ElementMask.Metal,
                goal: GoalSpec.WaterOnly());
            Assert.IsTrue(Issues(noWater).Any(i => i.Contains("没有水") && i.Contains("永远赢不了")));

            // 要撑 20 回合，但上限只有 15
            var survive = MakeRules(MetalWater, MetalWater, goal: GoalSpec.Survive(20));
            Assert.IsTrue(Issues(survive).Any(i => i.Contains("撑过 20 回合") && i.Contains("永远赢不了")));

            // 盯着不在池里的元素
            var atMost = MakeRules(MetalWater, MetalWater,
                goal: GoalSpec.ElementAtMost(Element.Fire, 0));
            Assert.IsTrue(Issues(atMost).Any(i => i.Contains("火") && i.Contains("开局即胜")));

            // 只剩一种元素，而池里本来就只有一种
            var single = MakeRules(ElementMask.Metal, ElementMask.Metal, goal: GoalSpec.SingleElement());
            Assert.IsTrue(Issues(single).Any(i => i.Contains("只剩一种元素") && i.Contains("立刻胜利")));

            // 一份正常的配置：一条问题都不该有
            var clean = MakeRules(MetalWater, ElementMask.Water, goal: GoalSpec.Survive(10));
            Assert.IsEmpty(Issues(clean), string.Join(" / ", Issues(clean)));
        }

        // ---------------------------------------------------------------- 关卡资产的校验

        [Test]
        public void 关卡校验_干净的一关没有问题()
        {
            var level = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, ElementMask.Water, 3, goal: GoalSpec.WaterOnly());
                l.layoutText = "金..\n...\n..水";
            });

            try
            {
                var issues = new List<string>();
                level.CollectAuthoringIssues(issues);
                Assert.IsEmpty(issues, string.Join(" / ", issues));
                StringAssert.Contains("金水", level.Summary());
            }
            finally { Object.DestroyImmediate(level); }
        }

        [Test]
        public void 关卡校验_布局错误与自定义目标都能指出()
        {
            var bad = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, MetalWater, 3);
                l.layoutText = "金..\n..."; // 少一行
            });
            var custom = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, MetalWater, 3, goal: GoalSpec.Custom());
                l.levelPrefab = null;
            });
            var hand = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, ElementMask.Water, 3);
                l.fixedInitialHand = new[] { Element.Fire }; // 火牌不在牌池里
            });
            var noId = NewLevel(l => l.levelId = "");

            try
            {
                StringAssert.Contains("行数", string.Join(" / ", Collect(bad)));
                StringAssert.Contains("自定义", string.Join(" / ", Collect(custom)));
                StringAssert.Contains("固定起手牌", string.Join(" / ", Collect(hand)));
                StringAssert.Contains("levelId", string.Join(" / ", Collect(noId)));
            }
            finally
            {
                foreach (var l in new[] { bad, custom, hand, noId }) Object.DestroyImmediate(l);
            }
        }

        [Test]
        public void 关卡校验_自定义目标挂了机制就算通过()
        {
            var go = new GameObject("cfg-mech");
            var level = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, MetalWater, 3, goal: GoalSpec.Custom());
            });

            try
            {
                go.AddComponent<ReactionCountRule>();
                level.levelPrefab = go;

                var issues = Collect(level);
                Assert.IsFalse(issues.Any(i => i.Contains("自定义")),
                    "挂了启用的机制组件就不该再报「永远赢不了」：" + string.Join(" / ", issues));
                StringAssert.Contains("机制 1 个", level.Summary());
            }
            finally
            {
                Object.DestroyImmediate(level);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 关卡校验_不改动关卡资产本身()
        {
            // 模拟「加元素池字段之前存下来的旧资产」：反序列化出来是 None
            var level = NewLevel(l =>
            {
                l.rules = MakeRules(MetalWater, MetalWater, 3, goal: GoalSpec.Survive(10));
                l.rules.ElementPool = ElementMask.None;
                l.rules.CardPool = ElementMask.None;
            });

            try
            {
                var issues = Collect(level);

                Assert.AreEqual(ElementMask.None, level.rules.ElementPool, "校验是只读的，不能顺手把资产改了");
                Assert.AreEqual(ElementMask.None, level.rules.CardPool);
                Assert.IsTrue(issues.Any(i => i.Contains("可用元素一个都没勾")),
                    "要告诉设计师「留空 = 全部」而不是装作没看见：" + string.Join(" / ", issues));
                StringAssert.Contains("全部", level.Summary(), "摘要里也要按运行时的实际行为显示");
            }
            finally { Object.DestroyImmediate(level); }
        }

        [Test]
        public void 空元素池_解析布局会拒绝一切元素_所以必须先补默认值()
        {
            // 这条钉住「LevelLoader 必须先 Clone + Validate 再把元素池交给解析器」这个顺序：
            // 拿旧资产的原始值（None）去解析，布局里每个元素都会被判成「池外」。
            Assert.IsFalse(BoardLayoutParser.TryParse("金..\n...\n...", 3, 3, ElementMask.None, out _, out var error));
            StringAssert.Contains("（空）", error);

            var rules = new Rules { ElementPool = ElementMask.None };
            rules.Validate();
            Assert.IsTrue(BoardLayoutParser.TryParse("金..\n...\n...", 3, 3, rules.ElementPool, out _, out _),
                "Validate 补好默认值之后就应该能正常解析");
        }

        static List<string> Collect(LevelDefinition level)
        {
            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);
            return issues;
        }
    }

    /// <summary>
    /// 「机制自己检查配置隐患」的测试（ILevelRuleValidator）。
    ///
    /// 这两条隐患光看数值看不出来，必须结合机制自身的语义：
    ///   · 不勾「必须都在场」→ 目标会退化成「只剩一种元素」（相克删起来快得多）
    ///   · 勾了但布局里没有那种元素 → 永远赢不了
    /// </summary>
    public class LevelRuleValidatorTests
    {
        const ElementMask MetalWater = ElementMask.Metal | ElementMask.Water;

        static LevelDefinition NewLevel(ElementMask pool, string layout, bool requireAllPool,
            ElementMask? cardPool = null, EmptyCellCardMode emptyCell = EmptyCellCardMode.Summon)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.levelId = "validator_test";
            level.displayName = "机制自查";
            level.rules = new Rules
            {
                BoardWidth = 3,
                BoardHeight = 3,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                RoundLimit = 15,
                ElementPool = pool,
                CardPool = cardPool ?? pool,
                EmptyCellCard = emptyCell,
                Goal = GoalSpec.Custom(),
            };
            level.layoutText = layout;

            // 机制挂在一个临时物体上（不用真预制体，GetComponentsInChildren 一样能收到）
            var host = new GameObject("validator-mech-host");
            var rule = host.AddComponent<EqualElementCountsRule>();
            SetBool(rule, "requireAllPoolElements", requireAllPool);
            level.levelPrefab = host;
            return level;
        }

        static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static List<string> Collect(LevelDefinition level)
        {
            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);
            return issues;
        }

        static void Dispose(LevelDefinition level)
        {
            var host = level.levelPrefab;
            Object.DestroyImmediate(level);
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void 不勾必须都在场_会警告目标退化成只剩一种()
        {
            var level = NewLevel(MetalWater, "金..\n...\n..水", requireAllPool: false);
            try
            {
                var text = string.Join(" / ", Collect(level));
                StringAssert.Contains("只剩一种元素", text);
                StringAssert.Contains("勾上", text);
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 勾了必须都在场_布局里缺的那种元素会被指出来()
        {
            // 布局里没有木，而且**牌池里也没有木牌**（牌池只有金水）→ 玩家造不出木来，是真死局。
            var pool = MetalWater | ElementMask.Wood;
            var level = NewLevel(pool, "金..\n...\n..水", requireAllPool: true, cardPool: MetalWater);
            try
            {
                var text = string.Join(" / ", Collect(level));
                StringAssert.Contains("木", text);
                StringAssert.Contains("永远赢不了", text);
                StringAssert.Contains("靠出牌也造不出来", text);
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 勾了必须都在场_能召唤出来的元素不再误报()
        {
            // 布局里没画木，但牌池里有木牌 → 玩家可以打一张木牌到空格上把它召唤出来（图1 第 2 条）。
            // 这正是 level_02 报的那条假问题：只看字符画就判「永远赢不了」。
            var pool = MetalWater | ElementMask.Wood;
            var level = NewLevel(pool, "金..\n...\n..水", requireAllPool: true);
            try
            {
                Assert.IsEmpty(Collect(level), string.Join(" / ", Collect(level)));
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 勾了必须都在场_相生能造出来的元素也不再误报()
        {
            // 关掉空格召唤，只剩相生这条路：木牌打水怪是「水生木」，木是赢家 → 场上会多出木。
            var pool = MetalWater | ElementMask.Wood;
            var level = NewLevel(pool, "金..\n...\n..水", requireAllPool: true,
                emptyCell: EmptyCellCardMode.Reject);
            try
            {
                Assert.IsEmpty(Collect(level), string.Join(" / ", Collect(level)));
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 勾了必须都在场_相生方向不对时牌面元素照样造不出来()
        {
            // 牌池 = 金 + 木，布局 = 三只水，关掉召唤：
            //   · 金牌打水怪是「金生水」—— 金是**输**的那一方，场上只会多出水，金永远出不来 → 该报
            //   · 木牌打水怪是「水生木」—— 木是赢家，木能出来 → 不该报
            var pool = MetalWater | ElementMask.Wood;
            var level = NewLevel(pool, "水..\n...\n..水", requireAllPool: true,
                cardPool: ElementMask.Metal | ElementMask.Wood, emptyCell: EmptyCellCardMode.Reject);
            try
            {
                var text = string.Join(" / ", Collect(level));
                StringAssert.Contains("金", text);
                StringAssert.Contains("永远赢不了", text);
                StringAssert.DoesNotContain("木", text, "木能靠相生（水生木）出来，不该被算成死局：" + text);
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 出牌能不能造出某种元素_召唤与相生两条路都要认得出来()
        {
            // 这条检查（「这种元素永远不会出现吗」）的底层判断，单拎出来钉一遍。
            var pool = MetalWater | ElementMask.Wood;
            var rules = new Rules { ElementPool = pool, CardPool = pool }; // 默认 EmptyCellCard = Summon
            var waterOnBoard = new[] { Element.Water };

            StringAssert.Contains("召唤", rules.CardSourceFor(Element.Wood, waterOnBoard));

            rules.EmptyCellCard = EmptyCellCardMode.Reject;
            StringAssert.Contains("相生", rules.CardSourceFor(Element.Wood, waterOnBoard),
                "木牌打水怪是水生木，木是赢家 → 木能出来");
            Assert.IsNull(rules.CardSourceFor(Element.Metal, waterOnBoard),
                "金生水里金是输的那一方，场上只会多出水，金出不来");
            Assert.IsNull(rules.CardSourceFor(Element.Wood, new Element[0]),
                "场上没有能相生它的元素，又关掉了召唤 → 造不出来");

            // 牌都发不出来（木牌不在牌池里）时两条路都不成立
            rules.CardPool = MetalWater;
            Assert.IsNull(rules.CardSourceFor(Element.Wood, waterOnBoard));

            // 元素牌权重为 0 = 一张元素牌都抽不到，同样造不出来
            rules.CardPool = pool;
            rules.ElementCardWeight = 0;
            Assert.IsNull(rules.CardSourceFor(Element.Wood, waterOnBoard));
        }

        [Test]
        public void 勾了必须都在场_布局齐全时不报问题()
        {
            var level = NewLevel(MetalWater, "金..\n...\n..水", requireAllPool: true);
            try
            {
                Assert.IsEmpty(Collect(level), string.Join(" / ", Collect(level)));
            }
            finally { Dispose(level); }
        }

        [Test]
        public void 随机铺盘的关卡会被提醒开局就相等()
        {
            // 随机铺盘给每种可用元素铺的数量一样多（CreepsPerElement 是"每种几个"），
            // 所以「每种都要有且数量相等」这个目标开局就基本达成了 —— 这关该用字符画摆个不相等的开局。
            var pool = MetalWater | ElementMask.Wood;
            var level = NewLevel(pool, "", requireAllPool: true);
            level.rules.CreepsPerElement = 2; // 让它真的会铺
            try
            {
                var text = string.Join(" / ", Collect(level));
                StringAssert.Contains("开局", text);
                StringAssert.Contains("不相等", text);

                // 但绝不能报成「缺元素永远赢不了」—— 随机铺盘每种都会铺上
                Assert.IsFalse(text.Contains("永远赢不了"), "不该误报成缺元素：" + text);
            }
            finally { Dispose(level); }
        }
    }

    /// <summary>
    /// 「新建关卡」菜单的测试。
    ///
    /// 这条用例是为一个真实 bug 补的：`AssetDatabase.CreateFolder` 的第二个参数是**文件夹名**而不是路径，
    /// 菜单里写成 `CreateFolder("Assets", "Assets/Levels")`，于是建出一个叫 `Assets_Levels` 的杂物目录，
    /// 而 `CreateAsset("Assets/Levels/Level_01.asset")` 因为目录不存在直接失败 ——
    /// 点菜单只会看到 Console 报错，什么都建不出来。这类「只在编辑器里跑得到」的代码最需要用例。
    ///
    /// ⚠ 它建在**隔离的临时目录**里，绝不碰真正的 `Assets/Levels`。
    ///    第一版是直接调 `CreateLevelAsset()` 往真目录里建的，结果一旦 TearDown 出问题
    ///    （先删了资产、再想去比对目录里的引用，那时两边都已经是「已销毁」状态），
    ///    就在真实目录里留下了一条指向已删资产的悬空引用。测试不该有能力弄脏真资产。
    /// </summary>
    public class LevelCreationTests
    {
        const string TestDir = "Assets/__TestLevels";
        const string TestCatalogPath = TestDir + "/TestCatalog.asset";
        const string RealCatalogPath = "Assets/Levels/LevelCatalog.asset";

        int _realCatalogCountBefore;

        [SetUp]
        public void SetUp()
        {
            // 记住真实目录的状态，用例结束后必须一模一样（不需要清理，只要没被碰过）
            var real = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            _realCatalogCountBefore = real != null && real.levels != null ? real.levels.Length : -1;
            AssetDatabase.DeleteAsset(TestDir); // 上一次跑残留的
        }

        [TearDown]
        public void TearDown()
        {
            // 整个临时目录一起删，包含它自己的目录资产 —— 一条命令，没有「先删谁」的顺序问题
            AssetDatabase.DeleteAsset(TestDir);
            AssetDatabase.Refresh();

            Assert.IsFalse(AssetDatabase.IsValidFolder(TestDir), "临时目录应该被删干净");

            var real = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            int now = real != null && real.levels != null ? real.levels.Length : -1;
            Assert.AreEqual(_realCatalogCountBefore, now,
                "真实的 LevelCatalog 一关都不能多、一关都不能少 —— 测试绝不许写进去");
        }

        [Test]
        public void 新建关卡_生成定义与预制体并加入目录()
        {
            var created = SceneScaffold.CreateLevelAsset(TestDir, TestCatalogPath, wireIntoScene: false);
            Assert.IsNotNull(created, "菜单应该能建出关卡来");

            var soPath = AssetDatabase.GetAssetPath(created);
            var prefabPath = created.levelPrefab != null ? AssetDatabase.GetAssetPath(created.levelPrefab) : null;

            // 文件必须真的落在指定目录下（这条就是那个 CreateFolder bug 的直接杀手）
            StringAssert.StartsWith(TestDir + "/Level_", soPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<LevelDefinition>(soPath),
                "关卡定义必须能在它自己的路径上被加载 —— 建到别处或者没建成功都会在这里红");
            Assert.IsNotNull(created.levelPrefab, "关卡预制体必须建出来并挂上");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(TestCatalogPath);
            Assert.IsNotNull(catalog, "目录应该被顺手建出来");
            CollectionAssert.Contains(catalog.levels, created);

            // 新建出来的关卡应该是「零问题」的起点，设计师可以直接开始填
            var issues = Collect(created);
            Assert.IsEmpty(issues, string.Join(" / ", issues));
        }

        [Test]
        public void 新建关卡_第二次会挑一个没被占用的编号()
        {
            var first = SceneScaffold.CreateLevelAsset(TestDir, TestCatalogPath, wireIntoScene: false);
            var second = SceneScaffold.CreateLevelAsset(TestDir, TestCatalogPath, wireIntoScene: false);

            Assert.AreNotEqual(AssetDatabase.GetAssetPath(first), AssetDatabase.GetAssetPath(second),
                "两关不能复用同一个文件名");
            Assert.AreNotEqual(first.levelId, second.levelId, "levelId 也不能重复（进度存档按它走）");

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(TestCatalogPath);
            Assert.AreEqual(2, catalog.levels.Length, "两关都要进目录");
            Assert.IsEmpty(Collect(second), string.Join(" / ", Collect(second)));
        }

        static List<string> Collect(LevelDefinition level)
        {
            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);
            return issues;
        }
    }
}
