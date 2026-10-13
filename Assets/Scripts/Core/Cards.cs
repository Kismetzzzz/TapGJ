namespace TapGJ.Core
{
    /// <summary>
    /// 手牌的种类。
    ///   · <see cref="ElementCard"/>  —— 五行元素牌（原有玩法）
    ///   · <see cref="FusionCard"/>   —— **秘术牌**：融合牌，把相邻的两个元素合成一个复合元素
    ///   · <see cref="DecomposeCard"/>—— **秘术牌**：分解牌，把场上的复合元素拆回两张元素牌
    /// </summary>
    public enum CardKind
    {
        /// <summary>五行元素牌。旧存档里没有这个字段，反序列化出来正好是 0 = 元素牌，行为不变。</summary>
        ElementCard = 0,

        /// <summary>融合牌：只有<see cref="CardKind.ElementCard"/>之外的一种新牌。</summary>
        FusionCard = 1,

        /// <summary>分解牌。</summary>
        DecomposeCard = 2,
    }

    /// <summary>
    /// 玩家手里的一张牌。
    /// 元素牌：牌面元素 = 打出后能与场上哪个元素发生反应。
    /// 秘术牌：没有牌面元素（<see cref="Element"/> 是一个占位值，不要读它）。
    /// </summary>
    public sealed class Card
    {
        public int Id { get; }
        public CardKind Kind { get; }

        /// <summary>
        /// 牌面元素。**只有元素牌有意义** —— 秘术牌的这张牌没有元素，
        /// 读它只会拿到占位值 <see cref="Element.Metal"/>。判断牌的种类一律看 <see cref="Kind"/>。
        /// </summary>
        public Element Element { get; }

        internal Card(int id, Element element)
        {
            Id = id;
            Kind = CardKind.ElementCard;
            Element = element;
        }

        internal Card(int id, CardKind kind)
        {
            Id = id;
            Kind = kind;
            Element = Element.Metal; // 占位：秘术牌没有元素
        }

        public bool IsElementCard => Kind == CardKind.ElementCard;
        public bool IsFusionCard => Kind == CardKind.FusionCard;
        public bool IsDecomposeCard => Kind == CardKind.DecomposeCard;
        public bool IsSpecialCard => Kind != CardKind.ElementCard;

        public string Label
        {
            get
            {
                switch (Kind)
                {
                    case CardKind.FusionCard: return "融合牌";
                    case CardKind.DecomposeCard: return "分解牌";
                    default: return $"{ElementDefs.Name(Element)}牌";
                }
            }
        }

        /// <summary>牌面上显示的字（元素牌是五行字，秘术牌是「融合」/「分解」）。</summary>
        public string Glyph
        {
            get
            {
                switch (Kind)
                {
                    case CardKind.FusionCard: return "融合";
                    case CardKind.DecomposeCard: return "分解";
                    default: return ElementDefs.Name(Element);
                }
            }
        }

        /// <summary>牌面配色。秘术牌用固定的两种颜色，和五行配色区分开。</summary>
        public UnityEngine.Color Tint
        {
            get
            {
                switch (Kind)
                {
                    case CardKind.FusionCard: return new UnityEngine.Color(0.58f, 0.42f, 0.82f);
                    case CardKind.DecomposeCard: return new UnityEngine.Color(0.42f, 0.36f, 0.30f);
                    default: return ElementDefs.Tint(Element);
                }
            }
        }

        public override string ToString() => $"#{Id}{Label}";
    }
}
