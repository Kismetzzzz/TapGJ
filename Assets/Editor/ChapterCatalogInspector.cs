using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.EditorTools
{
    [CustomEditor(typeof(ChapterCatalog))]
    public sealed class ChapterCatalogInspector : UnityEditor.Editor
    {
        public sealed class BindingStatus
        {
            public string location;
            public LevelDefinition definition;
            public readonly List<string> issues = new List<string>();
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("展开 Chapters → Levels，将关卡定义 .asset 拖入 Definition。留空表示待开放；Title 和 Goal 是卡片展示文案。", MessageType.Info);
            DrawDefaultInspector();

            var bindings = CheckBindings((ChapterCatalog)target, EditorBuildSettings.scenes);
            int bound = bindings.Count(b => b.definition != null);
            int problemCount = bindings.Count(b => b.issues.Count > 0);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("关卡绑定检查", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox($"共 {bindings.Count} 个关卡条目 · 已绑定 {bound} · 待开放 {bindings.Count - bound} · 有配置问题 {problemCount}",
                problemCount > 0 ? MessageType.Warning : MessageType.Info);

            foreach (var binding in bindings)
            {
                if (binding.definition == null) continue;
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(binding.location, EditorStyles.wordWrappedLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.ObjectField(binding.definition, typeof(LevelDefinition), false);
                    if (GUILayout.Button("查看关卡", GUILayout.Width(72)))
                    {
                        Selection.activeObject = binding.definition;
                        EditorGUIUtility.PingObject(binding.definition);
                    }
                }
                foreach (string issue in binding.issues)
                    EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }
        }

        // 只读检查；场景列表由调用方提供，测试无需改动项目构建设置。
        public static List<BindingStatus> CheckBindings(ChapterCatalog catalog, EditorBuildSettingsScene[] scenes)
        {
            var result = new List<BindingStatus>();
            if (catalog == null || catalog.chapters == null) return result;
            var ids = new Dictionary<string, BindingStatus>(StringComparer.Ordinal);
            for (int c = 0; c < catalog.chapters.Length; c++)
            {
                var chapter = catalog.chapters[c];
                if (chapter?.levels == null) continue;
                for (int l = 0; l < chapter.levels.Length; l++)
                {
                    var entry = chapter.levels[l];
                    if (entry == null) continue;
                    var binding = new BindingStatus
                    {
                        location = $"章节 {c + 1}「{chapter.title}」 / 关卡 {l + 1}「{entry.title}」",
                        definition = entry.definition
                    };
                    result.Add(binding);
                    var definition = binding.definition;
                    if (definition == null) continue;

                    definition.CollectAuthoringIssues(binding.issues);
                    if (string.IsNullOrWhiteSpace(definition.sceneName))
                        binding.issues.Add("Scene Name 留空了。请在关卡资产中指定游戏场景，例如 Game。");
                    else if (scenes == null || !scenes.Any(s => s != null && s.enabled && MatchesScene(s.path, definition.sceneName)))
                        binding.issues.Add($"场景「{definition.sceneName}」未加入或未启用。请在 File → Build Settings 中添加并勾选对应场景。");

                    if (string.IsNullOrWhiteSpace(definition.levelId)) continue;
                    if (ids.TryGetValue(definition.levelId, out var first))
                    {
                        if (first.definition != definition)
                            binding.issues.Add($"Level Id「{definition.levelId}」与 {first.location} 的另一份资产重复，会共用通关记录。请为不同关卡设置唯一 ID。");
                    }
                    else ids.Add(definition.levelId, binding);
                }
            }
            return result;
        }

        static bool MatchesScene(string path, string sceneName)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            sceneName = sceneName.Replace('\\', '/');
            string withoutExtension = path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - 6) : path;
            return string.Equals(path, sceneName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(withoutExtension, sceneName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
