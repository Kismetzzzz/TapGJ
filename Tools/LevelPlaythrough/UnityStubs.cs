// 让纯 C# 的 Core 能在 Unity 之外编译：只补它真正用到的那一点点 UnityEngine 表面。
// 注意：Assets/Scripts/ElementDefs.cs 也定义了 Element / ElementDefs，但它依赖 Texture2D 等，
// 所以本工程不编译那个文件，改由这里提供等价的最小实现（枚举值、顺序、名字必须与它一致）。

namespace UnityEngine
{
    public struct Color
    {
        public float r, g, b, a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1f, 1f, 1f, 1f);
    }
}

namespace TapGJ.Core
{
    /// <summary>与 Assets/Scripts/ElementDefs.cs 中的枚举逐位一致。</summary>
    public enum Element
    {
        Metal = 0,
        Wood = 1,
        Water = 2,
        Fire = 3,
        Earth = 4,
    }

    public static class ElementDefs
    {
        public const int Count = 5;

        public static readonly Element[] All =
        {
            Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth,
        };

        static readonly string[] Names = { "金", "木", "水", "火", "土" };

        static readonly UnityEngine.Color[] Palette =
        {
            new UnityEngine.Color(0.72f, 0.76f, 0.80f),
            new UnityEngine.Color(0.30f, 0.69f, 0.31f),
            new UnityEngine.Color(0.18f, 0.53f, 0.87f),
            new UnityEngine.Color(0.91f, 0.30f, 0.24f),
            new UnityEngine.Color(0.79f, 0.63f, 0.40f),
        };

        public static string Name(Element e) => Names[(int)e];

        public static UnityEngine.Color Tint(Element e) => Palette[(int)e];
    }
}
