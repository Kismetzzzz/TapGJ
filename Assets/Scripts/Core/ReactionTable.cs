using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 图2 的「元素小怪作用」表。原表按五行逐行书写，共 25 条，
    /// 其中 5 条是同元素相遇（不发生反应，只禁止移动），其余 20 条是 10 对跨元素组合。
    ///
    /// ── 统一读法（原表的唯一自洽解释）──
    /// 原表用了两种句式，看起来很矛盾，但按同一条规则读就全部自洽：
    ///   「与 X 相遇 X+1」   → 被点名的 X 活下来并 +1
    ///   「与 X 相遇 X消失」 → 被点名的 X 消失
    /// 把两条记录（A 行、B 行）合起来看，**被点名的那个元素就是输家**：
    ///   · A 行说「与 B 相遇，该 A 元素消失」→ A 输
    ///   · A 行说「与 B 牌相遇 B 元素+1」   → A 输、B 赢
    /// 两个方向指向同一个赢家，10 对全部可判。
    ///
    /// 于是 10 对的结果是（赢家 = 留下并 +1 的一方）：
    ///   金×木 → 金赢      金×火 → 火赢      金×水 → 水赢
    ///   水×木 → 木赢      水×火 → 水赢
    ///   木×火 → 火赢      木×土 → 木赢
    ///   火×土 → 土赢      金×土 → 金赢
    ///   水×土 → 土赢
    /// </summary>
    public sealed class ReactionTable
    {
        readonly ReactionDef[,] _table = new ReactionDef[ElementDefs.Count, ElementDefs.Count];

        public ReactionTable()
        {
            // 每对只声明一次（第一个参数主动撞上第二个参数），赢家明确。
            // 判据统一为：原表这条记录点名的元素就是输家，另一方留下并 +1。
            // 注释里的「某行」指图2 中该元素那一行。

            // 木输、金赢 —— 金行「与木元素相遇，该木元素消失」/ 木行「与金元素相遇，该木元素消失」
            WinTarget(Element.Metal, Element.Wood, Element.Metal);

            // 火输、金赢 —— 火行「与金元素相遇，该金元素消失」/ 土行「与金牌相遇金元素+1」
            WinTarget(Element.Fire, Element.Metal, Element.Metal);

            // 金输、水赢 —— 金行「与水牌相遇水元素+1」/ 水行「与金相遇水元素+1」
            WinTarget(Element.Metal, Element.Water, Element.Water);

            // 土输、金赢 —— 金行「与土牌相遇金元素+1」/ 土行「与金牌相遇金元素+1」
            WinTarget(Element.Metal, Element.Earth, Element.Metal);

            // 水输、木赢 —— 木行「与水牌相遇木元素+1」/ 水行「与木牌相遇木元素+1」
            WinTarget(Element.Water, Element.Wood, Element.Wood);

            // 木输、火赢 —— 木行「与火牌相遇火元素+1」/ 火行「与木牌相遇火元素+1」
            WinTarget(Element.Wood, Element.Fire, Element.Fire);

            // 土输、木赢 —— 木行「与土元素相遇，该土元素消失」/ 土行「与木元素相遇，该土元素消失」
            WinTarget(Element.Wood, Element.Earth, Element.Wood);

            // 火输、水赢 —— 水行「与火元素相遇，该火元素消失」/ 火行「与水元素相遇，该火元素消失」
            WinTarget(Element.Water, Element.Fire, Element.Water);

            // 水输、土赢 —— 水行「与土元素相遇，该水元素消失」/ 土行「与水元素相遇，该水元素消失」
            WinTarget(Element.Water, Element.Earth, Element.Earth);

            // 火输、土赢 —— 火行「与土牌相遇土元素+1」/ 土行「与火牌相遇土元素+1」
            WinTarget(Element.Fire, Element.Earth, Element.Earth);

            // 同元素：相遇即禁止该次移动，不发生任何反应。
            for (int i = 0; i < ElementDefs.Count; i++)
                _table[i, i] = ReactionDef.NoReaction();
        }

        /// <summary>
        /// 声明一对组合：a 主动撞上 b，winElement 是留下并 +1 的那一方，另一方消失。
        /// 两个方向一起写，保证 (a,b) 与 (b,a) 不会打架。
        /// </summary>
        void WinTarget(Element a, Element b, Element winner)
        {
            if (winner != a && winner != b)
                throw new ArgumentException($"{ElementDefs.Name(winner)} 不是这一对的成员");

            _table[(int)a, (int)b] = ReactionDef.SpawnOne(winner);
            _table[(int)b, (int)a] = ReactionDef.SpawnOne(winner);
        }

        public ReactionDef Get(Element a, Element b) => _table[(int)a, (int)b];

        /// <summary>某种元素作为「被撞上的一方」时，能打退哪些元素（用来生成玩家提示）。</summary>
        public IEnumerable<Element> BeatenBy(Element target)
        {
            foreach (var a in ElementDefs.All)
            {
                if (a == target) continue;
                var def = Get(a, target);
                if (def.CreatesCreep && def.Spawn == target) yield return a;
            }
        }

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
        /// <summary>Rule = SpawnOne 时留下并 +1 的元素。</summary>
        public Element Spawn;

        /// <summary>同元素相遇：不发生反应，只禁止本次移动。</summary>
        public static ReactionDef NoReaction() => new ReactionDef { Rule = ReactionRule.NoReaction, Spawn = NoElement };

        /// <summary>
        /// 双方都消失、不生成任何新元素。
        /// 当前这张表用不到（10 对都有明确赢家），保留它是为了以后能配出「湮灭」型规则。
        /// </summary>
        public static ReactionDef BothVanish() => new ReactionDef { Rule = ReactionRule.BothVanish, Spawn = NoElement };

        /// <summary>主动方消失，winElement 留下并 +1。</summary>
        public static ReactionDef SpawnOne(Element winElement) => new ReactionDef { Rule = ReactionRule.SpawnOne, Spawn = winElement };

        /// <summary>没有生成物时的占位值，避免 Spawn 字段带着旧数据误导调用方。</summary>
        public const Element NoElement = Element.Metal;

        public bool CreatesCreep => Rule == ReactionRule.SpawnOne;

        /// <summary>这张表里 10 对是否都有明确赢家。</summary>
        public bool IsDecided => Rule != ReactionRule.NoReaction;

        public string Describe()
        {
            switch (Rule)
            {
                case ReactionRule.SpawnOne: return $"主动方消失，{ElementDefs.Name(Spawn)}留下并 +1";
                case ReactionRule.BothVanish: return "双方都消失";
                default: return "不发生反应，禁止移动";
            }
        }
    }

    public enum ReactionRule
    {
        /// <summary>同元素相遇：不发生反应，禁止本次移动。</summary>
        NoReaction = 0,
        /// <summary>双方都消失，不生成新元素（当前表未使用，保留为可选规则）。</summary>
        BothVanish = 1,
        /// <summary>主动方消失，被撞方留下并 +1。</summary>
        SpawnOne = 2,
    }
}
