using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Events;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 回合事件（README 第十一节）的测试。
    ///
    /// 钉住的不变量：
    ///   · **第 1 回合不触发**，第一次落在 `1 + [min,max]`；之后每次重新随机间隔
    ///   · **调度用独立随机流**：同种子下「第几回合来什么事件」与玩家怎么打无关
    ///   · **不放回**，抽空重洗；**不适用的事件被跳过且不算抽过**
    ///   · 事件**先于关卡机制的 OnRoundStart**，且都在小怪移动之前
    ///   · 临时效果在**判定阶段**恢复（瓶子容量）
    ///   · 所有事件都不喂小瓶子、都不打破「棋盘上只出现池内元素」
    /// </summary>
    public class RoundEventTests
    {
        // ---------------------------------------------------------------- 测试替身

        sealed class SpyEvent : IRoundEvent
        {
            public string Id { get; set; } = "spy";
            public string DisplayName => Id;
            public string Description => "测试用事件";

            public bool Applicable = true;
            public string Reason = "测试：故意让它不适用";

            public int Applied;
            public int Reverted;
            public int AppliedRound;
            public string ActiveIdAtApply = "（没读）";

            public bool IsApplicable(GameEngine engine, out string reason)
            {
                reason = Applicable ? null : Reason;
                return Applicable;
            }

            public void Apply(GameEngine engine)
            {
                Applied++;
                AppliedRound = engine.Round;
                ActiveIdAtApply = engine.ActiveEvent == null ? null : engine.ActiveEvent.Id;
            }

            public void Revert(GameEngine engine) => Reverted++;
        }

        /// <summary>记录 OnRoundStart 那一刻引擎上生效的事件 Id（验证「事件先于机制」）。</summary>
        sealed class ActiveEventProbe : ILevelRule
        {
            public string RoundStartActiveId = "（还没轮到过回合开始）";
            public int RoundStarts;

            public string GoalDescription => "探针";
            public void OnLevelStart(GameEngine engine) { }

            public void OnRoundStart(GameEngine engine)
            {
                RoundStarts++;
                RoundStartActiveId = engine.ActiveEvent == null ? null : engine.ActiveEvent.Id;
            }

            public void OnEncounter(GameEngine engine, EncounterInfo info) { }
            public bool TryExtraWin(GameEngine engine, out string message) { message = null; return false; }
            public bool TryExtraLose(GameEngine engine, out string message) { message = null; return false; }
            public GoalProgress GetGoalProgress(GameEngine engine) => default;
        }

        // ---------------------------------------------------------------- 工具

        static Rules MakeRules(int size = 4)
            => new Rules
            {
                BoardWidth = size,
                BoardHeight = size,
                CreepsPerElement = 0,
                InitialHandSize = 0,
                HandLimit = 5,
                MaxCardsPerRound = 0,
                RoundLimit = 15,
                Goal = GoalSpec.Custom(), // 空棋盘 + 内置目标会「开局即胜」，测试一律用 Custom
                ElementPool = ElementMask.All,
                CardPool = ElementMask.All,
                CompositeRange = 1,
                CompositeIngredientsPerRound = 1,
            };

        static GameEngine NewEngine(params IRoundEvent[] pool) => NewEngine(MakeRules(), 3, pool);

        static GameEngine NewEngine(Rules rules, int seed, params IRoundEvent[] pool)
        {
            var engine = new GameEngine(rules, seed);
            engine.AttachEventPool(pool);
            engine.Start();
            return engine;
        }

        /// <summary>把一回合走到头（准备 → 行动 → 弃牌 → 判定 → 下一回合）。不播动画、不碰队列。</summary>
        static void PlayOutRound(GameEngine engine)
        {
            engine.EndPreparationNow();
            if (engine.Phase == GamePhase.PlayerAction) engine.EndPlayerPhase();
            if (engine.Phase == GamePhase.Discard) engine.EndDiscardPhase();
            engine.ResolveJudgment();
        }

        static void SetSerialized(Object target, string field, System.Action<SerializedProperty> set)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            Assert.IsNotNull(prop, $"找不到序列化字段 {field}");
            set(prop);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- 调度

        [Test]
        public void 没挂事件池_行为与以前完全一致()
        {
            var engine = NewEngine();

            Assert.IsFalse(engine.EventsEnabled, "没挂池子 = 没有事件");
            Assert.AreEqual(0, engine.NextEventRound, "0 = 本局不会再有事件");
            Assert.IsNull(engine.ActiveEvent);

            for (int i = 0; i < 5; i++) PlayOutRound(engine);

            Assert.AreEqual(0, engine.EventTriggerCount);
            Assert.AreEqual(6, engine.Round, "回合照常推进");
        }

        [Test]
        public void 第1回合不触发_第一次落在第4到7回合()
        {
            var spy = new SpyEvent();
            var engine = NewEngine(spy);

            int first = engine.NextEventRound;
            Assert.GreaterOrEqual(first, 4, "第 1 回合不触发 + 默认间隔 3~6 → 最早第 4 回合");
            Assert.LessOrEqual(first, 7, "第 1 回合不触发 + 默认间隔 3~6 → 最晚第 7 回合");

            int guard = 0;
            while (spy.Applied == 0 && guard++ < 12) PlayOutRound(engine);

            Assert.AreEqual(1, spy.Applied, "应该恰好触发一次");
            Assert.AreEqual(first, spy.AppliedRound, "就在预告的那一回合触发");
            StringAssert.Contains("spy", spy.ActiveIdAtApply, "Apply 的那一刻它已经挂在引擎上了");
        }

        [Test]
        public void 间隔由Rules决定_每次触发后重新排()
        {
            var rules = MakeRules();
            rules.EventIntervalMin = 2;
            rules.EventIntervalMax = 2;

            var spy = new SpyEvent();
            var engine = NewEngine(rules, 3, spy);

            Assert.AreEqual(3, engine.NextEventRound, "1 + 2");

            PlayOutRound(engine); // 第 1 → 2 回合
            PlayOutRound(engine); // 第 2 → 3 回合（触发）

            Assert.AreEqual(1, spy.Applied);
            Assert.AreEqual(3, spy.AppliedRound);
            Assert.AreEqual(5, engine.NextEventRound, "触发后从当前回合重新排：3 + 2");
        }

        [Test]
        public void 同种子_事件时间表可复现()
        {
            var rules = MakeRules();
            rules.EventIntervalMin = 1;
            rules.EventIntervalMax = 3;

            var spyA = new SpyEvent { Id = "a" };
            var spyB = new SpyEvent { Id = "a" };

            var a = NewEngine(rules, 20240607, spyA);
            var b = NewEngine(rules, 20240607, spyB);

            Assert.AreEqual(a.NextEventRound, b.NextEventRound, "同种子 → 同一次事件回合");

            for (int i = 0; i < 8; i++)
            {
                PlayOutRound(a);
                PlayOutRound(b);
            }

            Assert.AreEqual(spyA.Applied, spyB.Applied, "同种子 → 触发次数一样");
            Assert.Greater(spyA.Applied, 0, "8 回合里总该触发过（间隔 1~3）");
        }

        [Test]
        public void 不放回_抽空后重洗()
        {
            var rules = MakeRules();
            rules.EventIntervalMin = 1;
            rules.EventIntervalMax = 1; // 每回合都触发，方便连抽三次

            var a = new SpyEvent { Id = "a" };
            var b = new SpyEvent { Id = "b" };
            var engine = NewEngine(rules, 3, a, b);

            engine.ForceNextEventRoundForTest(2);

            var order = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                int beforeA = a.Applied;
                int beforeB = b.Applied;
                PlayOutRound(engine);

                if (a.Applied > beforeA) order.Add("a");
                else if (b.Applied > beforeB) order.Add("b");
            }

            Assert.AreEqual(3, order.Count, "三次触发都应该落到某条事件上");
            Assert.AreNotEqual(order[0], order[1], "不放回：前两次必然不同");
            Assert.AreEqual(3, engine.EventTriggerCount);
        }

        [Test]
        public void 不适用的事件被跳过_而且不算抽过()
        {
            var spy = new SpyEvent { Applicable = false };
            var engine = NewEngine(spy);
            engine.ForceNextEventRoundForTest(2);

            PlayOutRound(engine); // 进入第 2 回合

            Assert.AreEqual(0, spy.Applied, "不适用的不该生效");
            Assert.AreEqual(0, engine.EventTriggerCount);
            Assert.IsNull(engine.ActiveEvent);
            Assert.AreEqual(0, engine.DrawnEventCount, "跳过 ≠ 抽过：以后还有机会再抽到它");
            Assert.Greater(engine.NextEventRound, 2, "跳过之后照样排下一次，不会卡在同一个回合");
        }

        [Test]
        public void 事件先于关卡机制的OnRoundStart()
        {
            var spy = new SpyEvent();
            var engine = NewEngine(spy);

            var probe = new ActiveEventProbe();
            engine.AttachRule(probe);
            engine.ForceNextEventRoundForTest(2);

            PlayOutRound(engine); // 第 1 回合结束 → 第 2 回合开始（事件先、机制后）

            Assert.AreEqual("spy", probe.RoundStartActiveId,
                "机制收到 OnRoundStart 时事件应该已经生效（「事件先、机制后」）");
        }

        [Test]
        public void 事件播报排成一个回合事件步骤()
        {
            var spy = new SpyEvent();
            var engine = NewEngine(spy);
            engine.ForceNextEventRoundForTest(2);

            PlayOutRound(engine);

            var step = engine.History.FirstOrDefault(s => s.IsRoundEvent);
            Assert.IsNotNull(step, "事件应该排一个步骤给表现层播（不能悄悄发生）");
            Assert.AreEqual(1, step.Events.Count);
            Assert.AreEqual(GameEventType.RoundEvent, step.Events[0].Type);
            StringAssert.Contains("spy", step.Events[0].Text);
        }

        // ---------------------------------------------------------------- 事件效果

        [Test]
        public void 元素馈赠_给3张且都来自本关牌池()
        {
            var rules = MakeRules();
            rules.CardPool = ElementMask.Water | ElementMask.Metal;

            var engine = NewEngine(rules, 3);

            var ev = ScriptableObject.CreateInstance<Event_GiveElementCards>();
            SetSerialized(ev, "count", p => p.intValue = 3);

            Assert.IsTrue(ev.IsApplicable(engine, out _));
            ev.Apply(engine);

            Assert.AreEqual(3, engine.Hand.Count);
            foreach (var card in engine.Hand)
            {
                Assert.IsTrue(card.IsElementCard);
                CollectionAssert.Contains(new[] { Element.Water, Element.Metal }, card.Element,
                    "只能给本关牌池里的元素，否则是一张打不出去的废牌");
            }
        }

        [Test]
        public void 秘术馈赠_只有融合分解都启用才适用()
        {
            var rules = MakeRules();
            rules.UseCompositeElements = false;

            var engine = NewEngine(rules, 3);
            var ev = ScriptableObject.CreateInstance<Event_GiveSpecialCards>();
            SetSerialized(ev, "countPerKind", p => p.intValue = 1);

            Assert.IsFalse(ev.IsApplicable(engine, out var reason), "没开复合元素时这张牌打不出去");
            StringAssert.Contains("复合元素", reason);

            rules.UseCompositeElements = true;
            rules.SpecialCardPool = SpecialCardMask.Fusion; // 只开融合
            var onlyFusion = NewEngine(rules, 3);
            Assert.IsFalse(ev.IsApplicable(onlyFusion, out _), "只开融合时白送的分解牌是死牌");

            rules.SpecialCardPool = SpecialCardMask.All;
            var both = NewEngine(rules, 3);
            Assert.IsTrue(ev.IsApplicable(both, out _));

            int before = both.Hand.Count;
            ev.Apply(both);
            Assert.AreEqual(before + 2, both.Hand.Count, "融合 + 分解各一张");
            Assert.IsTrue(both.Hand.Any(c => c.IsFusionCard));
            Assert.IsTrue(both.Hand.Any(c => c.IsDecomposeCard));
        }

        [Test]
        public void 玉翁敛息_先结账再降容_恢复时不结账()
        {
            var rules = MakeRules();
            rules.BottleCapacity = 5;

            var engine = NewEngine(rules, 3);
            engine.SetupBottleForTest(4); // 五个瓶子都攒到 4/5

            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            SetSerialized(ev, "capacity", p => p.intValue = 3);

            Assert.IsTrue(ev.IsApplicable(engine, out _));

            int before = engine.Hand.Count;
            ev.Apply(engine);

            Assert.AreEqual(3, engine.Bottle.Capacity, "本回合容量降到 3");
            Assert.AreEqual(before + 5, engine.Hand.Count, "五个瓶子各结账 1 张（4/3 → 1 张 + 余 1）");
            Assert.AreEqual(1, engine.Bottle.Count(Element.Metal), "余数留在瓶里");

            ev.Revert(engine);
            Assert.AreEqual(5, engine.Bottle.Capacity, "恢复成本关的值");
            Assert.AreEqual(1, engine.Bottle.Count(Element.Metal), "恢复本身不结账");
        }

        [Test]
        public void 玉翁敛息_调度步骤包含真实手牌和算盘快照_判定恢复门槛()
        {
            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            try
            {
                SetSerialized(ev, "capacity", p => p.intValue = 3);
                var rules = MakeRules();
                rules.HandLimit = 0;
                var engine = NewEngine(rules, 3, ev);
                engine.AddBottleCharge(Element.Water, 4);
                engine.ForceNextEventRoundForTest(engine.Round + 1);
                int historyBefore = engine.History.Count;
                engine.BeginRound();

                Assert.AreEqual(historyBefore + 1, engine.History.Count, "充能变化应和回合事件在同一步播放");
                var step = engine.History.Last();
                Assert.IsTrue(step.IsRoundEvent);
                var changes = step.Events.Where(e => e.Element == Element.Water &&
                    (e.Type == GameEventType.BottleFill || e.Type == GameEventType.GainCard ||
                     e.Type == GameEventType.BottleCapacityChanged)).ToArray();
                CollectionAssert.AreEqual(new[] { GameEventType.BottleFill, GameEventType.GainCard,
                    GameEventType.BottleCapacityChanged }, changes.Select(e => e.Type).ToArray());
                Assert.AreEqual(3, changes[0].BottleCount);
                Assert.AreEqual(engine.Hand.Single().Id, changes[1].CardId, "飞牌必须绑定真实手牌");
                Assert.AreEqual(1, engine.Log.Count(e => e.Type == GameEventType.GainCard &&
                    e.CardId == changes[1].CardId), "事件发牌日志只能记录一次");
                Assert.IsTrue(changes.All(e => e.BottleCapacity == 3));
                Assert.AreEqual(1, changes[2].BottleCount, "算盘显示兑换后的余数");

                PlayOutRound(engine);
                Assert.AreEqual(5, engine.Bottle.Capacity);
                Assert.AreEqual(1, engine.Bottle.Count(Element.Water));
                Assert.AreEqual(1, engine.Hand.Count, "恢复门槛不能再次发牌");
                var restored = engine.History.SelectMany(s => s.Events).Last(e =>
                    e.Type == GameEventType.BottleCapacityChanged && e.Element == Element.Water);
                Assert.AreEqual(5, restored.BottleCapacity);
                Assert.AreEqual(1, restored.BottleCount);
            }
            finally { Object.DestroyImmediate(ev); }
        }

        [Test]
        public void 玉翁敛息_本关容量本来就一样时不适用()
        {
            var rules = MakeRules();
            rules.BottleCapacity = 3;

            var engine = NewEngine(rules, 3);
            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            SetSerialized(ev, "capacity", p => p.intValue = 3);

            Assert.IsFalse(ev.IsApplicable(engine, out var reason));
            StringAssert.Contains("本来就是", reason);
        }

        [Test]
        public void 事件在判定阶段自动恢复()
        {
            var rules = MakeRules();

            var engine = NewEngine(rules, 3);
            engine.SetupBottleForTest(1); // 别让它因为结账把容量改回来的事测糊

            var ev = ScriptableObject.CreateInstance<Event_BottleCapacity>();
            SetSerialized(ev, "capacity", p => p.intValue = 3);

            engine.AttachEventPool(new IRoundEvent[] { ev });
            engine.ForceNextEventRoundForTest(2);

            PlayOutRound(engine); // 第 1 回合结束 → 第 2 回合开始：事件生效

            Assert.AreEqual(3, engine.Bottle.Capacity, "进入第 2 回合时容量已经变成 3");

            PlayOutRound(engine); // 第 2 回合结束 → 第 3 回合

            Assert.AreEqual(5, engine.Bottle.Capacity, "判定阶段恢复成 Rules.BottleCapacity");
        }

        [Test]
        public void 静滞结界_准备阶段小怪一个都不动()
        {
            var engine = NewEngine();

            var water = engine.PlaceCreep(Element.Water, new Pos(0, 0));
            var fire = engine.PlaceCreep(Element.Fire, new Pos(1, 0));

            engine.SetSuppressCreepMovement(true);

            Assert.IsNull(engine.AdvancePreparation(), "定住 = 没有一步步的移动步骤");
            Assert.AreEqual(GamePhase.PlayerAction, engine.Phase, "直接进玩家行动阶段");
            Assert.AreEqual(0, engine.MovedThisRound, "没有任何小怪行动过");
            Assert.AreEqual(new Pos(0, 0), water.Pos);
            Assert.AreEqual(new Pos(1, 0), fire.Pos);
            Assert.AreEqual(0, engine.TotalTransformed);
            Assert.AreEqual(0, engine.TotalRemoved);
        }

        [Test]
        public void 静滞结界_不挡复合元素的每回合产出()
        {
            var engine = NewEngine();
            engine.PlaceComposite(CompositeKind.Lava, new Pos(2, 2));
            engine.SetSuppressCreepMovement(true);

            var step = engine.AdvancePreparation();

            Assert.IsNotNull(step, "静止的是元素，不是复合元素的产出");
            Assert.IsTrue(step.IsCompositeEffect);
        }

        [Test]
        public void 流漫溢_空则添加_已占就地转换_都不喂瓶子()
        {
            var engine = NewEngine(); // 4×4 = 16 格

            var a = engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.PlaceCreep(Element.Water, new Pos(1, 0));
            engine.PlaceCreep(Element.Water, new Pos(2, 0));

            int changed = engine.ConvertCellsToElement(Element.Metal, 100);

            Assert.AreEqual(16, changed, "16 格全是合法元素格");
            Assert.AreEqual(16, engine.CreepCount);
            Assert.AreEqual(0, engine.Board.CountOf(Element.Water));
            Assert.AreEqual(16, engine.Board.CountOf(Element.Metal));

            Assert.AreEqual(0, engine.Bottle.Count(Element.Metal), "转换和生成都不喂小瓶子");
            Assert.AreEqual(0, engine.TotalRemoved);
            Assert.AreEqual(a.Uid, engine.Board.At(new Pos(0, 0)).Uid, "就地转换：uid 不变，只有元素换了");
        }

        [Test]
        public void 流漫溢_随散9格_墙格与复合元素跳过()
        {
            var engine = NewEngine(); // 16 格
            engine.BlockCell(new Pos(3, 3), true);
            engine.PlaceComposite(CompositeKind.Lava, new Pos(0, 3));

            int changed = engine.ConvertCellsToElement(Element.Fire, 9);

            Assert.AreEqual(9, changed);
            Assert.IsFalse(engine.Board.IsWalkable(new Pos(3, 3)), "墙格不该被踩");
            Assert.IsNull(engine.Board.At(new Pos(3, 3)));
            Assert.IsNull(engine.Board.At(new Pos(0, 3)), "复合元素的格子不是元素格");
            Assert.IsTrue(engine.Board.HasComposite(new Pos(0, 3)));
        }

        [Test]
        public void 流漫溢_目标元素不在本关可用元素里就不适用()
        {
            var rules = MakeRules();
            rules.ElementPool = ElementMask.Water;
            rules.CardPool = ElementMask.Water;

            var engine = NewEngine(rules, 3);
            var ev = ScriptableObject.CreateInstance<Event_ConvertCells>();
            SetSerialized(ev, "element", p => p.intValue = (int)Element.Metal);
            SetSerialized(ev, "cellCount", p => p.intValue = 9);

            Assert.IsFalse(ev.IsApplicable(engine, out var reason));
            StringAssert.Contains("不变量", reason);

            Assert.AreEqual(0, engine.ConvertCellsToElement(Element.Metal, 9),
                "引擎自己也要挡住：往棋盘里塞池外元素会打破既有不变量");
            Assert.AreEqual(0, engine.CreepCount);
        }

        // ---------------------------------------------------------------- 关卡校验

        [Test]
        public void 关卡勾了事件但池子是空的_校验要报出来()
        {
            var level = ScriptableObject.CreateInstance<TapGJ.Levels.LevelDefinition>();
            level.levelId = "t";
            level.displayName = "测试关";
            level.useEvents = true;
            level.roundEvents = new RoundEventDefinition[0];

            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);

            Assert.IsTrue(issues.Any(s => s.Contains("事件池是空的")),
                "勾了开关却没配事件 = 白勾，必须报出来：\n" + string.Join("\n", issues));
        }

        [Test]
        public void 关卡回合上限太小撞不上第一次事件_校验要报出来()
        {
            var level = ScriptableObject.CreateInstance<TapGJ.Levels.LevelDefinition>();
            level.levelId = "t";
            level.displayName = "短关";
            level.useEvents = true;
            level.roundEvents = new RoundEventDefinition[] { ScriptableObject.CreateInstance<Event_GiveElementCards>() };
            level.rules = MakeRules();
            level.rules.RoundLimit = 3;          // 第一次事件最早第 4 回合
            level.rules.EventIntervalMin = 3;
            level.rules.EventIntervalMax = 6;

            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);

            Assert.IsTrue(issues.Any(s => s.Contains("永远不会有事件")),
                "6 回合上限 + 间隔 3~6 → 永远碰不上，必须报出来：\n" + string.Join("\n", issues));
        }
    }
}
