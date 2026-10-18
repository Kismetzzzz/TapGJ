using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.EditorTools;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 调试工具（<see cref="LevelUnlockTools"/>，编辑器菜单「TapGJ ▸ 调试」和那个面板都用它）的测试。
    ///
    /// 它做的事很窄：往 PlayerPrefs 里写「已通关」标记。但正因为直接写存档，两条规矩必须钉死：
    ///   · **只按已绑定关卡的展平顺序推进**（和真实解锁链的口径一致，待开放的条目不参与）
    ///   · **不写最好成绩** —— 调试推关卡不该污染真实的回合数记录
    ///
    /// 用测试专用 Id（`test_unlock_*`），跑的时不会碰到真实存档。
    /// </summary>
    public class LevelDebugToolsTests
    {
        static readonly string[] Ids = { "test_unlock_01", "test_unlock_02", "test_unlock_03", "test_unlock_04" };

        ChapterCatalog _catalog;
        readonly List<Object> _created = new List<Object>();

        LevelDefinition MakeLevel(string id)
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.levelId = id;
            def.displayName = id;
            _created.Add(def);
            return def;
        }

        /// <summary>
        /// 两章 5 条：第一章 [关1(绑定) / 待开放 / 关2(绑定)]，第二章 [关3 / 关4]。
        /// 中间那个待开放条目专门用来验证「它不参与解锁链」。
        /// </summary>
        void BuildCatalog()
        {
            _catalog = ScriptableObject.CreateInstance<ChapterCatalog>();
            _created.Add(_catalog);

            var ch1 = new ChapterCatalog.Chapter
            {
                title = "第一章",
                levels = new[]
                {
                    new ChapterCatalog.Level { title = "第 1 关", definition = MakeLevel(Ids[0]) },
                    new ChapterCatalog.Level { title = "第 2 关", definition = null },   // 待开放
                    new ChapterCatalog.Level { title = "第 3 关", definition = MakeLevel(Ids[1]) },
                },
            };
            var ch2 = new ChapterCatalog.Chapter
            {
                title = "第二章",
                levels = new[]
                {
                    new ChapterCatalog.Level { title = "第 1 关", definition = MakeLevel(Ids[2]) },
                    new ChapterCatalog.Level { title = "第 2 关", definition = MakeLevel(Ids[3]) },
                },
            };

            _catalog.chapters = new[] { ch1, ch2 };
        }

        [SetUp]
        public void SetUp()
        {
            foreach (var id in Ids) ProgressStore.Reset(id);
            BuildCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var id in Ids) ProgressStore.Reset(id);

            foreach (var obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();

            _catalog = null;
        }

        // ---------------------------------------------------------------- 基本语义

        [Test]
        public void 已绑定关卡按展平顺序排列_待开放的条目不参与()
        {
            var bound = LevelUnlockTools.BoundLevels(_catalog);
            Assert.AreEqual(4, bound.Count, "5 条里只有 4 条绑了关卡");
            CollectionAssert.AreEqual(Ids, bound.ConvertAll(l => l.definition.levelId));
            Assert.AreEqual(4, LevelUnlockTools.TotalCount(_catalog));
            Assert.AreEqual(0, LevelUnlockTools.ClearedCount(_catalog), "一开始一关都没通关");
        }

        [Test]
        public void 解锁下一关_一次推一关_下一关随即解锁()
        {
            Assert.AreEqual(Ids[0], LevelUnlockTools.NextToUnlock(_catalog).levelId, "第一关是链头，本来就解锁");
            Assert.IsTrue(_catalog.IsUnlocked(0, 0));
            Assert.IsFalse(_catalog.IsUnlocked(0, 2), "第 3 关还锁着");

            var first = LevelUnlockTools.UnlockNext(_catalog);
            Assert.AreEqual(Ids[0], first.levelId, "推的是第一个还没通关的关卡");
            Assert.IsTrue(_catalog.IsUnlocked(0, 2), "第 1 关通关 → 第 3 关解锁（中间那个待开放的跳过去）");
            Assert.IsFalse(_catalog.IsUnlocked(1, 0), "第二章还早");

            var second = LevelUnlockTools.UnlockNext(_catalog);
            Assert.AreEqual(Ids[1], second.levelId);
            Assert.IsTrue(_catalog.IsUnlocked(1, 0), "第一章清完 → 第二章第 1 关解锁");

            LevelUnlockTools.UnlockNext(_catalog);
            LevelUnlockTools.UnlockNext(_catalog);

            Assert.IsNull(LevelUnlockTools.NextToUnlock(_catalog), "全推完了就没有下一关了");
            Assert.IsNull(LevelUnlockTools.UnlockNext(_catalog));
            Assert.AreEqual(4, LevelUnlockTools.ClearedCount(_catalog));
        }

        [Test]
        public void 解锁到这里_只标目标之前的那几关_目标本身不动()
        {
            var target = _catalog.LevelAt(1, 1).definition;   // 关4

            int changed = LevelUnlockTools.UnlockUpTo(_catalog, target);
            Assert.AreEqual(3, changed, "关4 前面有 3 个已绑定关卡");
            Assert.AreEqual(Ids[3], LevelUnlockTools.NextToUnlock(_catalog).levelId, "关4 本身还没通关");
            Assert.IsTrue(_catalog.IsUnlocked(1, 1), "关4 现在可以进");
            Assert.AreEqual("开始", _catalog.Badge(1, 1));
            Assert.AreEqual("已通关", _catalog.Badge(0, 0));

            // 再来一次什么都不变（幂等）
            Assert.AreEqual(0, LevelUnlockTools.UnlockUpTo(_catalog, target));
        }

        [Test]
        public void 全部解锁与清空进度()
        {
            Assert.AreEqual(4, LevelUnlockTools.UnlockAll(_catalog));
            Assert.AreEqual(4, LevelUnlockTools.ClearedCount(_catalog));
            Assert.AreEqual(0, LevelUnlockTools.UnlockAll(_catalog), "第二次没有新东西可标");

            Assert.AreEqual(4, LevelUnlockTools.ResetAll(_catalog));
            Assert.AreEqual(0, LevelUnlockTools.ClearedCount(_catalog));
            Assert.IsTrue(_catalog.IsUnlocked(0, 0), "清空之后第一关仍然能进（链头）");
            Assert.IsFalse(_catalog.IsUnlocked(0, 2));
            Assert.AreEqual(0, LevelUnlockTools.ResetAll(_catalog), "已经是空的，再清一次没有变化");
        }

        // ---------------------------------------------------------------- 不能污染成绩

        [Test]
        public void 调试解锁不写最好成绩()
        {
            var level = LevelUnlockTools.UnlockNext(_catalog);

            Assert.IsTrue(ProgressStore.IsCleared(level.levelId));
            Assert.AreEqual(0, ProgressStore.BestRounds(level.levelId),
                "调试推关卡不该写回合数记录 —— 那是真实通关才写的");
        }
    }
}
