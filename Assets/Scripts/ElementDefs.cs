using UnityEngine;

namespace TapGJ.Core
{
    /// <summary>五行基础元素。顺序即枚举值顺序，反应表 / 瓶子 / 卡牌都按这个顺序索引。</summary>
    public enum Element
    {
        Metal = 0, // 金
        Wood = 1,  // 木
        Water = 2, // 水
        Fire = 3,  // 火
        Earth = 4, // 土
    }

    public static class ElementDefs
    {
        public const int Count = 5;

        public static readonly Element[] All =
        {
            Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth,
        };

        static readonly string[] Names = { "金", "木", "水", "火", "土" };

        /// <summary>棋盘上的配色（格子底色 / 小怪颜色 / HUD 都用它）。</summary>
        static readonly UnityEngine.Color[] Palette =
        {
            new UnityEngine.Color(0.72f, 0.76f, 0.80f), // 金
            new UnityEngine.Color(0.30f, 0.69f, 0.31f), // 木
            new UnityEngine.Color(0.18f, 0.53f, 0.87f), // 水
            new UnityEngine.Color(0.91f, 0.30f, 0.24f), // 火
            new UnityEngine.Color(0.79f, 0.63f, 0.40f), // 土
        };

        public static string Name(Element e) => Names[(int)e];

        /// <summary>
        /// 元素配色。
        /// 注意：本类里有一个叫 Color 的方法，所以类型必须写全 UnityEngine.Color，
        /// 否则 new Color(...) 会被解析成方法调用。
        /// </summary>
        public static UnityEngine.Color Tint(Element e) => Palette[(int)e];

        static Texture2D _white;

        /// <summary>1x1 白色贴图，运行时按需生成，避免依赖任何美术资源。</summary>
        public static Texture2D White
        {
            get
            {
                if (_white != null) return _white;
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, UnityEngine.Color.white);
                _white.Apply();
                return _white;
            }
        }
    }
}
