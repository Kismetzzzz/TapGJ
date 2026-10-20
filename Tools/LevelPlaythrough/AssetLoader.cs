using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TapGJ.Core;

namespace TapGJ.Playthrough
{
    /// <summary>一关的运行时数据：规则 + 布局 + 机制工厂 + 事件工厂。</summary>
    public sealed class LevelData
    {
        public string LevelId;
        public string DisplayName;
        public string Description;
        public string OutroText;
        public string AssetPath;

        public Rules Rules;
        public string LayoutText;
        public bool UseFixedSeed;
        public int FixedSeed;
        public bool UseEvents;

        public List<Func<ILevelRule>> RuleFactories = new List<Func<ILevelRule>>();
        public List<Func<IRoundEvent>> EventFactories = new List<Func<IRoundEvent>>();
        public List<string> MechanicNames = new List<string>();
        public List<string> EventNames = new List<string>();

        public CompositePlacement[] InitialComposites = new CompositePlacement[0];
        public Element[] FixedInitialHand = new Element[0];

        public LevelSetup Setup()
        {
            if (string.IsNullOrWhiteSpace(LayoutText)) return null;
            if (BoardLayoutParser.TryParse(LayoutText, Rules.BoardWidth, Rules.BoardHeight,
                    Rules.ElementPool, out var setup, out var error))
            {
                if (FixedInitialHand != null && FixedInitialHand.Length > 0)
                    setup.InitialHand = FixedInitialHand;
                return setup;
            }
            Console.WriteLine($"  [布局解析失败] {error}");
            return null;
        }
    }

    public static class AssetLoader
    {
        const string LevelDefinitionScriptGuid = "4baacca015d143941812e41b49f10709";
        const string LevelCatalogScriptGuid = "ed37b7f5c2b923a4cb5ddba980f94e2d";

