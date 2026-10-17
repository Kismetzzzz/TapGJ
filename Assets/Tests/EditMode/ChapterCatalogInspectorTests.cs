using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.EditorTools;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.Tests
{
    public class ChapterCatalogInspectorTests
    {
        readonly List<Object> _created = new List<Object>();
        static EditorBuildSettingsScene[] EnabledScenes => new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Game.unity", true)
        };

        T Create<T>() where T : ScriptableObject
        {
            var value = ScriptableObject.CreateInstance<T>();
            _created.Add(value);
            return value;
        }

        LevelDefinition Level(string id = "test_level")
        {
            var value = Create<LevelDefinition>();
            value.levelId = id;
            value.displayName = "测试关卡";
            return value;
        }

        ChapterCatalog Catalog(params LevelDefinition[] definitions)
        {
            var catalog = Create<ChapterCatalog>();
            catalog.chapters = new[]
            {
                new ChapterCatalog.Chapter
                {
                    title = "教学",
                    levels = definitions.Select((d, i) => new ChapterCatalog.Level
                    { title = "关卡 " + (i + 1), definition = d }).ToArray()
                }
            };
            return catalog;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var value in _created) Object.DestroyImmediate(value);
            _created.Clear();
        }

        [Test]
        public void 空条目跳过_未绑定正常待开放_保持配置位置()
        {
            var level = Level();
            var catalog = Catalog(level, null);
            var chapter = catalog.chapters[0];
            chapter.levels = new[] { null, chapter.levels[0], chapter.levels[1] };
            catalog.chapters = new[] { null, chapter, new ChapterCatalog.Chapter { levels = null } };
            var result = ChapterCatalogInspector.CheckBindings(catalog, EnabledScenes);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result.Count(b => b.definition != null));
            StringAssert.Contains("章节 2", result[0].location);
            StringAssert.Contains("关卡 2", result[0].location);
            Assert.IsEmpty(result.SelectMany(b => b.issues));
            catalog.chapters = null;
            Assert.IsEmpty(ChapterCatalogInspector.CheckBindings(catalog, EnabledScenes));
        }

        [TestCase("Game")]
        [TestCase("Assets/Scenes/Game.unity")]
        [TestCase("Assets/Scenes/Game")]
        [TestCase("game")]
        public void 已启用场景_接受场景名及完整路径(string scene)
        {
            var level = Level();
            level.sceneName = scene;
            Assert.IsEmpty(ChapterCatalogInspector.CheckBindings(Catalog(level), EnabledScenes)[0].issues);
        }

        [TestCase("")]
        [TestCase(" ")]
        [TestCase("Missing")]
        public void 空或不存在的场景_给出修复提示(string scene)
        {
            var level = Level();
            level.sceneName = scene;
            var issues = ChapterCatalogInspector.CheckBindings(Catalog(level), EnabledScenes)[0].issues;
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains(string.IsNullOrWhiteSpace(scene) ? "Scene Name" : "Build Settings", issues[0]);
        }

        [Test]
        public void 未启用场景_不当作可以进入()
        {
            var scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Game.unity", false) };
            var issues = ChapterCatalogInspector.CheckBindings(Catalog(Level()), scenes)[0].issues;
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("未启用", issues[0]);
        }

        [Test]
        public void 不同资产同ID_提示冲突位置_同一资产复用不误报()
        {
            var first = Level();
            var second = Level();
            var catalog = Catalog(first, first, second);
            var result = ChapterCatalogInspector.CheckBindings(catalog, EnabledScenes);
            Assert.IsEmpty(result[0].issues);
            Assert.IsEmpty(result[1].issues);
            Assert.AreEqual(1, result[2].issues.Count);
            StringAssert.Contains("Level Id", result[2].issues[0]);
            StringAssert.Contains(result[0].location, result[2].issues[0]);
            second.levelId = "another_level";
            Assert.IsEmpty(ChapterCatalogInspector.CheckBindings(catalog, EnabledScenes).SelectMany(b => b.issues));
        }

        [Test]
        public void 复用关卡校验_展示自定义目标缺少机制_检查不修改资产()
        {
            var level = Level();
            level.rules.Goal = GoalSpec.Custom();
            var before = EditorJsonUtility.ToJson(level);
            var issues = ChapterCatalogInspector.CheckBindings(Catalog(level), EnabledScenes)[0].issues;
            Assert.IsTrue(issues.Any(s => s.Contains("自定义") && s.Contains("预制体")));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(level));
        }

        [Test]
        public void Inspector绑定_使用章节检查面板()
        {
            var catalog = Catalog(Level());
            var editor = UnityEditor.Editor.CreateEditor(catalog);
            try { Assert.IsInstanceOf<ChapterCatalogInspector>(editor); }
            finally { Object.DestroyImmediate(editor); }
        }
    }
}
