using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**本局累计成功融合过 N 次复合元素**（第二章「合成教学」）。
    ///
    /// 和 <see cref="OnlyTheseElementsRule"/> 这类组件叠起来，就是「合成 1 个 **且** 场上只剩两种元素」——
    /// 记得把 `Rules.RequireAllWinRules` 勾上，否则两个条件是 OR。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`，否则内置判定会先跑（默认「只剩水」）把这一关误判赢。
    /// </summary>
    public sealed class FusionCountRule : LevelRuleBehaviour
    {
        [Tooltip("本局累计要成功融合几次")]
        [SerializeField, Min(1)] int count = 1;

        public int RequiredCount => count;

        protected override string DefaultGoalDescription => $"成功融合 {count} 次复合元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
            => IsSatisfied(engine, count, out message);

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            int got = engine == null ? 0 : engine.TotalFusions;

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = count <= 0 ? 1f : Clamp01((float)got / count),
                Label = $"融合 {got} / {count}",
            };
        }

        /// <summary>纯逻辑版本（测试不用建 GameObject 就能验）。</summary>
        public static bool IsSatisfied(GameEngine engine, int count, out string message)
        {
            if (engine == null)
            {
                message = null;
                return false;
            }

            int got = engine.TotalFusions;
            if (got >= count)
            {
                message = $"胜利：本局已经成功融合 {got} 次（≥ {count}）";
                return true;
            }

            message = null;
            return false;
        }
    }
}
