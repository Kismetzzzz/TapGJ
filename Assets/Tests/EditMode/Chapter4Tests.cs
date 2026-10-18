using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Events;
using TapGJ.Levels;
using TapGJ.Levels.Mechanics;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 第四章「加入事件牌」：9 个事件资产 + 5 关真实资产。
    ///
    /// 设计稿（见 <c>ChapterCatalog.asset</c> 第四章那 5 条）：
    ///   1. 发牌事件 · 运用随机分解两种复合元素
    ///   2. 固定范围事件 · 小瓶子生成 3 水 6 土 5 火 4 金 2 木
    ///   3. 随机范围事件 · 场上只剩土
    ///   4. 场上只剩金和土
    ///   5. 平均元素 · 通过关卡（收尾）
    ///
    /// 这些用例除了钉数值，还专门钉了一条**接线约定**：每一章绑的关卡必须属于这一章
    /// （`levelId` 前缀 `chN_`）。之前第四章的 5 条被误绑到第三章的关卡上过 ——
    /// 菜单里点「第四章第 1 关」会进第三章的关，而这种错肉眼很难发现。
    /// </summary>
    public class Chapter4Tests
    {
        const string RealCatalogPath = "Assets/Levels/LevelCatalog.asset";
        const string RealChapterPath = "Assets/UI/Data/ChapterCatalog.asset";
        const string EventCatalogPath = "Assets/Events/EventCatalog.asset";
        const string EventDir = "Assets/Events";

        static readonly string[] LevelIds = { "ch4_01", "ch4_02", "ch4_03", "ch4_04", "ch4_05" };

        static LevelDefinition Load(string id)
            => AssetDatabase.LoadAssetAtPath<LevelDefinition>("Assets/Levels/" + id + ".asset");

        // ---------------------------------------------------------------- 事件资产

        [Test]
        public void 事件目录与文件夹里的资产一致_且Id不重复()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EventCatalog>(EventCatalogPath);
            Assert.IsNotNull(catalog, "找不到 " + EventCatalogPath);

            var issues = new List<string>();
            catalog.CollectIssues(issues);
            Assert.IsEmpty(issues, string.Join(" / ", issues));

            var inCatalog = catalog.All().ToList();
            Assert.AreEqual(10, inCatalog.Count, "第四章准备了 10 条事件（给牌 2 + 玉翁 + 静滞 + 流漫溢 6）");

            var onDisk = new List<RoundEventDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("", new[] { EventDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".asset")) continue;
                var ev = AssetDatabase.LoadAssetAtPath<RoundEventDefinition>(path);
                if (ev != null) onDisk.Add(ev);
            }

            CollectionAssert.AreEquivalent(
                inCatalog.Select(e => e.Id).OrderBy(x => x).ToList(),
                onDisk.Select(e => e.Id).OrderBy(x => x).ToList(),
                "目录漏了资产，或者有资产没进目录（「校验所有事件」菜单就靠目录）");

            CollectionAssert.AllItemsAreUnique(inCatalog.Select(e => e.Id).ToList(),
                "事件 Id 不能重复 —— 「本局不重复」是按 Id 记账的");
        }

        [Test]
        public void 三种流漫溢的取格方式都在_固定范围与随机范围各有资产()
        {
            var modes = new Dictionary<ConvertAreaMode, int>();
            foreach (var guid in AssetDatabase.FindAssets("", new[] { EventDir }))
            {
                var ev = AssetDatabase.LoadAssetAtPath<RoundEventDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                var convert = ev as Event_ConvertCells;
                if (convert == null) continue;
                modes[convert.Mode] = (modes.TryGetValue(convert.Mode, out var n) ? n : 0) + 1;
            }

            Assert.IsTrue(modes.ContainsKey(ConvertAreaMode.FixedRect), "固定范围事件（FixedRect）至少要有一个");
            Assert.IsTrue(modes.ContainsKey(ConvertAreaMode.RandomBlock), "随机范围事件（RandomBlock）至少要有一个");
            Assert.IsTrue(modes.ContainsKey(ConvertAreaMode.Scatter), "散落（Scatter）也要留一个 —— 最早的形态");
        }

        // ---------------------------------------------------------------- 关卡资产

        [Test]
        public void 第四章五关都是零问题配置()
        {
            // 这一条同时把「事件池里每条事件在本关适不适用」也验了：
            // LevelDefinition.CollectEventIssues 会真的开一局探针引擎去问每条事件。
            foreach (var id in LevelIds)
            {
                var level = Load(id);
                Assert.IsNotNull(level, "找不到关卡资产 " + id);

                var issues = new List<string>();
                level.CollectAuthoringIssues(issues);
                Assert.IsEmpty(issues, id + " 有配置问题：" + string.Join(" / ", issues));

                Assert.IsTrue(level.useEvents, id + " 是事件牌那一章，必须开着事件");
                Assert.IsNotNull(level.rules, id + " 的 rules 是 null");
                Assert.AreEqual(GoalKind.Custom, level.rules.Goal.Kind, id + " 的目标要自定义");
                Assert.GreaterOrEqual(level.rules.RoundLimit, 1 + level.rules.EventIntervalMin,
                    id + " 的回合上限必须够撞上第一次事件");
            }
        }

        [Test]
        public void 第四章五关的事件池与数值钉死()
        {
            // (关卡, 瓶容量, 回合上限, 事件间隔 min/max, 事件 Id 列表, 目标组件描述)
            var table = new (string id, int capacity, int limit, int min, int max, string[] events, string goals)[]
            {
                ("ch4_01", 5, 10, 2, 3, new[] { "ev_element_gift", "ev_special_gift" },
                 "decompose:2:noComposite"),
                ("ch4_02", 1, 10, 2, 3, new[] { "ev_convert_fire_fixed", "ev_convert_earth_fixed" },
                 "bottle:水3,bottle:土6,bottle:火5,bottle:金4,bottle:木2"),
                ("ch4_03", 5, 10, 2, 3, new[] { "ev_convert_water_block", "ev_convert_fire_block" },
                 "only:土"),
                ("ch4_04", 5, 10, 2, 3, new[] { "ev_element_gift", "ev_convert_wood_scatter" },
                 "only:金土"),
                ("ch4_05", 5, 8, 1, 2, new[] { "ev_element_gift", "ev_convert_metal_block", "ev_stasis" },
                 "equal:1"),
            };

            foreach (var row in table)
            {
                var level = Load(row.id);
                Assert.IsNotNull(level, "找不到关卡资产 " + row.id);

                Assert.AreEqual(row.capacity, level.rules.BottleCapacity, row.id + " 的瓶容量");
                Assert.AreEqual(row.limit, level.rules.RoundLimit, row.id + " 的回合上限");
                Assert.AreEqual(row.min, level.rules.EventIntervalMin, row.id + " 的事件间隔下限");
                Assert.AreEqual(row.max, level.rules.EventIntervalMax, row.id + " 的事件间隔上限");

                CollectionAssert.AreEqual(row.events, level.roundEvents.Select(e => e.Id).ToArray(),
                    row.id + " 的事件池（顺序 = 预制体/资产的挂载顺序，改了就说明设计变了）");

                Assert.AreEqual(row.goals, DescribeGoals(level), row.id + " 的目标组件");
            }
        }

        /// <summary>把预制体上的目标组件读成一行字，方便在断言里钉死。</summary>
        static string DescribeGoals(LevelDefinition level)
        {
            Assert.IsNotNull(level.levelPrefab, level.levelId + " 缺关卡预制体");

            var parts = new List<string>();
            foreach (var rule in level.levelPrefab.GetComponentsInChildren<ILevelRule>(true))
            {
                switch (rule)
                {
                    case DecomposeCountRule decompose:
                        parts.Add($"decompose:{decompose.RequiredCount}:"
                                  + (decompose.RequireNoComposites ? "noComposite" : "keep"));
                        break;
                    case BottleProducedRule bottle:
                        parts.Add($"bottle:{ElementDefs.Name(bottle.TargetElement)}{bottle.RequiredCount}");
                        break;
                    case OnlyTheseElementsRule only:
                        parts.Add($"only:{Names(only.Allowed)}");
                        break;
                    case EqualElementCountsRule equal:
                        parts.Add($"equal:{equal.Tolerance}");
                        break;
                    default:
                        parts.Add("?" + rule.GetType().Name);
                        break;
                }
            }

            // 瓶子那种是「5 个组件」，逗号连起来更可读
            return string.Join(",", parts);
        }

        static string Names(ElementMask mask)
        {
            var names = new List<string>();
            foreach (var e in ElementDefs.All)
                if (mask.Has(e)) names.Add(ElementDefs.Name(e));
            return string.Concat(names);
        }

        // ---------------------------------------------------------------- 目录接线

        [Test]
        public void 所有正式关卡都开着空格召唤()
        {
            // 空格召唤（元素牌打到空格 = 在那里召唤一只该元素的小怪）是**第一章就教过的基础操作**。
            // 逐关把它关掉，玩家会以为「功能丢了」，而且关了之后没有任何替代手段去主动摆局。
            // 所以定成硬约定：正式关卡一律 Summon。哪一关真的需要关，必须在这里写清楚原因。
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            Assert.IsNotNull(catalog, "找不到 " + RealCatalogPath);

            foreach (var level in catalog.levels)
            {
                Assert.IsNotNull(level, "目录里有空槽位");
                Assert.AreEqual(EmptyCellCardMode.Summon, level.rules.EmptyCellCard,
                    level.levelId + " 关掉了空格召唤 —— 正式关卡一律打开（见这条用例的注释）");
            }
        }

        [Test]
        public void 关卡目录一共20关_第四章排在最后()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(RealCatalogPath);
            Assert.IsNotNull(catalog, "找不到 " + RealCatalogPath);
            Assert.AreEqual(20, catalog.levels.Length, "第一~四章共 20 关");

            var ids = catalog.levels.Select(l => l.levelId).ToList();
            CollectionAssert.AreEqual(LevelIds, ids.GetRange(15, 5), "第四章 5 关要按顺序排在目录最后");
            CollectionAssert.AllItemsAreUnique(ids, "levelId 不能重复（进度存档按它走）");
        }

        [Test]
        public void 每一章绑的关卡都属于这一章()
        {
            var chapters = AssetDatabase.LoadAssetAtPath<ChapterCatalog>(RealChapterPath);
            Assert.IsNotNull(chapters, "找不到 " + RealChapterPath);
            Assert.AreEqual(4, chapters.chapters.Length, "现在有四章");

            for (int c = 0; c < chapters.chapters.Length; c++)
            {
                string prefix = "ch" + (c + 1) + "_";
                foreach (var entry in chapters.chapters[c].levels)
                {
                    Assert.IsNotNull(entry.level,
                        $"第 {c + 1} 章的「{entry.title}」还没绑关卡定义");
                    StringAssert.StartsWith(prefix, entry.level.levelId,
                        $"第 {c + 1} 章的「{entry.title}」绑到了别的章的关卡上（{entry.level.levelId}）"
                        + " —— 菜单里点进去会进错关，肉眼很难发现");
                }
            }
        }
    }
}
