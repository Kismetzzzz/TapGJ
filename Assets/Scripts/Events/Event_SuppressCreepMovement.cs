using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 「静滞结界」：本回合元素不会自主相互作用。
    ///
    /// 实现上就是**准备阶段直接跳过小怪移动**（`GameEngine.AdvancePreparation` 里那个分支）：
    /// 不走 = 不会相遇 = 不会相生/相克，比「照常走但相遇不结算」干净得多
    /// （后者还要重新定义「异元素相遇算不算挡路」）。
    ///
    /// 两条边界：
    ///   · **复合元素的每回合产出照旧** —— 它是「产出元素」，不是元素之间的相互作用；
    ///   · 玩家出牌照样能引发反应（那是玩家行动，不是「自主」相互作用）。
    /// </summary>
    [CreateAssetMenu(fileName = "Event_静滞结界", menuName = "TapGJ/事件/静滞结界 (SuppressCreepMovement)", order = 23)]
    public sealed class Event_SuppressCreepMovement : RoundEventDefinition
    {
        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Board.CreepCount() > 0)
            {
                reason = null;
                return true;
            }

            reason = "场上一个小怪都没有，定住它们没有任何效果";
            return false;
        }

        public override void Apply(GameEngine engine) => engine.SetSuppressCreepMovement(true);

        public override void Revert(GameEngine engine) => engine.SetSuppressCreepMovement(false);
    }
}
