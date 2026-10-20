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
    /// 第三章的设计见 <c>ChapterCatalog.asset</c> 第三章那 5 条（关卡名/目标就是设计稿原文）：
    ///   1. 只剩火（教「元素消失 → 瓶子涨一格，满 5 格给一张牌」）
    ///   2. 火瓶产 5 张（容量 5 → 要让 25 个火消失）
    ///   3. 金瓶 10 张 + 木瓶 10 张（教「瓶子里出来的牌就是下一环的弹药」）
    ///   4. 水瓶 7 + 土瓶 6 + 火瓶 5（相克链往下走一环）
    ///   5. 五种各 5 张（五行相克是一个环）
    ///
    /// ⚠ 出牌预算是硬的：一局最多打「回合上限 × 手牌上限」张牌（8 × 5 = 40），
    ///   而「产 N 张牌」= 该元素消失 **瓶容量 × N** 次。所以：
    ///   · ch3_01 / ch3_02 用**容量 5**（张数也是 5 → 25 次消失，攒的过程看得见）；
    ///   · ch3_03 / 04 / 05 的张数大（10 / 7+6+5 / 各 5），容量 5 会变成 100 次以上的消失，
    ///     打不完，所以用**容量 1**（消失 N 次就给 N 张）。
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
            public string GoalDescription => Text;
            public void OnLevelStart(GameEngine engine) { }
            public void OnRoundStart(GameEngine engine) { }
            public void OnEncounter(GameEngine engine, EncounterInfo info) { }
            public bool TryExtraWin(GameEngine engine, out string message) { message = null; return false; }
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

        // ---------------------------------------------------------------- 真实资产

        [Test]
        public void 第三章五关都接在真实关卡目录里()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            Assert.IsNotNull(catalog, "找不到 " + RealCatalogPath);
            Assert.AreEqual(20, catalog.levels.Length, "第一~四章共 20 关");

            var ids = catalog.levels.Select(l => l.levelId).ToList();
            Assert.AreEqual(15, ids.Count, "目录里有空槽位");
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
                ("ch3_02", 5, 8, new[] { (Element.Fire, 5) }),
                ("ch3_03", 1, 8, new[] { (Element.Metal, 10), (Element.Wood, 10) }),
                ("ch3_04", 1, 8, new[] { (Element.Water, 7), (Element.Earth, 6), (Element.Fire, 5) }),
                ("ch3_05", 1, 8, new[]
                {
                    (Element.Metal, 5), (Element.Wood, 5), (Element.Water, 5), (Element.Fire, 5), (Element.Earth, 5),
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
