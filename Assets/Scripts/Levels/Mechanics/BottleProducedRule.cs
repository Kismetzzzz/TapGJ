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
    /// ⚠ 数字要按「出牌预算」来定：产 N 张某元素牌 = 该元素消失 **`BottleCapacity` × N** 次，
    ///   而每次消失基本都要一张牌（一局最多打「回合上限 × 手牌上限」张）。
    ///   所以容量 5 的关卡「产 20 张」= 100 次出牌，通常根本打不完。三个办法：
    ///     ① 把 N 定小（容量 5 → 4 张就只要 20 次出牌）；
    ///     ② **把 `BottleCapacity` 调小**（容量 1 → 产 N 张只需要让 N 个消失）；
    ///     ③ 配一个持续的元素源（复合元素、每回合生成元素的机制），让「消失」不必全靠出牌。
    /// </summary>
    public sealed class BottleProducedRule : LevelRuleBehaviour
    {
        [Tooltip("看哪个元素的小瓶子")]
        [SerializeField] Element element = Element.Fire;

        [Tooltip("要产出多少张对应元素牌（产 N 张 = 这个元素消失「瓶容量 × N」次）")]
        [SerializeField, Min(1)] int count = 5;

        public Element TargetElement => element;
        public int RequiredCount => count;

        protected override string DefaultGoalDescription =>
            $"让{ElementDefs.Name(element)}瓶子产出 {count} 张{ElementDefs.Name(element)}牌";

        public override bool TryExtraWin(GameEngine engine, out string message)
            => IsSatisfied(engine, element, count, out message);

        /// <summary>
        /// 纯逻辑版本（测试不用建 GameObject 就能验；也方便别的机制复用）。
        /// 口径就一句：**这一局该元素的瓶子已经产出过多少张牌**（不是「瓶里现在攒了几个」）。
        /// </summary>
        public static bool IsSatisfied(GameEngine engine, Element element, int count, out string message)
        {
            message = null;
            if (engine == null) return false;

            int got = engine.Bottle.CardsGranted(element);
            if (got < count) return false;

            message = $"胜利：{ElementDefs.Name(element)}瓶子已产出 {got} 张牌（≥ {count}）";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            if (engine == null) return default;

            int got = engine.Bottle.CardsGranted(element);
            int need = Mathf.Max(1, count);

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = Clamp01((float)got / need),
                // 后面的「瓶 x/y」是**当前这一格攒了几个**（容量 1 的关卡永远是 0/1 或 1/1）。
                // 教学章全靠它：不然玩家只能看到「已产出几张」，看不到「离下一次还差几个」。
                Label = $"{ElementDefs.Name(element)}牌 {got} / {count}"
                        + $"（瓶 {engine.Bottle.Count(element)}/{engine.Bottle.Capacity}）",
            };
        }
    }
}