        static readonly Dictionary<string, string> GuidToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static void IndexGuids(string assetsRoot)
        {
            GuidToPath.Clear();
            foreach (var meta in Directory.EnumerateFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
            {
                string guid = null;
                foreach (var line in File.ReadLines(meta).Take(6))
                {
                    if (!line.StartsWith("guid:", StringComparison.Ordinal)) continue;
                    guid = line.Substring(5).Trim();
                    break;
                }
                if (guid == null) continue;
                GuidToPath[guid] = meta.Substring(0, meta.Length - ".meta".Length);
            }
        }

        public static string PathOf(string guid)
        {
            if (guid == null) return null;
            return GuidToPath.TryGetValue(guid, out var p) ? p : null;
        }

        public static List<LevelData> LoadAll(string assetsRoot)
        {
            IndexGuids(assetsRoot);

            var catalogPath = Path.Combine(assetsRoot, "Levels", "LevelCatalog.asset");

            var docs = Yaml.Load(catalogPath);
            var catalog = docs.FirstOrDefault(d => d.Str("m_Script")?.Contains(LevelCatalogScriptGuid) == true)
                          ?? docs.FirstOrDefault();

            var levels = new List<LevelData>();
            var list = catalog.List("levels");
            if (list == null) return levels;

            foreach (var item in list)
            {
                var inline = item as string;
                var guid = YamlDoc.GuidOf(inline);
                var path = PathOf(guid);
                if (path == null || !File.Exists(path)) continue;

                var level = LoadLevel(path);
                if (level != null) levels.Add(level);
            }

            return levels;
        }

        public static LevelData LoadLevel(string path)
        {
            var doc = Yaml.Load(path)
                .FirstOrDefault(d => d.Str("m_Script")?.Contains(LevelDefinitionScriptGuid) == true);
            if (doc == null) return null;

            var level = new LevelData
            {
                LevelId = doc.Str("levelId"),
                DisplayName = doc.Str("displayName"),
                Description = doc.Str("description"),
                OutroText = doc.Str("outroText"),
                AssetPath = path,
                LayoutText = doc.Str("layoutText"),
                UseFixedSeed = doc.Bool("useFixedSeed"),
                FixedSeed = doc.Int("fixedSeed", 20240607),
                UseEvents = doc.Bool("useEvents"),
            };

            level.Rules = BuildRules(doc.Map("rules"));

            // 开局就摆在棋盘上的复合元素（LevelLoader 会把它们拷进 rules.InitialComposites）
            var composites = doc.List("initialComposites");
            if (composites != null)
            {
                var list = new List<CompositePlacement>();
                foreach (var item in composites)
                {
                    if (item is not Dictionary<string, object> m) continue;
                    int Get(string k) => m.TryGetValue(k, out var v) && int.TryParse(v as string, out var n) ? n : 0;
                    list.Add(new CompositePlacement(Get("X"), Get("Y"), (CompositeKind)Get("Kind")));
                }
                level.InitialComposites = list.ToArray();
            }
            level.Rules.InitialComposites = level.InitialComposites;

            // 固定起手牌
            var hand = doc.List("fixedInitialHand");
            if (hand != null)
            {
                var list = new List<Element>();
                foreach (var item in hand)
                    if (item is string s && int.TryParse(s, out var n)) list.Add((Element)n);
                level.FixedInitialHand = list.ToArray();
            }

            // 机制：关卡预制体上启用的组件
            var prefabInline = doc.Str("levelPrefab");
            var prefabGuid = YamlDoc.GuidOf(prefabInline);
            var prefabPath = PathOf(prefabGuid);
            if (prefabPath != null && File.Exists(prefabPath))
            {
                foreach (var comp in Yaml.Load(prefabPath))
                {
                    if (comp.ClassTag != "!u!114") continue;
                    if (!comp.Bool("m_Enabled", true)) continue;

                    var scriptGuid = YamlDoc.GuidOf(comp.Str("m_Script"));
                    var factory = MechanicFactory.Create(scriptGuid, comp);
                    if (factory == null) continue;

                    level.RuleFactories.Add(factory);
                    level.MechanicNames.Add(MechanicFactory.NameOf(scriptGuid));
                }
            }

            // 事件池
            var events = doc.List("roundEvents");
            if (events != null)
            {
                foreach (var item in events)
                {
                    var guid = YamlDoc.GuidOf(item as string);
                    var p = PathOf(guid);
                    if (p == null || !File.Exists(p)) continue;

                    var evDoc = Yaml.Load(p).FirstOrDefault();
                    if (evDoc == null) continue;

                    var evFactory = EventFactory.Create(YamlDoc.GuidOf(evDoc.Str("m_Script")), evDoc);
                    if (evFactory == null) continue;

                    level.EventFactories.Add(evFactory);
                    level.EventNames.Add(evDoc.Str("displayName"));
                }
            }

            return level;
        }

        static Rules BuildRules(Dictionary<string, object> map)
        {
            var rules = new Rules();
            if (map == null) return rules;

            foreach (var field in typeof(Rules).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!map.TryGetValue(field.Name, out var raw) || raw == null) continue;

                if (field.FieldType == typeof(int) && raw is string s && int.TryParse(s, out var i))
                {
                    field.SetValue(rules, i);
                }
                else if (field.FieldType == typeof(bool) && raw is string b)
                {
                    field.SetValue(rules, b == "1");
                }
                else if (field.FieldType.IsEnum && raw is string e && int.TryParse(e, out var n))
                {
                    field.SetValue(rules, Enum.ToObject(field.FieldType, n));
                }
                else if (field.FieldType == typeof(GoalSpec) && raw is Dictionary<string, object> goal)
                {
                    var spec = new GoalSpec();
                    if (goal.TryGetValue("Kind", out var k) && int.TryParse(k as string, out var kind))
                        spec.Kind = (GoalKind)kind;
                    if (goal.TryGetValue("Target", out var t) && int.TryParse(t as string, out var target))
                        spec.Target = (Element)target;
                    if (goal.TryGetValue("TargetCount", out var c) && int.TryParse(c as string, out var count))
                        spec.TargetCount = count;
                    if (goal.TryGetValue("Rounds", out var r) && int.TryParse(r as string, out var rounds))
                        spec.Rounds = rounds;
                    field.SetValue(rules, spec);
                }
            }

            // 与 LevelLoader 一致：旧数据留空的池按「全部」处理（Validate 也会修，这里显式对齐）
            rules.Validate();
            return rules;
        }
    }
}
