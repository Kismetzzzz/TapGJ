using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 章节配置（<see cref="ChapterCatalog"/>）的解锁链测试。
    ///
    /// 钉住的三条规矩：
    ///   · **没绑关卡的条目 = 待开放**，不参与解锁链（开发期没做的那一关不能把后面全锁死）
    ///   · 链上**第一关默认解锁**，其余要等前面最近的**已绑定**关卡通关
    ///   · 解锁链**跨章节展平**：第 1 章最后一关通关 → 第 2 章第 1 关解锁
    ///
    /// 这一层是纯数据 + ProgressStore（PlayerPrefs），不碰场景，所以能直接建 ScriptableObject 来验。
    /// </summary>
    public class ChapterCatalogTests
    {
        static readonly string[] Ids = { "test_ch1_01", "test_ch1_02", "test_ch1_03", "test_ch2_01" };

        ChapterCatalog _catalog;
        readonly List<Object> _created = new List<Object>();

        // ---------------------------------------------------------------- 搭数据

        LevelDefinition MakeLevel(string id)
        {
            var def = ScriptableObject.CreateInstance<LevelDefinition>();
            def.levelId = id;
            def.displayName = id;
            _created.Add(def);
            return def;
        }

        ChapterCatalog.Level MakeEntry(string title, LevelDefinition level)
            => new ChapterCatalog.Level { title = title, goal = "目标", definition = level };

        /// <summary>两章：第一章 3 关、第二章 1 关。<paramref name="bound"/> 标记哪些条目绑了关卡。</summary>
        ChapterCatalog BuildCatalog(params bool[] bound)
        {
            _catalog = ScriptableObject.CreateInstance<ChapterCatalog>();
            _created.Add(_catalog);

            var ch1 = new ChapterCatalog.Chapter { title = "第一章", levels = new ChapterCatalog.Level[3] };
            var ch2 = new ChapterCatalog.Chapter { title = "第二章", levels = new ChapterCatalog.Level[1] };

            for (int i = 0; i < 3; i++)
                ch1.levels[i] = MakeEntry($"第 {i + 1} 关", (i < bound.Length && bound[i]) ? MakeLevel(Ids[i]) : null);

            ch2.levels[0] = MakeEntry("第 1 关", (bound.Length > 3 && bound[3]) ? MakeLevel(Ids[3]) : null);

            _catalog.chapters = new[] { ch1, ch2 };
            return _catalog;
        }

        [SetUp]
        public void SetUp()
        {
            ProgressStore.Reset("");
            foreach (var id in Ids) ProgressStore.Reset(id);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var id in Ids) ProgressStore.Reset(id);
            foreach (var obj in _created) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        // ---------------------------------------------------------------- 待开放

        [Test]
        public void 没绑关卡的条目是待开放_也不参与解锁链()
        {
            var catalog = BuildCatalog();
            Assert.AreEqual("待开放", catalog.Badge(0, 0));
            Assert.IsFalse(catalog.IsUnlocked(0, 0), "还没做的关卡谈不上解锁");
            Assert.IsFalse(catalog.HasUnlockedLevel(0));
        }

        [Test]
        public void 解锁链会跳过没绑关卡的条目()
        {
            // 第 1 关已绑定且通关；第 2 关还没做（不绑）；第 3 关已绑定 → 应该直接解锁第 3 关
            var catalog = BuildCatalog(true, false, true);
            ProgressStore.MarkCleared(Ids[0], 5);

            Assert.AreEqual("待开放", catalog.Badge(0, 1));
            Assert.IsTrue(catalog.IsUnlocked(0, 2), "中间那个待开放的不能把后面的关卡锁死");
            Assert.AreEqual(Ids[0], catalog.PreviousBound(0, 2).definition.levelId, "上一关指的是最近一个**已绑定**的关卡");
        }

        // ---------------------------------------------------------------- 顺序解锁

        [Test]
        public void 链上第一关默认解锁_后面的要等上一关通关()
        {
            var catalog = BuildCatalog(true, true, true, true);

            Assert.IsTrue(catalog.IsUnlocked(0, 0), "第一关永远开放");
            Assert.AreEqual("开始", catalog.Badge(0, 0));

            Assert.IsFalse(catalog.IsUnlocked(0, 1));
            Assert.AreEqual("待解锁", catalog.Badge(0, 1));

            ProgressStore.MarkCleared(Ids[0], 4);
            Assert.IsTrue(catalog.IsUnlocked(0, 1), "第 1 关通关后第 2 关解锁");
            Assert.AreEqual("已通关", catalog.Badge(0, 0));

            ProgressStore.MarkCleared(Ids[1], 4);
            Assert.IsTrue(catalog.IsUnlocked(0, 2));
            Assert.IsFalse(catalog.IsUnlocked(1, 0), "第一章没打完，第二章不该开");

            ProgressStore.MarkCleared(Ids[2], 4);
            Assert.IsTrue(catalog.IsUnlocked(1, 0), "解锁链跨章节：第一章最后一关通关 → 第二章第 1 关解锁");
        }

        [Test]
        public void 上一关指展平顺序里的上一个已绑定关卡()
        {
            var catalog = BuildCatalog(true, true, true, true);

            Assert.IsNull(catalog.PreviousBound(0, 0), "第一关没有前置");
            Assert.AreEqual(Ids[0], catalog.PreviousBound(0, 1).definition.levelId);
            Assert.AreEqual(Ids[2], catalog.PreviousBound(1, 0).definition.levelId, "第二章第 1 关的前置是第一章最后一关");
        }

        [Test]
        public void 越界与空槽位不会炸()
        {
            var catalog = BuildCatalog(true);

            Assert.IsNull(catalog.LevelAt(-1, 0));
            Assert.IsNull(catalog.LevelAt(0, 99));
            Assert.IsNull(catalog.LevelAt(99, 0));
            Assert.AreEqual(string.Empty, catalog.Badge(5, 5));
            Assert.IsNull(catalog.PreviousBound(99, 99));
        }
    }
}
