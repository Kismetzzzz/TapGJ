using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 「元素馈赠」：额外给玩家 N 张元素牌（种类从**本关牌池**里均匀随机）。
    ///
    /// 这几条是刻意的：
    ///   · 从 `Rules.CardPool` 抽 —— 给池外元素的牌等于给一张打不出去的废牌（出牌会被拒）；
    ///   · **不受抽牌权重影响**：权重管的是「抽牌阶段抽到什么」，事件是礼物，不走那一套；
    ///   · **允许把手牌顶到上限以上**（和「瓶子满自动产牌」同一个口径）。想清就自己在弃牌阶段弃，
    ///     所以这条事件顺带把一直空转的弃牌阶段激活了。
    /// </summary>
    [CreateAssetMenu(fileName = "Event_元素馈赠", menuName = "TapGJ/事件/元素馈赠 (GiveElementCards)", order = 20)]
    public sealed class Event_GiveElementCards : RoundEventDefinition
    {
        [Tooltip("给几张元素牌（图上的表写的是 3）")]
        [SerializeField, Min(1)] int count = 3;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Rules.PoolCards.Length > 0)
            {
                reason = null;
                return true;
            }

            reason = "本关牌池里一张元素牌都没有，给了也用不了";
            return false;
        }

        public override void Apply(GameEngine engine) => engine.GiveElementCards(count);
    }
}
