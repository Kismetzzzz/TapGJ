using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 关卡系统（P2）测试：字符画解析、胜利目标、墙格、固定布置、进度存档、目录、跨场景上下文。
    /// </summary>
    public class LevelSystemTests
    {
        const string TestLevelId = "test_level_x"; // 用不撞别的关卡的 id，且 TearDown 里清掉

        [TearDown]
        public void TearDown()
        {
            ProgressStore.Reset(TestLevelId);
        }

        // ---------------------------------------------------------------- 字符画解析

        [Test]
        public void 字符画_正常解析_元素与墙都摆对()
        {
            // 文本第 1 行 = 棋盘最上面（y 最大）
            var text = "金.水\n#..\n木.火";
            Assert.IsTrue(BoardLayoutParser.TryParse(text, 3, 3, out var setup, out var error), error);

            Assert.AreEqual(Element.Metal, setup.Cells[2 * 3 + 0].Value, "(0,2) 应该是金");
            Assert.AreEqual(Element.Water, setup.Cells[2 * 3 + 2].Value, "(2,2) 应该是水");
            Assert.AreEqual(Element.Wood, setup.Cells[0 * 3 + 0].Value, "(0,0) 应该是木");
            Assert.AreEqual(Element.Fire, setup.Cells[0 * 3 + 2].Value, "(2,0) 应该是火");
            Assert.IsTrue(setup.Blocked[1 * 3 + 0], "(0,1) 应该是墙");
            Assert.IsNull(setup.Cells[1 * 3 + 1], "(1,1) 应该是空");
        }

        [Test]
        public void 字符画_非法输入被拒绝_并给出行列()
        {
            Assert.IsFalse(BoardLayoutParser.TryParse("金.水\n#..", 3, 3, out _, out var tooFew),
                "行数不够应该失败");
            StringAssert.Contains("行数", tooFew);

            Assert.IsFalse(BoardLayoutParser.TryParse("金水\n##\n木火", 3, 3, out _, out var tooShort),
                "某行太短应该失败");
            StringAssert.Contains("长度", tooShort);
            StringAssert.Contains("第 1 行", tooShort);

            Assert.IsFalse(BoardLayoutParser.TryParse("金X水\n#..\n木.火", 3, 3, out _, out var badChar),
                "未知字符应该失败");
            StringAssert.Contains("X", badChar);
            StringAssert.Contains("第 1 行第 2 列", badChar);

            Assert.IsFalse(BoardLayoutParser.TryParse("###\n###\n###", 3, 3, out _, out var allWalls),
                "全是墙应该失败");
        }

        // ---------------------------------------------------------------- 胜利目标

        [Test]
        public void 目标_六种都能判定()
        {
            // WaterOnly：全是水才算赢，空场不算
            Assert.IsTrue(Rig(new Rules { Goal = GoalSpec.WaterOnly() }, (Element.Water, new Pos(0, 0)))
                .CheckWinCondition(out _), "只有水应该赢");
            Assert.IsFalse(Rig(new Rules { Goal = GoalSpec.WaterOnly() }, (Element.Water, new Pos(0, 0)), (Element.Fire, new Pos(1, 0)))
                .CheckWinCondition(out _), "有水有火不算只剩水");
            Assert.IsFalse(Rig(new Rules { Goal = GoalSpec.WaterOnly() }).CheckWinCondition(out _), "空场对只剩水不算赢");

            // SingleElement / ClearBoard：空场都算
            Assert.IsTrue(Rig(new Rules { Goal = GoalSpec.ClearBoard() }).CheckWinCondition(out _), "空场应该清空胜利");

            // SurviveRounds：Rig 里 BeginRound 已经把 Round 设为 1，所以 Survive(1) 应该立刻达成
            var survive = Rig(new Rules { Goal = GoalSpec.Survive(1) });
            Assert.IsTrue(survive.CheckWinCondition(out _), "Round=1 已经 ≥ 1");

            // ElementAtMost / ElementAtLeast
            Assert.IsTrue(Rig(new Rules { Goal = GoalSpec.ElementAtMost(Element.Fire, 1) },
                (Element.Fire, new Pos(0, 0)), (Element.Water, new Pos(1, 0))).CheckWinCondition(out _), "火 1 只 ≤ 1");
            Assert.IsFalse(Rig(new Rules { Goal = GoalSpec.ElementAtLeast(Element.Fire, 2) },
                (Element.Fire, new Pos(0, 0))).CheckWinCondition(out _), "火只有 1 只 < 2");
        }

        [Test]
        public void 目标_进度条文字按种类变化()
        {
            var engine = Rig(new Rules { Goal = GoalSpec.Survive(10) }, (Element.Water, new Pos(0, 0)));
            var p = engine.GetGoalProgress();
            Assert.AreEqual("撑过 10 回合", p.Description);
            Assert.IsTrue(p.Label.Contains("回合"), "进度条文字该提到回合：" + p.Label);

            var atLeast = Rig(new Rules { Goal = GoalSpec.ElementAtLeast(Element.Metal, 3) },
                (Element.Metal, new Pos(0, 0)));
            Assert.IsTrue(atLeast.GetGoalProgress().Label.Contains("金"), "进度条该提到目标元素");
        }

        static GameEngine Rig(Rules rules, params (Element, Pos)[] pieces)
        {
            var engine = new GameEngine(rules, 1);
            engine.ClearBoard();
            foreach (var (e, p) in pieces) engine.PlaceCreep(e, p);
            engine.BeginRound();
            return engine;
        }

        // ---------------------------------------------------------------- 墙格

        [Test]
        public void 墙_小怪撞墙就停下_不会穿过去()
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 3 }, 1);
            engine.ClearBoard();
            engine.Board.SetBlocked(new Pos(2, 0), true);
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.BeginRound();
            engine.SetNextRandomValues(3, 1); // 走 3 格，方向 +X：走一格到 (1,0)，下一格 (2,0) 是墙

            var step = engine.AdvancePreparation();

            Assert.IsNotNull(step);
            Assert.AreEqual(new Pos(1, 0), step.To, "撞墙后停在墙前");
            Assert.IsNull(engine.Board.At(new Pos(2, 0)), "墙里不能有元素");
            Assert.AreEqual(1, engine.CreepCount);
            Assert.IsTrue(engine.Board.IsBlocked(new Pos(2, 0)));
        }

        [Test]
        public void 墙_出生格搜索跳过墙()
        {
            var board = new Board(3, 3);
            // 中心 (1,1) 的 8 邻格只留 (2,2) 可生，其余全堵
            var blocked = new[]
            {
                new Pos(0, 0), new Pos(1, 0), new Pos(2, 0),
                new Pos(0, 1), new Pos(2, 1),
                new Pos(0, 2), new Pos(1, 2),
            };
            foreach (var p in blocked) board.SetBlocked(p, true);

            var cell = board.FindSpawnCell(new Pos(1, 1), new System.Random(1), Pos.None, 1);
            Assert.AreEqual(new Pos(2, 2), cell, "唯一没堵的邻格是 (2,2)");
        }

        // ---------------------------------------------------------------- 固定布置

        [Test]
        public void 固定布置_按LevelSetup铺盘并发固定起手牌()
        {
            var text = "金..\n...\n...";
            Assert.IsTrue(BoardLayoutParser.TryParse(text, 3, 3, out var setup, out var error), error);
            setup.InitialHand = new[] { Element.Water, Element.Fire };

            var rules = new Rules { BoardWidth = 3, BoardHeight = 3, CreepsPerElement = 0, InitialHandSize = 5 };
            var engine = new GameEngine(rules, 7);
            engine.Start(setup);

            Assert.AreEqual(1, engine.CreepCount, "只有字符画里那一只金");
            Assert.AreEqual(Element.Metal, engine.Board.At(new Pos(0, 2)).Element, "金在 (0,2)");
            Assert.AreEqual(2, engine.Hand.Count, "起手牌用固定的两张，而不是随机 5 张");
            Assert.AreEqual(Element.Water, engine.Hand[0].Element);
            Assert.AreEqual(Element.Fire, engine.Hand[1].Element);
        }

        // ---------------------------------------------------------------- 进度 / 目录 / 上下文

        [Test]
        public void 进度_读写与重置()
        {
            Assert.IsFalse(ProgressStore.IsCleared(TestLevelId));

            ProgressStore.MarkCleared(TestLevelId, 12);
            Assert.IsTrue(ProgressStore.IsCleared(TestLevelId));
            Assert.AreEqual(12, ProgressStore.BestRounds(TestLevelId));

            ProgressStore.MarkCleared(TestLevelId, 8); // 更好成绩
            Assert.AreEqual(8, ProgressStore.BestRounds(TestLevelId));

            ProgressStore.MarkCleared(TestLevelId, 20); // 更差，不该覆盖最好成绩
            Assert.AreEqual(8, ProgressStore.BestRounds(TestLevelId));

            ProgressStore.Reset(TestLevelId);
            Assert.IsFalse(ProgressStore.IsCleared(TestLevelId));
        }

        [Test]
        public void 目录_按id查找与按索引取关()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var a = ScriptableObject.CreateInstance<LevelDefinition>();
            a.levelId = "aaa";
            var b = ScriptableObject.CreateInstance<LevelDefinition>();
            b.levelId = "bbb";
            catalog.levels = new[] { a, b };

            Assert.AreEqual(0, catalog.IndexOf("aaa"));
            Assert.AreEqual(1, catalog.IndexOf("bbb"));
            Assert.AreEqual(-1, catalog.IndexOf("zzz"));
            Assert.AreSame(b, catalog.GetByIndex(1));
            Assert.IsNull(catalog.GetByIndex(99));

            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
        }

        // ---------------------------------------------------------------- 下一关（HUD 的按钮）

        [Test]
        public void 目录_下一关按顺序走_最后一关不再返回第一关()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var a = MakeLevel("aaa");
            var b = MakeLevel("bbb");
            var c = MakeLevel("ccc");
            catalog.levels = new[] { a, b, c };

            try
            {
                Assert.AreSame(b, catalog.NextAfter("aaa"));
                Assert.AreSame(c, catalog.NextAfter("bbb"));
                Assert.IsNull(catalog.NextAfter("ccc"), "通关最后一关后不能把重新开始当成下一关");

                // 找不到当前关（在玩一个不在目录里的调试关 / id 为空）→ 回到目录第一关
                Assert.AreSame(a, catalog.NextAfter("zzz"));
                Assert.AreSame(a, catalog.NextAfter(null));
                Assert.AreSame(a, catalog.NextAfter(""));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(c);
            }
        }

        [Test]
        public void 目录_下一关会跳过None洞()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var a = MakeLevel("aaa");
            var b = MakeLevel("bbb");
            catalog.levels = new[] { a, null, b, null };

            try
            {
                Assert.AreSame(b, catalog.NextAfter("aaa"), "当前关后面那个洞要跳过（关卡被删了但目录没清）");
                Assert.IsNull(catalog.NextAfter("bbb"), "末尾没有可用关卡时应结束，不能绕回第一关");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void 目录_没有别的关卡时没有下一关()
        {
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            var only = MakeLevel("only");

            try
            {
                catalog.levels = null;
                Assert.IsNull(catalog.NextAfter("only"), "levels 是 null");

                catalog.levels = new LevelDefinition[0];
                Assert.IsNull(catalog.NextAfter("only"), "空目录");

                catalog.levels = new LevelDefinition[] { null, null };
                Assert.IsNull(catalog.NextAfter("only"), "目录里全是 None 洞");

                catalog.levels = new[] { only };
                Assert.IsNull(catalog.NextAfter("only"), "目录里只有当前这一关 → 没有「下一关」");
                Assert.AreSame(only, catalog.NextAfter("other"),
                    "但当前关不在目录里时，这个唯一的关卡就是它的「下一关」");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(only);
            }
        }

        [Test]
        public void 主控与HUD_下一关的转发与按钮文字()
        {
            var go = new GameObject("session-next-level");
            try
            {
                var session = go.AddComponent<GameSession>();

                // 开发兜底路径：没有关卡加载器 → 没有下一关，按钮要说明原因
                Assert.IsFalse(session.HasNextLevel);
                Assert.IsNull(session.NextLevelName);
                Assert.IsNull(session.CurrentLevelName);
                Assert.IsFalse(session.GoToNextLevel(), "没有关卡加载器时切关要返回 false，不能崩");
                StringAssert.Contains("没有下一关", Hud.NextLevelButtonLabel(session));

                var host = new FakeLevelHost
                {
                    CurrentLevelName = "第 1 关",
                    HasNextLevel = true,
                    NextLevelName = "第 2 关",
                };
                session.SetLevelHost(host);

                Assert.IsTrue(session.HasNextLevel);
                Assert.AreEqual("第 1 关", session.CurrentLevelName);
                Assert.AreEqual("第 2 关", session.NextLevelName);
                Assert.AreEqual("下一关 → 第 2 关", Hud.NextLevelButtonLabel(session),
                    "按钮上要写清楚要去哪一关");

                Assert.IsTrue(session.GoToNextLevel());
                Assert.AreEqual(1, host.GoToCalls, "切关必须转给关卡加载器（换关要重建整关）");
                Assert.AreEqual(0, host.Rebuilds, "转发的是 GoToNextLevel，不是让 HUD 自己 Rebuild");

                // 目录里没有别的关卡了 → 按钮置灰并说明原因
                host.HasNextLevel = false;
                host.NextLevelName = null;
                Assert.IsFalse(session.GoToNextLevel());
                StringAssert.Contains("没有下一关", Hud.NextLevelButtonLabel(session));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static LevelDefinition MakeLevel(string id)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.levelId = id;
            return level;
        }

        /// <summary>
        /// 只回答「有没有下一关 / 它叫什么」的关卡宿主替身。
        /// 真换关要重建关卡预制体 + 引擎（那是 LevelLoader 的活，需要整套场景）。
        /// </summary>
        sealed class FakeLevelHost : ILevelHost
        {
            public string CurrentLevelName { get; set; }
            public bool HasNextLevel { get; set; }
            public string NextLevelName { get; set; }

            public int Rebuilds;
            public int GoToCalls;

            public void RebuildLevel(int seed) => Rebuilds++;

            public bool GoToNextLevel()
            {
                GoToCalls++;
                return HasNextLevel;
            }
        }

        [Test]
        public void 启动上下文_设置与清空()
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();

            GameLaunchContext.Set(level, 42);
            Assert.IsTrue(GameLaunchContext.HasLevel);
            Assert.AreSame(level, GameLaunchContext.Level);
            Assert.AreEqual(42, GameLaunchContext.Seed);

            GameLaunchContext.Clear();
            Assert.IsFalse(GameLaunchContext.HasLevel);
            Assert.IsNull(GameLaunchContext.Level);

            Object.DestroyImmediate(level);
        }

        [Test]
        public void 关卡加载器_缺引用时逐条报出()
        {
            var issues = new List<WiringIssue>();
            LevelLoader.CheckWiring(null, null, issues);

            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.Exists(i => i.Target == "LevelLoader" && i.Field == "session" && i.IsError));
            Assert.IsTrue(issues.Exists(i => i.Target == "LevelLoader" && i.Field == "levelRoot" && i.IsError));
        }
    }
}
