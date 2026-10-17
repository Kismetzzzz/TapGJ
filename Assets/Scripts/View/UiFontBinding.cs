using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>
    /// 将一个 Text 接到共享字体配置，编辑器预览与运行时采用相同规则。
    /// 只修改 Font，保留文字内容、字号、颜色以及所属按钮的交互。
    /// </summary>
    // ponytail: 统一现有 uGUI Text；其他文字组件可接入对应绑定。
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Text))]
    public sealed class UiFontBinding : MonoBehaviour
    {
        // 所有需要一起换字体的文字都引用同一份资产。
        [SerializeField, InspectorName("字体配置")] UiFontTheme theme;
        [SerializeField, InspectorName("文字用途")] UiFontRole role;
        Text _text;
        public UiFontTheme Theme => theme;
        public UiFontRole Role => role;

        // 隐藏面板重新打开、或模板复制出新卡片时，立即读取最新配置。
        void OnEnable() => Refresh();
        // 每帧比较字体引用，兼容运行时直接修改配置；字体相同时不会重复写入 Text。
        void LateUpdate() => Refresh();

        /// <summary>为新创建的文字指定共享配置与用途，并立即应用。</summary>
        public void Configure(UiFontTheme fontTheme, UiFontRole fontRole)
        {
            theme = fontTheme;
            role = fontRole;
            Refresh();
        }

        /// <summary>应用当前用途的字体；配置缺失时保留已有显示。</summary>
        public void Refresh()
        {
            // 缓存同物体上的文字组件，后续更新不必反复查找。
            if (_text == null) _text = GetComponent<Text>();
            var font = theme != null ? theme.GetFont(role) : null;
            // 正文暂未配置时保留现有字体，避免文字消失。
            if (font != null && _text.font != font) _text.font = font;
        }
    }
}
