namespace TapGJ.Core
{
    /// <summary>
    /// 一次「元素相遇」结算完之后的状态快照，交给 <see cref="ILevelRule.OnEncounter"/>。
    ///
    /// 两种来源都会通知：
    ///   · 准备阶段两只小怪撞上（<see cref="IsPlayerCard"/> = false，Uid 是两只小怪的真实 uid）
    ///   · 玩家出牌命中目标格（<see cref="IsPlayerCard"/> = true，TriggerUid = -1，因为牌不是小怪）
    /// </summary>
    public readonly struct EncounterInfo
    {
        /// <summary>主动方小怪 uid；出牌时为 -1。</summary>
        public readonly int TriggerUid;
        /// <summary>被撞方小怪 uid；出牌时就是被打的那只。</summary>
        public readonly int TargetUid;
        public readonly Element TriggerElement;
        public readonly Element TargetElement;

        /// <summary>这次反应的种类（相生 / 相克 / 同归于尽 / 同元素撞车）。</summary>
        public readonly ReactionRule Rule;

        /// <summary>赢家元素。仅当 <see cref="HasWinner"/> 为 true 时有意义。</summary>
        public readonly Element Winner;
        /// <summary>输家元素。仅当 <see cref="HasWinner"/> 为 true 时有意义。</summary>
        public readonly Element Loser;

        /// <summary>发生反应的那一格。</summary>
        public readonly Pos Cell;

        /// <summary>是不是玩家出牌触发的（false = 准备阶段小怪互撞）。</summary>
        public readonly bool IsPlayerCard;

        internal EncounterInfo(int triggerUid, int targetUid, Element triggerElement, Element targetElement,
            ReactionRule rule, Element winner, Element loser, Pos cell, bool isPlayerCard)
        {
            TriggerUid = triggerUid;
            TargetUid = targetUid;
            TriggerElement = triggerElement;
            TargetElement = targetElement;
            Rule = rule;
            Winner = winner;
            Loser = loser;
            Cell = cell;
            IsPlayerCard = isPlayerCard;
        }

        /// <summary>同元素撞车：没有发生任何反应。</summary>
        public bool HadReaction => Rule != ReactionRule.NoReaction;

        /// <summary>这一对有没有输赢（BothVanish 和同元素撞车都没有）。</summary>
        public bool HasWinner => Rule == ReactionRule.Transform || Rule == ReactionRule.Annihilate;

        /// <summary>相生：输家就地变成赢家，场上元素总数不变。</summary>
        public bool IsGeneration => Rule == ReactionRule.Transform;

        /// <summary>相克 / 同归于尽：有元素真的从棋盘上消失了。</summary>
        public bool IsDestruction => Rule == ReactionRule.Annihilate || Rule == ReactionRule.BothVanish;

        public override string ToString()
        {
            var how = IsPlayerCard ? "出牌" : "相遇";
            return HadReaction
                ? $"[{how}]{ElementDefs.Name(TriggerElement)}×{ElementDefs.Name(TargetElement)} @{Cell} → {Rule}"
                : $"[{how}]{ElementDefs.Name(TriggerElement)}×{ElementDefs.Name(TargetElement)} @{Cell} → 无反应";
        }
    }

    /// <summary>
    /// 一关专属机制的钩子。定义在 Core 里（不引用 UnityEngine），实现通常是挂在
    /// **关卡预制体**上的 MonoBehaviour —— 那个预制体由 LevelLoader 按关卡实例化，
    /// 所以「换关卡 = 换机制」是自动的，引擎和场景都不用改。
    ///
    /// 挂载顺序就是调用顺序；某个钩子抛异常会被停用（记一条日志），不影响其它钩子和主流程。
    ///
    /// 写机制时继承 Assets/Scripts/Levels/Mechanics/LevelRuleBehaviour.cs，
    /// 那样只需要覆盖关心的那几个方法。
    /// </summary>
    public interface ILevelRule
    {
        /// <summary>
        /// 给 HUD / 日志用的一句话目标说明。
        /// 只有 <see cref="GoalKind.Custom"/> 的关卡会用它当胜利条件文案；其余目标留空即可。
        /// </summary>
        string GoalDescription { get; }

        /// <summary>棋盘铺好、手牌发完之后调用一次（引擎的 Start 末尾、第 1 回合开始之前）。</summary>
        void OnLevelStart(GameEngine engine);

        /// <summary>每个回合开始时调用（第 1 回合也算）。</summary>
        void OnRoundStart(GameEngine engine);

        /// <summary>任意一次元素反应结算完之后调用（小怪相遇与玩家出牌都会通知）。</summary>
        void OnEncounter(GameEngine engine, EncounterInfo info);

        /// <summary>
        /// 判定阶段的额外胜利条件。返回 true 即本关立即胜利，message 会显示在结算横幅上。
        ///
        /// 目标不是 Custom 时它只能「加赢」（内置目标先判，不满足才问它）；
        /// 目标是 Custom 时内置判定完全不参与，胜负**只**由钩子决定。
        /// </summary>
        bool TryExtraWin(GameEngine engine, out string message);

        /// <summary>判定阶段的额外失败条件。返回 true 即本关立即失败。</summary>
        bool TryExtraLose(GameEngine engine, out string message);

        /// <summary>自定义目标的进度条数据；不需要就返回 <c>default</c>（Fill = 0 且 Label 为空时会被忽略）。</summary>
        GoalProgress GetGoalProgress(GameEngine engine);
    }
}
