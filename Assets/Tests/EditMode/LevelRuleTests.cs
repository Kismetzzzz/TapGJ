using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Levels;
using TapGJ.Levels.Mechanics;
using TapGJ.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 关卡机制（P3）测试：ILevelRule 的挂载与回调时机、Custom 目标与内置目标的关系、
    /// 注入 API（生成/移除/加墙/排队步骤）、以及两个示例机制。
    ///
    /// 核心不变量：
    ///   · 目标是 Custom 时内置判定完全不参与（否则会因为 Goal 的默认值提前获胜）
    ///   · 目标是别的时机制只能「加赢」
    ///   · 相遇快照里的元素是「相遇当时」的，相生把输家改掉之后也读得到
    /// </summary>
    public class LevelRuleTests
    {
        // ---------------------------------------------------------------- 测试替身

        sealed class FakeRule : ILevelRule
        {
            public string GoalDescription { get; set; } = "假目标";
            public Func<GameEngine, bool> Win;
            public Func<GameEngine, bool> Lose;

            public int LevelStarts;
            public int RoundStarts;
            public int Encounters;
            public int CreepsAtLevelStart = -1;
            public readonly List<EncounterInfo> Seen = new List<EncounterInfo>();

            public void OnLevelStart(GameEngine engine)
            {
                LevelStarts++;
                CreepsAtLevelStart = engine.CreepCount;
            }

            public void OnRoundStart(GameEngine engine) => RoundStarts++;

            public void OnEncounter(GameEngine engine, EncounterInfo info)
            {
                Encounters++;
                Seen.Add(info);
            }

            public bool TryExtraWin(GameEngine engine, out string message)
            {
                message = "假胜利：机制判定达成";
                return Win != null && Win(engine);
            }

            public bool TryExtraLose(GameEngine engine, out string message)
            {
                message = "假失败：机制判定未达成";
                return Lose != null && Lose(engine);
            }

            public GoalProgress GetGoalProgress(GameEngine engine)
                => new GoalProgress { Description = GoalDescription, Fill = 0.5f, Label = "假进度 1/2" };
        }

        /// <summary>OnRoundStart 抛异常，用来验证「坏掉的机制会被摘掉而不是每回合刷屏」。</summary>
        sealed class ThrowingRule : ILevelRule
        {
            public string GoalDescription => "会炸的机制";
            public void OnLevelStart(GameEngine engine) { }
            public void OnRoundStart(GameEngine engine) => throw new InvalidOperationException("故意炸的");
            public void OnEncounter(GameEngine engine, EncounterInfo info) { }
            public bool TryExtraWin(GameEngine engine, out string message) { message = null; return false; }
            public bool TryExtraLose(GameEngine engine, out string message) { message = null; return false; }
            public GoalProgress GetGoalProgress(GameEngine engine) => default;
        }

        // ---------------------------------------------------------------- 工具

        static Rules MakeRules(GoalSpec goal, int size = 4)
            => new Rules
            {
                BoardWidth = size,
                BoardHeight = size,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                RoundLimit = 15,
                Goal = goal,
            };

        static GameEngine NewEngine(GoalSpec goal, int size = 4, int seed = 3)
        {
            var engine = new GameEngine(MakeRules(goal, size), seed);
            engine.Start();
            return engine;
        }

        /// <summary>把引擎推进到玩家行动阶段（先空场，免得准备阶段把小怪走掉）。</summary>
        static void ToPlayerAction(GameEngine engine)
        {
            engine.EndPreparationNow();
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase, "应该已经进入玩家行动阶段");
        }

        static void SetSerialized(Object target, string field, Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            Assert.IsNotNull(prop, $"找不到序列化字段 {field}");
            set(prop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- Custom 目标 vs 内置目标

        [Test]
        public void Custom目标_内置判定完全不参与()
        {
            var engine = NewEngine(GoalSpec.Custom());
            engine.PlaceCreep(Element.Water, new Pos(0, 0));

            // 这个局面本来满足「只剩水元素」，但目标是 Custom，所以内置判定必须让位。
            Assert.IsFalse(engine.CheckWinCondition(out _), "Custom 时不该由内置目标判赢");
            StringAssert.Contains("自定义", engine.WinConditionDescription());
            StringAssert.Contains("自定义", GoalSpec.Custom().Describe());
        }

        [Test]
        public void Custom目标_挂了机制就由机制说了算()
        {
            var engine = NewEngine(GoalSpec.Custom());
            var rule = new FakeRule { GoalDescription = "把金元素全清掉", Win = _ => true };
            engine.AttachRule(rule);

            Assert.IsTrue(engine.CheckWinCondition(out var reason));
            StringAssert.Contains("假胜利", reason);
            Assert.AreEqual("把金元素全清掉", engine.WinConditionDescription(), "文案应该换成机制给的那句");
            Assert.AreEqual("假进度 1/2", engine.GetGoalProgress().Label, "进度条也应该来自机制");
        }

        [Test]
        public void 非Custom目标_机制只能加赢()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            engine.PlaceCreep(Element.Metal, new Pos(0, 0));

            Assert.IsFalse(engine.CheckWinCondition(out _), "只剩金，内置目标没达成");

            engine.AttachRule(new FakeRule { Win = _ => true });
            Assert.IsTrue(engine.CheckWinCondition(out var reason), "机制可以额外判赢");
            StringAssert.Contains("假胜利", reason);
        }

        [Test]
        public void 机制可以提前判负()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            engine.AttachRule(new FakeRule { Lose = _ => true });

            // 完整的回合链：准备 → 玩家行动 → 弃牌 → 判定（FinishRound 只到弃牌，所以要走齐三步）
            ToPlayerAction(engine);
            engine.EndPlayerPhase();
            engine.EndDiscardPhase();
            engine.ResolveJudgment();

            Assert.AreEqual(GamePhase.Defeat, engine.Phase, "机制判负应该立刻结束本局");
            Assert.IsTrue(engine.Log.Any(e => e.Type == GameEventType.Lose && e.Text.Contains("假失败")),
                "结算横幅要拿到机制给的那句话");
        }

        [Test]
        public void 多个机制_按挂载顺序依次生效()
        {
            var engine = NewEngine(GoalSpec.Custom());
            var first = new FakeRule { Win = _ => false };
            var second = new FakeRule { Win = _ => true };

            engine.AttachRule(first);
            engine.AttachRule(second);

            Assert.IsTrue(engine.CheckWinCondition(out _));
            Assert.AreEqual(1, first.LevelStarts);
            Assert.AreEqual(1, second.LevelStarts);
        }

        // ---------------------------------------------------------------- 回调时机

        [Test]
        public void OnLevelStart_在棋盘铺好发完牌之后()
        {
            var setup = new LevelSetup
            {
                Cells = new Element?[16],
                Blocked = new bool[16],
                InitialHand = new[] { Element.Water, Element.Fire },
            };
            setup.Cells[0] = Element.Water;

            var engine = new GameEngine(MakeRules(GoalSpec.WaterOnly()), 11);
            var rule = new FakeRule();
            engine.AttachRule(rule); // Start 之前挂 → 回调要等铺完盘
            engine.Start(setup);

            Assert.AreEqual(1, rule.LevelStarts);
            Assert.AreEqual(1, rule.CreepsAtLevelStart, "OnLevelStart 时棋盘上那一只水已经在位");
            Assert.AreEqual(2, engine.Hand.Count);
            Assert.AreEqual(1, rule.RoundStarts, "Start 之后紧跟着第 1 回合的 OnRoundStart");
        }

        [Test]
        public void OnRoundStart_每回合一次()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            var rule = new FakeRule();
            engine.AttachRule(rule); // Start 之后挂：不补发已经过去的回合开始

            Assert.AreEqual(1, rule.LevelStarts, "补挂时仍然补发一次 OnLevelStart");
            Assert.AreEqual(0, rule.RoundStarts);

            engine.SimulateRound();
            Assert.AreEqual(1, rule.RoundStarts);
            engine.SimulateRound();
            Assert.AreEqual(2, rule.RoundStarts);
        }

        [Test]
        public void 机制抛异常_只停用它自己()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            var bad = new ThrowingRule();
            engine.AttachRule(bad);
            engine.AttachRule(new FakeRule());

            Assert.DoesNotThrow(() => engine.SimulateRound(), "机制炸了不该把主流程带崩");
            Assert.IsNull(engine.LevelRules[0], "抛异常的机制被摘掉");
            Assert.IsNotNull(engine.LevelRules[1], "别的机制不受影响");
            Assert.IsTrue(engine.Log.Any(e => e.Text != null && e.Text.Contains("已停用")), "日志里要说一声");
        }

        // ---------------------------------------------------------------- 相遇通知

        [Test]
        public void 相遇通知_出牌也会通知_快照是相克()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            var rule = new FakeRule();
            engine.AttachRule(rule);

            ToPlayerAction(engine);
            engine.PlaceCreep(Element.Fire, new Pos(1, 1));
            engine.ForceHandForTest(new[] { Element.Water });

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1));
            Assert.IsNotNull(record, engine.FailReason);

            Assert.AreEqual(1, rule.Encounters);
            var info = rule.Seen[0];
            Assert.AreEqual(Element.Water, info.TriggerElement, "牌面元素是主动方");
            Assert.AreEqual(Element.Fire, info.TargetElement);
            Assert.AreEqual(ReactionRule.Annihilate, info.Rule);
            Assert.AreEqual(Element.Water, info.Winner);
            Assert.AreEqual(Element.Fire, info.Loser);
            Assert.IsTrue(info.IsPlayerCard);
            Assert.AreEqual(-1, info.TriggerUid, "牌不是小怪，没有 uid");
            Assert.IsTrue(info.HasWinner);
            Assert.IsTrue(info.IsDestruction);
            Assert.IsFalse(info.IsGeneration);
            Assert.AreEqual(0, engine.CreepCount);
        }

        [Test]
        public void 相遇通知_相生的快照是变之前的元素()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            var rule = new FakeRule();
            engine.AttachRule(rule);

            ToPlayerAction(engine);
            engine.PlaceCreep(Element.Metal, new Pos(1, 1));
            engine.ForceHandForTest(new[] { Element.Water });

            var record = engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1));
            Assert.IsNotNull(record, engine.FailReason);

            var info = rule.Seen[0];
            Assert.AreEqual(ReactionRule.Transform, info.Rule);
            Assert.AreEqual(Element.Metal, info.TargetElement, "目标相遇时是金");
            Assert.AreEqual(Element.Metal, info.Loser, "输家必须是「变之前」的金，不能是变完的水");
            Assert.AreEqual(Element.Water, info.Winner);
            Assert.IsTrue(info.IsGeneration);
            Assert.IsFalse(info.IsDestruction);
            Assert.AreEqual(2, engine.CreepCount, "金变水 + 相生生成一只水");
            Assert.AreEqual(2, engine.Board.CountOf(Element.Water));
        }

        // ---------------------------------------------------------------- 注入 API

        [Test]
        public void 注入_生成与移除()
        {
            var engine = NewEngine(GoalSpec.ClearBoard());

            var creep = engine.SpawnAt(new Pos(2, 2), Element.Earth);
            Assert.IsNotNull(creep);
            Assert.AreEqual(1, engine.CreepCount);
            Assert.AreEqual(1, engine.SpawnedOf(Element.Earth));
            Assert.IsNull(engine.SpawnAt(new Pos(2, 2), Element.Earth), "同一格不能放两只");
            Assert.IsNull(engine.SpawnAt(new Pos(9, 9), Element.Earth), "越界要拒绝而不是抛异常");

            Assert.IsTrue(engine.DestroyCreep(creep.Uid));
            Assert.AreEqual(0, engine.CreepCount);
            Assert.AreEqual(1, engine.RemovedOf(Element.Earth), "删除走正常路径，瓶子照常记账");
            Assert.IsFalse(engine.DestroyCreep(creep.Uid), "已经死掉的不能再删");
            Assert.AreEqual(1, engine.PendingStepCount, "删除会排出一步给表现层播");
        }

        [Test]
        public void 注入_加墙会挡住生成和行走()
        {
            var engine = NewEngine(GoalSpec.WaterOnly());
            var wall = new Pos(0, 0);

            engine.BlockCell(wall);
            Assert.IsTrue(engine.Board.IsBlocked(wall));
            Assert.IsFalse(engine.Board.IsWalkable(wall));
            Assert.IsNull(engine.SpawnAt(wall, Element.Water), "墙格不能生成");

            engine.BlockCell(wall, false);
            Assert.IsTrue(engine.Board.IsWalkable(wall));
            Assert.IsNotNull(engine.SpawnAt(wall, Element.Water));
        }

        [Test]
        public void 注入_随机空格与排队提示()
        {
            var engine = NewEngine(GoalSpec.ClearBoard(), 2);
            var cell = engine.RandomEmptyCell();
            Assert.IsFalse(cell.IsNone);
            Assert.IsTrue(engine.Board.IsEmpty(cell));

            engine.QueueNotice("机制说了一句话", cell, Element.Water);
            Assert.AreEqual(1, engine.PendingStepCount);
            var step = engine.DequeueStep();
            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(GameEventType.Message, step.Events[0].Type);
            StringAssert.Contains("机制说了一句话", step.Events[0].Text);

            // 棋盘填满之后没有空格可挑
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    engine.SpawnAt(new Pos(x, y), Element.Water);
            Assert.IsTrue(engine.RandomEmptyCell().IsNone, "满了就返回 Pos.None");
        }

        // ---------------------------------------------------------------- 示例机制

        [Test]
        public void 示例_相生计数机制可以当自定义胜利条件()
        {
            var go = new GameObject("mech-reaction-count");
            try
            {
                var rule = go.AddComponent<ReactionCountRule>();
                SetSerialized(rule, "target", p => p.intValue = 1);

                var engine = NewEngine(GoalSpec.Custom());
                engine.AttachRule(rule);

                Assert.AreEqual("累计完成 1 次相生转换", rule.GoalDescription);
                Assert.AreEqual("累计完成 1 次相生转换", engine.WinConditionDescription());
                Assert.IsFalse(engine.CheckWinCondition(out _), "还没转换过，不该赢");

                ToPlayerAction(engine);
                engine.PlaceCreep(Element.Metal, new Pos(1, 1));
                engine.ForceHandForTest(new[] { Element.Water });
                Assert.IsNotNull(engine.TryPlayCard(engine.Hand[0].Id, new Pos(1, 1)), engine.FailReason);

                Assert.AreEqual(1, rule.Count, "金变水算一次相生转换");
                Assert.AreEqual("相生转换 1 / 1", engine.GetGoalProgress().Label);
                Assert.IsTrue(engine.CheckWinCondition(out var reason));
                StringAssert.Contains("相生转换", reason);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 示例_每N回合生成机制()
        {
            var go = new GameObject("mech-spawn");
            try
            {
                var rule = go.AddComponent<SpawnOnRoundRule>();
                SetSerialized(rule, "element", p => p.intValue = (int)Element.Fire);
                SetSerialized(rule, "everyRounds", p => p.intValue = 2);
                SetSerialized(rule, "countPerTime", p => p.intValue = 1);

                var engine = NewEngine(GoalSpec.WaterOnly());
                engine.AttachRule(rule);
                Assert.AreEqual(0, engine.Board.CountOf(Element.Fire));

                engine.SimulateRound(); // 第 1 回合结束 → 进入第 2 回合，触发一次生成
                Assert.AreEqual(2, engine.Round);
                Assert.AreEqual(1, engine.Board.CountOf(Element.Fire));

                engine.SimulateRound(); // 第 2 回合结束 → 第 3 回合，3 % 2 != 0，不再生成
                Assert.AreEqual(3, engine.Round);
                Assert.AreEqual(1, engine.Board.CountOf(Element.Fire), "只有每 2 回合才生成");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- 契约

        [Test]
        public void 契约_LevelLoader就是关卡宿主()
        {
            Assert.IsTrue(typeof(ILevelHost).IsAssignableFrom(typeof(LevelLoader)),
                "GameSession 靠 ILevelHost 重开关卡，LevelLoader 必须实现它");
        }
    }
}
