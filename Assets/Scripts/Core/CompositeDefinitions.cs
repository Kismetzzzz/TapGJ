using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 五种**复合元素**（图2 上半部分那张表）。
    ///
    /// 复合元素不属于 <see cref="Element"/>（五行）——它是独立的一类东西：
    ///   · 它占棋盘上的一格，但**不会移动**（准备阶段不动），也不能被元素牌打
    ///   · 它不会被计入任何「元素数量 / 元素种类」统计
    ///   · 它每回合「产生」自己的两种基础元素各若干份，**产生出来的元素才会**按图2 的表和周围反应
    ///
    /// 配方用的是**相生链**（金→水→木→火→土→金），和图2 表里括号写的两种元素一一对应。
    /// </summary>
    public enum CompositeKind
    {
        /// <summary>烬（木 + 火）：范围内生产 木+4、火+4。</summary>
        Ember = 0,

        /// <summary>熔岩（火 + 土）：范围内生产 火+4、土+4。</summary>
        Lava = 1,

        /// <summary>矿晶（土 + 金）：范围内生产 土+4、金+4。</summary>
        Ore = 2,

        /// <summary>寒钢（金 + 水）：范围内生产 金+4、水+4。</summary>
        ColdSteel = 3,

        /// <summary>灵泉（水 + 木）：范围内生产 水+4、木+4。</summary>
        SpiritSpring = 4,
    }

    /// <summary>一种复合元素的静态信息：名字、两个组成元素、配色。</summary>
    public readonly struct CompositeDef
    {
        public readonly CompositeKind Kind;
        /// <summary>括号里写的两个基础元素，顺序固定（就是下表「生产」那一列的顺序）。</summary>
        public readonly Element A;
        public readonly Element B;
        public readonly string Name;
        public readonly string ShortName;
        public readonly UnityEngine.Color Tint;

        internal CompositeDef(CompositeKind kind, Element a, Element b, string name, string shortName,
            UnityEngine.Color tint)
        {
            Kind = kind;
            A = a;
            B = b;
            Name = name;
            ShortName = shortName;
            Tint = tint;
        }

        public Element[] Ingredients => new[] { A, B };
    }

    public static class CompositeDefs
    {
        public const int Count = 5;

        static readonly CompositeDef[] Defs =
        {
            // 配色取两种组成元素的中和色，和五行的配色一眼能区分开（更亮、更饱和）。
            new CompositeDef(CompositeKind.Ember, Element.Wood, Element.Fire, "烬", "烬",
                new UnityEngine.Color(0.86f, 0.50f, 0.20f)),
            new CompositeDef(CompositeKind.Lava, Element.Fire, Element.Earth, "熔岩", "岩",
                new UnityEngine.Color(0.95f, 0.42f, 0.18f)),
            new CompositeDef(CompositeKind.Ore, Element.Earth, Element.Metal, "矿晶", "晶",
                new UnityEngine.Color(0.70f, 0.66f, 0.36f)),
            new CompositeDef(CompositeKind.ColdSteel, Element.Metal, Element.Water, "寒钢", "钢",
                new UnityEngine.Color(0.42f, 0.72f, 0.86f)),
            new CompositeDef(CompositeKind.SpiritSpring, Element.Water, Element.Wood, "灵泉", "泉",
                new UnityEngine.Color(0.34f, 0.82f, 0.62f)),
        };

        public static readonly CompositeKind[] All =
        {
            CompositeKind.Ember, CompositeKind.Lava, CompositeKind.Ore,
            CompositeKind.ColdSteel, CompositeKind.SpiritSpring,
        };

        public static CompositeDef Of(CompositeKind kind) => Defs[(int)kind];

        public static string Name(CompositeKind kind) => Defs[(int)kind].Name;

        public static string ShortName(CompositeKind kind) => Defs[(int)kind].ShortName;

        public static UnityEngine.Color Tint(CompositeKind kind) => Defs[(int)kind].Tint;

        public static Element[] Ingredients(CompositeKind kind) => Defs[(int)kind].Ingredients;
    }

    /// <summary>
    /// 融合表：哪两种基础元素能合成哪种复合元素。
    /// 就是图2 的那 5 行，顺序 **A→B 沿相生链**（金→水→木→火→土→金）：
    ///   金 + 水 = 寒钢、水 + 木 = 灵泉、木 + 火 = 烬、火 + 土 = 熔岩、土 + 金 = 矿晶。
    /// 融合牌里的「元素1 / 元素2」不分先后（寒钢 既接受 金→水，也接受 水→金）。
    /// </summary>
    public static class FusionRecipes
    {
        /// <summary>能不能用这两种基础元素合成复合元素（顺序无关，同元素不行）。</summary>
        public static bool CanFuse(Element a, Element b) => TryGetKind(a, b, out _);

        /// <summary>查这两种基础元素合成的复合元素；不能合成时返回 false。</summary>
        public static bool TryGetKind(Element a, Element b, out CompositeKind kind)
        {
            kind = default;
            if (a == b) return false;

            for (int i = 0; i < CompositeDefs.Count; i++)
            {
                var def = CompositeDefs.Of((CompositeKind)i);
                if ((def.A == a && def.B == b) || (def.A == b && def.B == a))
                {
                    kind = def.Kind;
                    return true;
                }
            }
            return false;
        }

        /// <summary>本关的元素池里有没有**至少一对**能合成的组合（决定融合牌是不是死牌）。</summary>
        public static bool AnyPairAvailable(ElementMask pool)
        {
            var elements = pool.ToArray();
            for (int i = 0; i < elements.Length; i++)
                for (int j = i + 1; j < elements.Length; j++)
                    if (CanFuse(elements[i], elements[j])) return true;
            return false;
        }

        /// <summary>能和这个元素配对的另一元素（用于面板说明）。没有就返回 null。</summary>
        public static List<(Element partner, CompositeKind kind)> PartnersOf(Element e)
        {
            var list = new List<(Element, CompositeKind)>();
            for (int i = 0; i < CompositeDefs.Count; i++)
            {
                var def = CompositeDefs.Of((CompositeKind)i);
                if (def.A == e) list.Add((def.B, def.Kind));
                else if (def.B == e) list.Add((def.A, def.Kind));
            }
            return list;
        }
    }
}
