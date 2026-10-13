using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 示例机制二：本关的胜利条件不是「场上剩什么」，而是「累计完成 N 次相生转换」。
    /// 演示 GoalKind.Custom + OnEncounter + TryExtraWin + GetGoalProgress。
    ///
    /// 相生转换 = 两只元素相遇、输的一方就地变成赢家元素（金遇水 → 金变水）。
    /// 玩家出牌造成的转换也算，因为出牌同样会发出 OnEncounter。
    /// </summary>
    public sealed class ReactionCountRule : LevelRuleBehaviour
    {
        [Tooltip("累计多少次相生转换算通关")]
        [SerializeField, Min(1)] int target = 8;

        [Tooltip("只统计玩家出牌造成的转换（勾上就忽略小怪自己撞出来的）")]
        [SerializeField] bool playerCardsOnly;

        int _count;

        protected override string DefaultGoalDescription => $"累计完成 {target} 次相生转换";

        public override void OnLevelStart(GameEngine engine) => _count = 0;

        public override void OnEncounter(GameEngine engine, EncounterInfo info)
        {
            if (!info.IsGeneration) return;            // 只看相生，相克不算
            if (playerCardsOnly && !info.IsPlayerCard) return;
            _count++;
        }

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (_count < target)
            {
                message = null;
                return false;
            }

            message = $"胜利：累计完成 {_count} 次相生转换（目标 {target} 次）";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = Clamp01(target <= 0 ? 1f : (float)_count / target),
            Label = $"相生转换 {_count} / {target}",
        };

        /// <summary>给编辑器和测试看当前进度。</summary>
        public int Count => _count;
    }
}
