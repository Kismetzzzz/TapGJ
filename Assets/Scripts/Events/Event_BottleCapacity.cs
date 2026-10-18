using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 「玉翁敛息」：本回合元素小葫芦（= 元素小瓶子 <see cref="Bottle"/>）容量上限变成 N。
    ///
    /// 语义（两条都是刻意的）：
    ///   · **先结账再降容**：触发的一瞬间，把瓶里已经攒够一个新容量的份数换成牌（见
    ///     `GameEngine.ApplyBottleCapacity`），余数留在瓶里。观感是「葫芦把憋着的气吐出来」，
    ///     而不是「上限变了但什么也没发生」。
    ///   · 容量小 = 满得快 = 这回合产牌快，所以它是**利好**事件；判定阶段恢复成本关的值。
    ///
    /// ⚠ 本关容量本来就 ≤ 目标值时它没有任何变化，会被当成「不适用」跳过（见 IsApplicable）。
    /// </summary>
    [CreateAssetMenu(fileName = "Event_玉翁敛息", menuName = "TapGJ/事件/玉翁敛息 (BottleCapacity)", order = 22)]
    public sealed class Event_BottleCapacity : RoundEventDefinition
    {
        [Tooltip("本回合的瓶子容量（图上的表写的是 3，本关默认是 5）")]
        [SerializeField, Min(1)] int capacity = 3;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            // 本关根本没有瓶子 → 这条事件什么都做不了，抽到会被跳过（不占本回合）。
            if (!engine.Rules.BottlesEnabled)
            {
                reason = "本关没有元素瓶（Rules.BottleMode = Disabled）";
                return false;
            }

            if (capacity < 1)
            {
                reason = $"容量 {capacity} 非法（至少 1）";
                return false;
            }

            if (engine.Rules.BottleCapacity == capacity)
            {
                reason = $"本关瓶子容量本来就是 {capacity}，这条事件不会有任何变化";
                return false;
            }

            reason = null;
            return true;
        }

        public override void Apply(GameEngine engine) => engine.ApplyBottleCapacity(capacity);

        public override void Revert(GameEngine engine) => engine.RestoreBottleCapacity();
    }
}
