using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**某一元素的瓶子已经产出过 N 张对应元素牌**（第三章「小瓶子教学」的核心目标）。
    ///
    /// 一个组件 = 一种元素的一个数量要求。要「10 张金 + 10 张木」就在关卡预制体上挂两份，
    /// 并把 <see cref="Rules.RequireAllWinRules"/> 勾上（否则两个条件是 OR，先达成的那个就赢了）。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`，否则内置判定会先跑（默认「只剩水」）把这一关误判赢。
    /// ⚠ 数字要按「出牌预算」来定：产 N 张某元素牌 = 该元素消失 5N 次 = 至少 5N 次出牌，
    ///   而每回合最多摸 `HandLimit` 张牌。20 张火牌 ≈ 100 次出牌，通常超过一局的牌量 ——
    ///   所以这类关卡要么配一个持续的元素源，要么把 N 定小。
    /// </summary>
    public sealed class BottleProducedRule : LevelRuleBehaviour
    {
        [Tooltip("看哪个元素的小瓶子")]
        [SerializeField] Element element = Element.Fire;

        [Tooltip("要产出多少张对应元素牌（产 N 张 = 这个元素消失了 5N 次）")]
        [SerializeField, Min(1)] int count = 5;

        public Element TargetElement => element;
        public int RequiredCount => count;

        protected override string DefaultGoalDescription =>
            $"让{ElementDefs.Name(element)}瓶子产出 {count} 张{ElementDefs.Name(element)}牌";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            int got = engine.Bottle.CardsGranted(element);

            if (got >= count)
            {
                message = $"胜利：{ElementDefs.Name(element)}瓶子已产出 {got} 张牌（≥ {count}）";
                return true;
            }

            message = null;
            return false;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            int got = engine.Bottle.CardsGranted(element);
            int need = Mathf.Max(1, count);

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = Clamp01((float)got / need),
                Label = $"{ElementDefs.Name(element)}牌 {got} / {count}",
            };
        }
    }
}
