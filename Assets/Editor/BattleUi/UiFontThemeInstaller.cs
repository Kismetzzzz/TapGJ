using System.IO;
using System.Linq;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 字体配置的编辑器入口与文字绑定工具。
    /// 日常换字体使用 OpenTheme；场景创建工具通过 BindText 接入新文字。
    /// </summary>
    public static class UiFontThemeInstaller
    {
        public const string ThemePath = "Assets/UI/Data/UiFontTheme.asset";
        static readonly string[] ScenePaths = { "Assets/Scenes/MainMenu.unity", BattleUiSceneBuilder.ScenePath };

        /// <summary>复用已存在的配置；首次创建时留空槽位，保留各处原有字体。</summary>
        public static UiFontTheme EnsureTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UiFontTheme>(ThemePath);
            // 已有配置可能包含用户选好的字体，不能在重复接入时重置。
            if (theme != null) return theme;
            Directory.CreateDirectory("Assets/UI/Data");
            theme = ScriptableObject.CreateInstance<UiFontTheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
            return theme;
        }

        /// <summary>选中共享配置，并为当前打开的 UI 场景补齐缺少的绑定。</summary>
        [MenuItem("TapGJ/打开 UI 字体配置", false, 6)]
        public static void OpenTheme()
        {
            // 场景在外部更新后仍开着时，直接补上绑定，保留当前未保存的编辑内容。
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !ScenePaths.Contains(scene.path)) continue;
                foreach (var text in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Text>(true)))
                    if (text.GetComponent<UiFontBinding>() == null) BindText(text, RoleFor(text), !Application.isPlaying);
            }
            Selection.activeObject = EnsureTheme();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        /// <summary>保存文字与配置的关联；从菜单补加组件时支持撤销。</summary>
        public static void BindText(Text text, UiFontRole role, bool recordUndo = false)
        {
            var binding = text.GetComponent<UiFontBinding>();
            if (binding == null) binding = recordUndo ? Undo.AddComponent<UiFontBinding>(text.gameObject)
                : text.gameObject.AddComponent<UiFontBinding>();
            // 通过序列化属性绑定，确保关联能随场景或 Prefab 保存。
            var data = new SerializedObject(binding);
            data.FindProperty("theme").objectReferenceValue = EnsureTheme();
            data.FindProperty("role").enumValueIndex = (int)role;
            data.ApplyModifiedPropertiesWithoutUndo();
            binding.Refresh();
        }

        // 按现有层级中的文字名称分组；新加的普通文字默认使用正文。
        static UiFontRole RoleFor(Text text)
        {
            if (text.name == "Title" || text.name == "Heading") return UiFontRole.Title;
            // 只把纯数值控件归到数字组，RankHeader 等中文标题继续使用正文。
            if (text.name == "Time" || text.name == "Number" || text.name == "Rank"
                || text.name == "Duration") return UiFontRole.Number;
            return UiFontRole.Body;
        }
    }
}
