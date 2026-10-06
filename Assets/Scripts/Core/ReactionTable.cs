using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 图2 的「元素小怪作用」表。原表按五行逐行书写，共 25 条：
    /// 5 条同元素（不发生反应，只禁止移动），其余 20 条是 10 对跨元素组合。
    ///
    /// ── 统一读法（已与设计者逐句确认）──
    /// 原表只有两种句式，正好对应五行相生/相克两套机制：
    ///
    ///   「与 X 相遇 X元素+1」   → **相生**：输的那个**就地变成** X 元素（格子占用不变）
    ///   「与 X 相遇，该 Y 元素消失」→ **相克**：Y 直接消失（格子空出来）
    ///
    /// 所以「被点名的那个元素」要么是相生里生出来的那个，要么是相克里被克掉的那个：
    ///   相生链：金→水→木→火→土→金
    ///   相克链：金克木、木克土、土克水、水克火、火克金
    ///
    /// 10 对全部落在相生/相克上，每行每列都是 2 胜 2 负（不会出现某个元素一家独大）。
    /// ⚠ 声明一对时**必须传「谁输」**（见 Generate / Overcome），这样写错方向一眼能看出来 ——
    ///    之前用「传赢家」的写法时，金×火 那一对被写反过，而且测试和注释跟着一起错。
    /// </summary>
    public sealed class ReactionTable
    {
        readonly ReactionDef[,] _table = new ReactionDef[ElementDefs.Count, ElementDefs.Count];

        public ReactionTable()
        {
            // ────────────── 相生（原表「X元素+1」句式）：输家变成赢家元素 ──────────────

            // 金行「与水牌相遇水元素+1」 → 金变水
            Generate(Element.Metal, Element.Water);
            // 木行「与水牌相遇木元素+1」 → 水变木
            Generate(Element.Water, Element.Wood);
            // 木行「与火牌相遇火元素+1」 → 木变火
            Generate(Element.Wood, Element.Fire);
            // 火行「与土牌相遇土元素+1」 → 火变土
            Generate(Element.Fire, Element.Earth);
            // 金行「与土牌相遇金元素+1」 → 土变金
            Generate(Element.Earth, Element.Metal);

            // ────────────── 相克（原表「该Y元素消失」句式）：输家直接消失 ──────────────

            // 金行「与木元素相遇，该木元素消失」 → 金克木
            Overcome(Element.Wood, Element.Metal);
            // 金行「与火元素/火牌相遇，该金元素消失」 → 火克金
            Overcome(Element.Metal, Element.Fire);
            // 木行「与土元素相遇，该土元素消失」 → 木克土
            Overcome(Element.Earth, Element.Wood);
            // 水行「与火元素相遇，该火元素消失」 → 水克火
            Overcome(Element.Fire, Element.Water);
            // 水行「与土元素相遇，该水元素消失」 → 土克水
            Overcome(Element.Water, Element.Earth);

            // 同元素：相遇即禁止该次移动，不发生任何反应。
            for (int i = 0; i < ElementDefs.Count; i++)
                _table[i, i] = ReactionDef.NoReaction();
        }

        /// <summary>
        /// 相生：loser 就地变成 winner（等价于原表「与 winner 相遇 winner+1」）。
        /// 例：Generate(金, 水) 表示「金生水」——金怪遇到水时自己变成水。
        /// </summary>
        void Generate(Element loser, Element winner)
        {
            Write(loser, winner, ReactionDef.Transform(winner));
        }

        /// <summary>
        /// 相克：loser 直接消失，winner 留下（等价于原表「该 loser 元素消失」）。
        /// 例：Overcome(木, 金) 表示「金克木」——木怪遇到金时消失。
        /// </summary>
        void Overcome(Element loser, Element winner)
        {
            Write(loser, winner, ReactionDef.Annihilate(winner));
        }

        /// <summary>两个方向一起写：表是对称的，只看「谁输」就够。</summary>
        void Write(Element loser, Element winner, ReactionDef def)
        {
            if (loser == winner)
                throw new ArgumentException("相生/相克的两方必须是不同元素");

            _table[(int)loser, (int)winner] = def;
            _table[(int)winner, (int)loser] = def;
        }

        public ReactionDef Get(Element a, Element b) => _table[(int)a, (int)b];

        /// <summary>供 UI / 编辑器窗口列举规则用。</summary>
        public static List<(Element a, Element b, ReactionDef def)> AllRules()
        {
            var t = new ReactionTable();
            var list = new List<(Element, Element, ReactionDef)>();
            for (int i = 0; i < ElementDefs.Count; i++)
                for (int j = 0; j < ElementDefs.Count; j++)
                    list.Add(((Element)i, (Element)j, t.Get((Element)i, (Element)j)));
            return list;
        }
    }

    /// <summary>
    /// 一条元素反应的结算方式。
    /// 表里写的是「行元素」遇到「列元素」时会发生什么，因此这里的 Rule 描述的是
    /// 「主动撞上去的一方(trigger)遇到被撞上的一方(target)」的完整结局。
    /// </summary>
    public struct ReactionDef
    {
        public ReactionRule Rule;

        /// <summary>这一对里「赢」的元素：相生时输家会变成它，相克时它留下。</summary>
        public Element Winner;

        /// <summary>同元素相遇：不发生反应，只禁止本次移动。</summary>
        public static ReactionDef NoReaction() => new ReactionDef { Rule = ReactionRule.NoReaction, Winner = NoElement };

        /// <summary>
        /// 双方都消失、不生成任何新元素。
        /// 当前这张表用不到（10 对都是相生或相克），保留它是为了以后能配出「湮灭」型规则。
        /// </summary>
        public static ReactionDef BothVanish() => new ReactionDef { Rule = ReactionRule.BothVanish, Winner = NoElement };

        /// <summary>相生：输家就地变成 winner。</summary>
        public static ReactionDef Transform(Element winner) => new ReactionDef { Rule = ReactionRule.Transform, Winner = winner };

        /// <summary>相克：输家直接消失，winner 留下。</summary>
        public static ReactionDef Annihilate(Element winner) => new ReactionDef { Rule = ReactionRule.Annihilate, Winner = winner };

        /// <summary>没有赢家时的占位值，避免 Winner 字段带着旧数据误导调用方。</summary>
        public const Element NoElement = Element.Metal;

        /// <summary>相生：输家变成赢家元素（棋盘上的元素总数不变）。</summary>
        public bool IsGenerating => Rule == ReactionRule.Transform;

        /// <summary>相克：输家消失（棋盘上的元素总数 −1）。</summary>
        public bool IsDestroying => Rule == ReactionRule.Annihilate;

        public string Describe()
        {
            switch (Rule)
            {
                case ReactionRule.Transform: return $"相生：输的一方变成{ElementDefs.Name(Winner)}（占用不变）";
                case ReactionRule.Annihilate: return $"相克：输的一方直接消失，{ElementDefs.Name(Winner)}留下";
                case ReactionRule.BothVanish: return "双方都消失";
                default: return "不发生反应，禁止移动";
            }
        }
    }

    public enum ReactionRule
    {
        /// <summary>同元素相遇：不发生反应，禁止本次移动。</summary>
        NoReaction = 0,
        /// <summary>相生：输家就地变成赢家元素（原表「X元素+1」句式）。</summary>
        Transform = 1,
        /// <summary>相克：输家直接消失（原表「该X元素消失」句式）。</summary>
        Annihilate = 2,
        /// <summary>双方都消失，不生成新元素（当前表未使用，保留为可选规则）。</summary>
        BothVanish = 3,
    }
}
