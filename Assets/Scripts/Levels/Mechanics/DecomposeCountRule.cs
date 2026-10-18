using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**本局累计成功分解过 N 次复合元素**（第二章「分解教学 / 分解五种」）。
    ///
    /// 两种写法：
    ///   · **开局摆好**：`initialComposites` 摆 5 种（一种一个），这里 `count = 5` 并勾 **`requireNoComposites`**
    ///     —— 必须「确实拆掉了 5 个、且场上不再剩复合元素」。`AllowMultipleComposites` 关着时融合牌用不出去，
    ///     所以「分解数 = 5」等价于「五种各拆了一次」，不需要按种类记账。（ch2_02 用这种：只拆一个现成的）
    ///   · **自己造再拆**：开局不摆复合元素，玩家用融合牌现场合一个、再用分解牌拆掉，重复 N 次
    ///     （ch2_03 用这种：`count = 5` + `requireNoComposites`）。这时「反复拆同一种」也算数 ——
    ///     真正卡住玩家的不是种类，而是「融合牌 + 分解牌 + 一对相邻元素」的循环速度。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`，否则内置判定会先跑把这一关误判赢。
    /// </summary>
    public sealed class DecomposeCountRule : LevelRuleBehaviour
    {
        [Tooltip("本局累计要成功分解几次")]
        [SerializeField, Min(1)] int count = 1;

        [Tooltip("勾上：还要求**场上一个复合元素都不剩**（「分解五种」这种关卡用它）")]
        [SerializeField] bool requireNoComposites;

        public int RequiredCount => count;
        public bool RequireNoComposites => requireNoComposites;

        protected override string DefaultGoalDescription => requireNoComposites
            ? $"分解 {count} 次复合元素，且场上不再剩复合元素"
            : $"成功分解 {count} 次复合元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
            => IsSatisfied(engine, count, requireNoComposites, out message);

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            int got = engine == null ? 0 : engine.TotalDecompositions;
            int left = engine == null ? 0 : engine.CompositeCount;

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = count <= 0 ? 1f : Clamp01((float)got / count),
                Label = requireNoComposites
                    ? $"分解 {got} / {count} · 场上复合 {left}"
                    : $"分解 {got} / {count}",
            };
        }

        /// <summary>纯逻辑版本（测试不用建 GameObject 就能验）。</summary>
        public static bool IsSatisfied(GameEngine engine, int count, bool requireNoComposites, out string message)
        {
            if (engine == null)
            {
                message = null;
                return false;
            }

            int got = engine.TotalDecompositions;
            if (got < count)
            {
                message = null;
                return false;
            }

            if (requireNoComposites && engine.CompositeCount > 0)
            {
                message = null;
                return false;
            }

            message = requireNoComposites
                ? $"胜利：已分解 {got} 次，且场上不再有复合元素"
                : $"胜利：本局已经成功分解 {got} 次（≥ {count}）";
            return true;
        }
    }
}
