using System;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>文字用途只决定使用哪个字体槽位，字号和颜色仍由 Text 自己控制。</summary>
    public enum UiFontRole { Body, Title, Number }

    /// <summary>
    /// 主菜单、局内 UI 和卡片共用的字体资产。
    /// 日常换字体只修改这份资产，不需要逐个修改场景中的 Text。
    /// </summary>
    [CreateAssetMenu(menuName = "TapGJ/UI 字体配置", fileName = "UiFontTheme")]
    public sealed class UiFontTheme : ScriptableObject
    {
        [InspectorName("系统正文字体"), Tooltip("已安装的字体族名称，例如 KaiTi。找到时优先使用；未安装或留空时使用正文字体文件。")]
        public string systemBodyFont;
        [InspectorName("界面正文"), Tooltip("按钮、任务说明和普通界面文字；也作为系统字体未安装时的回退。暂时留空则保留原字体。")]
        public Font body;
        [InspectorName("大标题"), Tooltip("留空时跟随界面正文。")]
        public Font title;
        [InspectorName("数字"), Tooltip("计时、排行和章节序号；留空时跟随界面正文。")]
        public Font number;
        Font _systemFont;
        string _resolvedSystemFontName;

        /// <summary>
        /// 标题和数字没有单独指定字体时，使用正文字体。
        /// 返回 null 表示没有可用配置，由绑定组件保留 Text 已有的字体。
        /// </summary>
        public Font GetFont(UiFontRole role)
        {
            switch (role)
            {
                case UiFontRole.Title: return title != null ? title : ResolveBodyFont();
                case UiFontRole.Number: return number != null ? number : ResolveBodyFont();
                default: return ResolveBodyFont();
            }
        }

        Font ResolveBodyFont()
        {
            string requested = systemBodyFont?.Trim() ?? "";
            if (_resolvedSystemFontName != requested)
            {
                ReleaseSystemFont();
                _resolvedSystemFontName = requested;
                if (requested.Length > 0 && Array.Exists(Font.GetOSInstalledFontNames(),
                    name => string.Equals(name, requested, StringComparison.OrdinalIgnoreCase)))
                {
                    _systemFont = Font.CreateDynamicFontFromOSFont(requested, 16);
                    if (_systemFont != null) _systemFont.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            return _systemFont != null ? _systemFont : body;
        }

        void ReleaseSystemFont()
        {
            if (_systemFont != null)
            {
                if (Application.isPlaying) Destroy(_systemFont);
                else DestroyImmediate(_systemFont);
            }
            _systemFont = null;
            _resolvedSystemFontName = null;
        }

        void OnDisable() => ReleaseSystemFont();

#if UNITY_EDITOR
        // 修改资产后主动请求编辑器更新，让绑定组件在未进入 Play 时也能显示新字体。
        void OnValidate() => UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
    }
}
