using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 「秘术馈赠」：额外给玩家 N 张秘术牌（融合 + 分解各一张）。
    ///
    /// ⚠ 只在**融合牌和分解牌都启用**的关卡里适用（见 <see cref="IsApplicable"/>）：
    ///   引擎里「秘术牌能不能打出去」由 `Rules.HasSpecialCard(kind)` 决定（要求复合元素总开关 +
    ///   该种牌在秘术牌池里），所以在只开了融合牌的关卡里白送一张分解牌 = 一张永远用不出去的死牌。
    ///   这一条同时会出现在关卡校验里 —— 配错的话进关前就被指出来了。
    /// </summary>
    [CreateAssetMenu(fileName = "Event_秘术馈赠", menuName = "TapGJ/事件/秘术馈赠 (GiveSpecialCards)", order = 21)]
    public sealed class Event_GiveSpecialCards : RoundEventDefinition
    {
        [Tooltip("每种秘术牌各给几张（图上的表写的是「每种各一个」，即 1）")]
        [SerializeField, Min(1)] int countPerKind = 1;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (!engine.Rules.UseCompositeElements)
            {
                reason = "本关没开「复合元素」，融合牌 / 分解牌都用不出去";
                return false;
            }

            bool fusion = engine.Rules.HasSpecialCard(CardKind.FusionCard);
            bool decompose = engine.Rules.HasSpecialCard(CardKind.DecomposeCard);

            if (!fusion || !decompose)
            {
                reason = $"本条事件要「融合牌 + 分解牌」都启用，本关只开了"
                         + $"{(fusion ? "融合牌" : decompose ? "分解牌" : "（都没开）")} → 另一种牌拿到手也用不出去";
                return false;
            }

            reason = null;
            return true;
        }

        public override void Apply(GameEngine engine) => engine.GiveSpecialCards(countPerKind);
    }
}
