using System;

namespace TapGJ.Core
{
    /// <summary>
    /// 打出元素牌的作用方式。图1 只写了「对小怪使用卡牌（选择卡牌，点击元素格的格子使用，视为发生作用）」，
    /// 没说清牌面元素与目标元素的关系，这里给出三种可切换的解释，默认第 1 种（与图2 的表自洽）。
    /// </summary>
    public enum CardEffectMode
    {
        /// <summary>
        /// 默认：牌 = 一个虚拟元素，与目标格上的元素按图2 的表发生反应。
        /// 例：水牌打火小怪 → 查「火 遇 水」= 火消失；水牌打金小怪 → 查「金 遇 水」= 金留下、生成 1 个水。
        /// 表里没有反应（如金×金）时该次出牌不合法。
        /// </summary>
        CardAsVirtualElement = 0,

        /// <summary>牌只能打在同元素的小怪上：该小怪消失，并按「牌面元素 × 牌面元素」的规则生成（默认不生成）。</summary>
        SameElementOnly = 1,

        /// <summary>牌只能打在同元素的小怪上：该小怪消失，并生成 1 个同元素（净效果 = 格子平移）。</summary>
        SameElementSpawnOne = 2,
    }

    public enum WinCondition
    {
        /// <summary>图1 的原始规则文本：全场只剩水元素。</summary>
        WaterOnly = 0,
        /// <summary>原型备用：全场只剩同一种元素（水/火/土都可达，便于验证玩法闭环）。</summary>
        SingleElement = 1,
    }

    /// <summary>
    /// 全部可调数值集中在这里，改规则不用翻代码。
    /// </summary>
    [Serializable]
    public class Rules
    {
        public int BoardWidth = 8;
        public int BoardHeight = 8;

        /// <summary>每种初始元素在棋盘上生成几个（5 种 × 4 个 = 20 个，占 64 格的 31%）。</summary>
        public int CreepsPerElement = 4;

        /// <summary>准备阶段每只小怪每回合的随机移动步数上限（0~MaxMoveSteps）。</summary>
        public int MaxMoveSteps = 3;

        /// <summary>玩家初始手牌数量。</summary>
        public int InitialHandSize = 5;

        /// <summary>
        /// 手牌上限。抽牌阶段会把手牌补到这个数量；弃牌阶段要把手牌弃回这个数量以内。
        /// 图1 只写了「抽牌直至 5 张」「弃牌直至至多 5 张」，两处都用 5，所以这里当作同一个上限。
        /// </summary>
        public int HandLimit = 5;

        /// <summary>弃牌阶段每回合最多能弃几张（图1：「弃牌直至至多 5 张」）。</summary>
        public int MaxDiscardsPerRound = 5;

        /// <summary>
        /// 玩家行动阶段每回合最多能打出几张牌。0 = 不限制（当前默认，与图1 的字面一致）。
        ///
        /// ⚠ 建议设成 1~2：抽牌阶段每回合把手牌补满 5 张，如果出牌还不限张数，
        /// 玩家一回合能打十几张，棋盘几回合就被清空，弃牌阶段永远用不上。
        /// 实测数据见 README 第四节的「每回合出牌上限」表。
        /// </summary>
        public int MaxCardsPerRound = 0;

        /// <summary>元素小瓶子容量，满瓶换算成 1 张对应元素牌。</summary>
        public int BottleCapacity = 5;

        /// <summary>回合上限，超过即失败。</summary>
        public int RoundLimit = 15;

        /// <summary>胜利条件。</summary>
        public WinCondition WinCondition = WinCondition.WaterOnly;

        /// <summary>元素牌的作用方式（图1 未定义清楚，见 CardEffectMode 注释）。</summary>
        public CardEffectMode CardEffect = CardEffectMode.CardAsVirtualElement;

        public Rules Clone() => (Rules)MemberwiseClone();

        public int CellCount => BoardWidth * BoardHeight;
        public int InitialCreepCount => CreepsPerElement * ElementDefs.Count;

        public void Validate()
        {
            if (BoardWidth < 1) throw new ArgumentException("BoardWidth 必须 >= 1");
            if (BoardHeight < 1) throw new ArgumentException("BoardHeight 必须 >= 1");
            if (CreepsPerElement < 0) throw new ArgumentException("CreepsPerElement 不能为负");
            if (InitialCreepCount > CellCount)
                throw new ArgumentException($"初始小怪 {InitialCreepCount} 个超过棋盘 {CellCount} 格");
            if (MaxMoveSteps < 0) throw new ArgumentException("MaxMoveSteps 不能为负");
            if (InitialHandSize < 0) throw new ArgumentException("InitialHandSize 不能为负");
            if (HandLimit < 0) throw new ArgumentException("HandLimit 不能为负");
            if (MaxDiscardsPerRound < 0) throw new ArgumentException("MaxDiscardsPerRound 不能为负");
            if (MaxCardsPerRound < 0) throw new ArgumentException("MaxCardsPerRound 不能为负");
            if (BottleCapacity < 1) throw new ArgumentException("BottleCapacity 至少为 1");
            if (RoundLimit < 1) throw new ArgumentException("RoundLimit 至少为 1");
        }
    }
}
