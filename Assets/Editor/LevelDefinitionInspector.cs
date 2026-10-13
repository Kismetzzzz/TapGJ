using System.Collections.Generic;
using TapGJ.Levels;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 关卡资产的 Inspector：把「这一关能不能玩」直接摆在编辑器里。
    ///
    /// 设计师改 layoutText / 元素池 / 目标的时候，问题列表会实时跟着变，
    /// 不用 Play 才知道哪里配错了。检查逻辑和运行时是同一套
    /// （<see cref="LevelDefinition.CollectAuthoringIssues"/>）。
    /// </summary>
    [CustomEditor(typeof(LevelDefinition))]
    public sealed class LevelDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var level = (LevelDefinition)target;

            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("这一关", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(level.Summary(), MessageType.None);

            DrawCompositeShortcut(level);
            DrawIssues(level);
            DrawActions(level);
        }

        /// <summary>
        /// 复合元素的一键开关。
        ///
        /// `rules` 是嵌套的序列化类，默认 Inspector 画不出「勾了开关顺便把秘术牌也配上」这种联动，
        /// 所以这里补一个按钮：勾上总开关 + 用一套能玩的默认配比。细调仍然去 rules 里改。
        /// </summary>
        static void DrawCompositeShortcut(LevelDefinition level)
        {
            var rules = level.rules;
            if (rules == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (rules.UseCompositeElements)
            {
                if (GUILayout.Button("关掉复合元素（回到纯五行玩法）", GUILayout.Height(22f)))
                {
                    rules.UseCompositeElements = false;
                    rules.InitialComposites = new TapGJ.Core.CompositePlacement[0];
                    MarkDirty(level);
                }
                GUILayout.Label($"当前：{LevelDefinition.CompositeFeatureText(rules)}", EditorStyles.miniLabel);
            }
            else
            {
                if (GUILayout.Button("启用复合元素（用默认配比）", GUILayout.Height(22f)))
                {
                    rules.UseCompositeElements = true;
                    if (rules.SpecialCardPool == TapGJ.Core.SpecialCardMask.None)
                        rules.SpecialCardPool = TapGJ.Core.SpecialCardMask.All;
                    MarkDirty(level);
                }
                GUILayout.Label("当前：关（这一关没有复合元素与秘术牌）", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 改完字段要标脏。注意 <see cref="LevelDefinition"/> 是 **ScriptableObject**，
        /// 没有 `gameObject.scene` 可以标脏场景（那是 MonoBehaviour 的写法）。
        /// 这里走 SerializedObject，让 Inspector 立刻刷新，并且能被撤销（Ctrl+Z）。
        /// </summary>
        static void MarkDirty(LevelDefinition level)
        {
            EditorUtility.SetDirty(level);
            var so = new SerializedObject(level);
            so.ApplyModifiedProperties();
        }

        static void DrawIssues(LevelDefinition level)
        {
            var issues = new List<string>();
            level.CollectAuthoringIssues(issues);

            EditorGUILayout.Space();
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("✔ 没发现配置问题。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"⚠ 发现 {issues.Count} 个问题", EditorStyles.boldLabel);
            foreach (var issue in issues)
                EditorGUILayout.HelpBox(issue, MessageType.Error);
        }

        static void DrawActions(LevelDefinition level)
        {
            EditorGUILayout.Space();

            var loader = Object.FindFirstObjectByType<LevelLoader>();
            using (new EditorGUI.DisabledScope(loader == null || Application.isPlaying))
            {
                if (GUILayout.Button(loader == null
                        ? "Play 这一关（当前场景里没有 LevelLoader）"
                        : "Play 这一关（写进 LevelLoader.inlineLevel 后进入 Play）"))
                {
                    SetRef(loader, "inlineLevel", level);
                    EditorSceneManager.SaveOpenScenes();
                    EditorApplication.EnterPlaymode();
                }
            }

            using (new EditorGUI.DisabledScope(loader == null))
            {
                if (GUILayout.Button("清空 inlineLevel（回到目录第一关）"))
                {
                    SetRef(loader, "inlineLevel", null);
                    EditorSceneManager.MarkSceneDirty(loader.gameObject.scene);
                }
            }

            if (loader != null && loader.CurrentLevel == level && Application.isPlaying)
                EditorGUILayout.HelpBox($"正在玩的就是这一关（seed {loader.CurrentSeed}）。", MessageType.None);
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogError($"[TapGJ] {target.GetType().Name} 上找不到字段 {field}（被改名了？）");
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
