using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Levels;
using TapGJ.Levels.Mechanics;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 第三章「小瓶子教学」：目标组件 + 5 关真实资产。
    ///
    /// 第三章的设计见 <c>ChapterCatalog.asset</c> 第三章那 5 条：
    ///   1. 把水清光（教「元素消失 → 瓶子涨一格，满 5 格给一张牌」）
    ///   2. 火瓶 1 张（容量 5 → 让 5 个火消失）
    ///   3. 金瓶 1 张 + 木瓶 1 张（教「瓶子里出来的牌就是下一环的弹药」）
    ///   4. 水瓶 1 + 土瓶 1 + 火瓶 1（相克链往下走一环）
    ///   5. 五种各 1 张（五行相克是一个环）
    ///
    /// 从 ch3_02 起还**沿用第二章的复合元素/秘术牌**（叠加而不是替换，只有 ch3_01 保持纯教学）：
    /// 秘术牌权重压到元素牌的 1/10，手牌上限 +1 吸收稀释，回合上限与目标张数一律不动。
    ///
    /// ⚠ 出牌预算是硬的：一局最多打「回合上限 × 手牌上限」张牌（12 × 6 = 72）。
    ///   而**牌池是全部五种元素**（除 ch1_01/ch1_02/ch3_01 三个纯教学关），所以某种元素牌
    ///   一局大概只抽得到「出牌数 ÷ 5」≈ 14 张 —— 目标压到「各 1 张」（= 各消失 5 次）
    ///   就是为了留足余量；秘术牌进牌堆后还要再打八折。
    ///   ch3_04 / ch3_05 能吃下更大的张数，是因为它们的链条**自己供牌**
    ///   （杀掉 X 就拿到 X 牌，X 牌正好是杀下一种元素的弹药）。
    /// </summary>
    public class Chapter3Tests
    {
        const string RealCatalogPath = "Assets/Levels/LevelCatalog.asset";
        const string RealChapterPath = "Assets/UI/Data/ChapterCatalog.asset";

        static Rules MakeRules(int capacity) => new Rules
        {
            BoardWidth = 6,
            BoardHeight = 6,
            CreepsPerElement = 0,
            InitialHandSize = 0,
            HandLimit = 5,
            RoundLimit = 10,
            BottleMode = TapGJ.Core.BottleMode.Enabled,
            BottleCapacity = capacity,
        };

        static GameEngine NewEngine(int capacity)
        {
            var engine = new GameEngine(MakeRules(capacity), 7);
            engine.Start();
            engine.EndPreparationNow();
            return engine;
        }

        // ---------------------------------------------------------------- 目标口径

        [Test]
        public void 瓶产出目标_一张都没产不算赢_产够才算()
        {
            var engine = NewEngine(capacity: 2);
            Assert.AreEqual(0, engine.Bottle.CardsGranted(Element.Metal));

            Assert.IsFalse(BottleProducedRule.IsSatisfied(engine, Element.Metal, 1, out _),
                "还没产出过任何一张，不算赢");

            // 容量 2 → 金消失 2 个就给 1 张金牌
            Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Metal));
            Assert.AreEqual(0, engine.Bottle.CardsGranted(Element.Metal), "才 1 个，瓶子还没满");
            Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Metal));

            Assert.AreEqual(1, engine.Bottle.CardsGranted(Element.Metal));
            Assert.IsTrue(BottleProducedRule.IsSatisfied(engine, Element.Metal, 1, out var message));
            StringAssert.Contains("金", message);
            Assert.IsFalse(BottleProducedRule.IsSatisfied(engine, Element.Metal, 2, out _),
                "要 2 张的话还差一张");
            Assert.IsFalse(BottleProducedRule.IsSatisfied(engine, Element.Wood, 1, out _),
                "木瓶一张都没产，不能拿金瓶的账来抵");
        }

        [Test]
        public void 瓶产出目标_看的是累计产出_不是瓶里现在攒了几个()
        {
            var engine = NewEngine(capacity: 1);   // 容量 1 → 每消失一个就产一张
            engine.TryPlaceAndRemoveForTest(Element.Fire);
            engine.TryPlaceAndRemoveForTest(Element.Fire);

            Assert.AreEqual(2, engine.Bottle.CardsGranted(Element.Fire));
            Assert.AreEqual(0, engine.Bottle.Count(Element.Fire), "瓶里已经清空了");
            Assert.IsTrue(BottleProducedRule.IsSatisfied(engine, Element.Fire, 2, out _),
                "瓶里空着但账上已经产出 2 张 —— 目标是累计产出，不是当前存量");
        }

        // ---------------------------------------------------------------- 多目标进度条

        /// <summary>只用来喂进度条的假机制（真机制是 MonoBehaviour，字段是私有的，测试里造不出来）。</summary>
        sealed class StubRule : ILevelRule
        {
            public string Label;
            public float Fill;
            public string Text = "假目标";
            public bool Win;
            public string WinMessage = "胜利：假目标达成";
            public string GoalDescription => Text;
            public void OnLevelStart(GameEngine engine) { }
            public void OnRoundStart(GameEngine engine) { }
            public void OnEncounter(GameEngine engine, EncounterInfo info) { }
            public bool TryExtraWin(GameEngine engine, out string message) { message = WinMessage; return Win; }
            public bool TryExtraLose(GameEngine engine, out string message) { message = null; return false; }
            public GoalProgress GetGoalProgress(GameEngine engine)
                => new GoalProgress { Description = GoalDescription, Fill = Fill, Label = Label };
        }

        [Test]
        public void 多个目标时进度条把每一条都写出来_且语义下填最差的那条()
        {
            var rules = new Rules
            {
                BoardWidth = 4, BoardHeight = 4,
                CreepsPerElement = 0, InitialHandSize = 0, HandLimit = 5, RoundLimit = 10,
                Goal = GoalSpec.Custom(),
            };
            var engine = new GameEngine(rules, 1);
            engine.Start();
            engine.AttachRule(new StubRule { Label = "金牌 3 / 10", Fill = 0.3f, Text = "让金瓶产出 10 张" });
            engine.AttachRule(new StubRule { Label = "木牌 9 / 10", Fill = 0.9f, Text = "让木瓶产出 10 张" });

            // 或：哪条先达成算哪条 → 进度条取最好的那条
            var or = engine.GetGoalProgress();
            Assert.AreEqual("金牌 3 / 10　木牌 9 / 10", or.Label,
                "两条目标都要写出来（只报第一条的话，玩家永远看不到第二条差多少）");
            Assert.AreEqual(0.9f, or.Fill, 0.001f, "「或」取最好的那条");
            StringAssert.Contains("让木瓶产出 10 张", or.Description,
                "**标题**也要把所有目标连起来 —— 之前只写第一条，玩家以为只要做完那一件事");
            StringAssert.Contains("让金瓶产出 10 张", or.Description);

            // 且：必须全部达成 → 进度条取最差的那条，满了才等于要赢
            engine.Rules.RequireAllWinRules = true;
            Assert.AreEqual(0.3f, engine.GetGoalProgress().Fill, 0.001f, "「且」取最差的那条");
            StringAssert.Contains("　且　", engine.GetGoalProgress().Description, "「且」语义下用「且」连接");
        }

        // ---------------------------------------------------------------- 多目标通关提示

        [Test]
        public void 多目标的通关提示要把每个条件都说出来()
        {
            var rules = new Rules
            {
                BoardWidth = 4, BoardHeight = 4,
                CreepsPerElement = 0, InitialHandSize = 0, HandLimit = 5, RoundLimit = 10,
                Goal = GoalSpec.Custom(),
                RequireAllWinRules = true,
            };
            var engine = new GameEngine(rules, 1);
            engine.Start();
            engine.AttachRule(new StubRule
            {
                Text = "金瓶 1 张", Win = true, WinMessage = "胜利：金瓶子已产出 1 张牌（≥ 1）",
            });
            engine.AttachRule(new StubRule
            {
                Text = "木瓶 1 张", Win = true, WinMessage = "胜利：木瓶子已产出 1 张牌（≥ 1）",
            });

            Assert.IsTrue(engine.CheckWinCondition(out var reason));
            StringAssert.Contains("金", reason);
            StringAssert.Contains("木", reason, "「且」语义的通关提示不能只报第一条：" + reason);
            Assert.AreEqual(1, reason.Split(new[] { "胜利：" }, System.StringSplitOptions.None).Length - 1,
                "拼起来之后只该有一个「胜利：」前缀：" + reason);
        }

        [Test]
        public void 第三章第三关的通关提示_金瓶和木瓶都要出现()
        {
            // 这一关是「金瓶 1 张 且 木瓶 1 张」：当时通关只弹出第一条（金瓶），
            // 玩家刚打完木瓶却看到金瓶的文案，以为提示错了。
            var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Levels/ch3_03.asset");
            Assert.IsNotNull(level, "找不到 ch3_03");
            Assert.IsNotNull(level.levelPrefab, "ch3_03 必须有关卡预制体");

            var rules = level.rules.Clone();
            rules.InitialComposites = level.initialComposites ?? new CompositePlacement[0];

            var engine = new GameEngine(rules, level.fixedSeed);
            foreach (var rule in level.levelPrefab.GetComponentsInChildren<ILevelRule>(true))
                if (!(rule is MonoBehaviour mb) || mb.enabled) engine.AttachRule(rule);
            engine.Start();

            // 容量 5：各让 5 个消失 → 各产出 1 张牌
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Metal), "金瓶还差第 " + (i + 1) + " 个金");
            for (int i = 0; i < 5; i++)
                Assert.IsTrue(engine.TryPlaceAndRemoveForTest(Element.Wood), "木瓶还差第 " + (i + 1) + " 个木");

            Assert.IsTrue(engine.CheckWinCondition(out var reason), "两个瓶子都产出了 1 张，应该已经赢了");
            StringAssert.Contains("金", reason);
            StringAssert.Contains("木", reason, "通关提示把木瓶那条吞了：" + reason);
        }

        // ---------------------------------------------------------------- 真实资产

        [Test]
        public void 第三章五关都接在真实关卡目录里()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            Assert.IsNotNull(catalog, "找不到 " + RealCatalogPath);
            Assert.AreEqual(20, catalog.levels.Length, "第一~四章共 20 关");

            var ids = catalog.levels.Select(l => l.levelId).ToList();
            Assert.AreEqual(20, ids.Count, "目录里有空槽位（20 个槽位都该绑着关卡）");
            CollectionAssert.AreEqual(
                new[] { "ch3_01", "ch3_02", "ch3_03", "ch3_04", "ch3_05" },
                ids.GetRange(10, 5), "第三章 5 关要按顺序排在目录最后");
            CollectionAssert.AllItemsAreUnique(ids, "levelId 不能重复（进度存档按它走）");
        }

        [Test]
        public void 第三章五关都是零问题配置()
        {
            foreach (var id in new[] { "ch3_01", "ch3_02", "ch3_03", "ch3_04", "ch3_05" })
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Levels/" + id + ".asset");
                Assert.IsNotNull(level, "找不到关卡资产 " + id);

                var issues = new List<string>();
                level.CollectAuthoringIssues(issues);
                Assert.IsEmpty(issues, id + " 有配置问题：" + string.Join(" / ", issues));
            }
        }

        [Test]
        public void 第三章的教学节奏_瓶子开着_目标组件上的数字钉死()
        {
            // (关卡, 瓶容量, 回合上限, 目标组件上的 (元素, 张数) —— 顺序 = 预制体上的挂载顺序)
            var expectations = new (string id, int capacity, int roundLimit, (Element el, int count)[] goals)[]
            {
                ("ch3_01", 5, 6, new (Element, int)[0]),   // 内置目标「水 = 0」，没有瓶产出组件
                ("ch3_02", 5, 12, new[] { (Element.Fire, 1) }),
                ("ch3_03", 5, 12, new[] { (Element.Metal, 1), (Element.Wood, 1) }),
                ("ch3_04", 5, 12, new[] { (Element.Water, 1), (Element.Earth, 1), (Element.Fire, 1) }),
                ("ch3_05", 5, 12, new[]
                {
                    (Element.Metal, 1), (Element.Wood, 1), (Element.Water, 1), (Element.Fire, 1), (Element.Earth, 1),
                }),
            };

            foreach (var (id, capacity, roundLimit, goals) in expectations)
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Levels/" + id + ".asset");
                Assert.IsNotNull(level, "找不到关卡资产 " + id);

                Assert.IsTrue(level.rules.BottlesEnabled, id + " 要开着元素瓶（这一章就是教它）");
                Assert.AreEqual(capacity, level.rules.BottleCapacity, id + " 的瓶容量");
                Assert.AreEqual(roundLimit, level.rules.RoundLimit, id + " 的回合上限");
                Assert.IsFalse(level.useEvents, id + " 是教学章，不带事件");
                Assert.AreEqual(level.rules.RequireAllWinRules, goals.Length > 1, id + " 多个目标必须用「且」");

                // ch3_01 是唯一用**内置目标**的（元素 ≤ N）：它要教「杀水喂瓶」，
                // 用「只剩火」的话，召唤出来的土 / 「火生土」都会把一个清不掉的土留在场上 → 一键锁死。
                if (id == "ch3_01")
                {
                    Assert.AreEqual(GoalKind.ElementAtMost, level.rules.Goal.Kind, "ch3_01 用内置目标");
                    Assert.AreEqual(Element.Water, level.rules.Goal.Target, "盯的是水");
                    Assert.AreEqual(0, level.rules.Goal.TargetCount, "水要清到 0");
                    Assert.IsNull(level.levelPrefab, "ch3_01 不需要关卡预制体（没有机制组件）");
                    continue;
                }

                Assert.AreEqual(GoalKind.Custom, level.rules.Goal.Kind, id + " 的目标要自定义");
                Assert.IsNotNull(level.levelPrefab, id + " 必须有关卡预制体");

                var ruleComps = level.levelPrefab.GetComponentsInChildren<BottleProducedRule>(true);
                Assert.AreEqual(goals.Length, ruleComps.Length, id + " 的瓶产出组件个数");

                for (int i = 0; i < goals.Length; i++)
                {
                    Assert.AreEqual(goals[i].el, ruleComps[i].TargetElement, id + " 第 " + (i + 1) + " 个目标看哪个瓶子");
                    Assert.AreEqual(goals[i].count, ruleComps[i].RequiredCount, id + " 第 " + (i + 1) + " 个目标的张数");
                }
            }
        }

        // ---------------------------------------------------------------- 机制叠加

        [Test]
        public void 第三章从第二关起沿用第二章的秘术牌_第一关仍只教瓶子()
        {
            // 叠加规则（第四章同理）：新章节只做加法 —— 第三章 = 小瓶子（本章）+ 复合元素/秘术牌（第二章）。
            // 唯一的例外是 ch3_01：它的教学前提是「牌池只有土牌」，塞进秘术牌就讲不成了。
            foreach (var id in new[] { "ch3_01", "ch3_02", "ch3_03", "ch3_04", "ch3_05" })
            {
                var level = AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Levels/" + id + ".asset");
                Assert.IsNotNull(level, "找不到关卡资产 " + id);

                if (id == "ch3_01")
                {
                    Assert.IsFalse(level.rules.UseCompositeElements,
                        "ch3_01 的起手必须还是 5 张土牌 → 不能开秘术牌");
                    Assert.AreEqual(SpecialCardMask.None, level.rules.SpecialCardPool);
                    continue;
                }

                Assert.IsTrue(level.rules.UseCompositeElements, id + " 要沿用第二章的复合元素/秘术牌");
                Assert.IsTrue(level.rules.HasSpecialCard(CardKind.FusionCard), id + " 要有融合牌");
                Assert.IsTrue(level.rules.HasSpecialCard(CardKind.DecomposeCard), id + " 要有分解牌");
                Assert.Greater(level.rules.FusionCardWeight, 0, id + " 融合牌权重不能是 0（等于没开）");
                Assert.Greater(level.rules.DecomposeCardWeight, 0, id + " 分解牌权重不能是 0（等于没开）");

                // 这一章的主角是瓶子：秘术牌只能当点缀，权重压到元素牌的 1/10。
                Assert.Less(level.rules.FusionCardWeight, level.rules.ElementCardWeight,
                    id + " 的秘术牌权重必须低于元素牌，否则瓶子的弹药会被稀释");
                Assert.Less(level.rules.DecomposeCardWeight, level.rules.ElementCardWeight);

                Assert.AreEqual(6, level.rules.HandLimit,
                    id + " 牌堆里多了秘术牌 → 手牌上限 +1（否则元素牌会被秘术牌挤出牌堆）");
                Assert.AreEqual(12, level.rules.RoundLimit, id + " 回合上限保持 12 —— 叠加只动权重与手牌");
            }
        }

        [Test]
        public void 第三章在章节页里都绑上了关卡()
        {
            var chapters = AssetDatabase.LoadAssetAtPath<ChapterCatalog>(RealChapterPath);
            Assert.IsNotNull(chapters, "找不到 " + RealChapterPath);
            Assert.IsTrue(chapters.chapters.Length >= 3, "至少要有三章");

            var third = chapters.chapters[2];
            Assert.AreEqual(5, third.levels.Length, "第三章是 5 关");
            foreach (var entry in third.levels)
            {
                Assert.IsNotNull(entry.level, "第三章的「" + entry.title + "」还没绑关卡定义");
                StringAssert.StartsWith("ch3_", entry.level.levelId);
            }
        }
    }
}
