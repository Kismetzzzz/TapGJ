using System.Text;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**场上恰好只剩 N 种元素**（不指定是哪几种）。
    ///
    /// 和 <see cref="OnlyTheseElementsRule"/> 的分工：
    ///   · 「只剩**水**和**火**」→ 用 `OnlyTheseElementsRule`（指定是哪两种，还能要求两种都在）
    ///   · 「只剩**任意**两种」  → 用本组件（只说种类数，不说名字）
    ///
    /// 判定是**精确相等**，所以空棋盘（0 种）天然不算赢 —— 「清空全场」不会变成捷径，不需要额外开关。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`，否则内置判定会先跑把这一关误判赢。
    /// </summary>
    public sealed class ElementKindsRule : LevelRuleBehaviour
    {
        [Tooltip("场上要恰好剩下几种元素")]
        [SerializeField, Min(1)] int kinds = 2;

        public int RequiredKinds => kinds;

        protected override string DefaultGoalDescription => $"场上恰好只剩 {kinds} 种元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
            => IsSatisfied(engine, kinds, out message);

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            int now = engine == null ? 0 : engine.Board.DistinctElementCount();
            int target = Mathf.Max(1, kinds);

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = now == target ? 1f : Clamp01(1f - Mathf.Abs(now - target) / 5f),
                Label = $"元素种类 {now} / {kinds}",
            };
        }

        /// <summary>纯逻辑版本（测试不用建 GameObject 就能验）。</summary>
        public static bool IsSatisfied(GameEngine engine, int kinds, out string message)
        {
            message = null;
            if (engine == null) return false;

            int now = engine.Board.DistinctElementCount();
            if (now != kinds)
            {
                message = now > kinds
                    ? $"场上还有 {now} 种元素（要 {kinds} 种）：{NamesOnBoard(engine)}"
                    : $"场上只剩 {now} 种元素（要 {kinds} 种）";
                return false;
            }

            message = $"胜利：场上恰好只剩 {now} 种元素（{NamesOnBoard(engine)}）";
            return true;
        }

        static string NamesOnBoard(GameEngine engine)
        {
            var sb = new StringBuilder();
            foreach (var e in ElementDefs.All)
            {
                if (engine.Board.CountOf(e) <= 0) continue;
                if (sb.Length > 0) sb.Append('、');
                sb.Append(ElementDefs.Name(e));
            }

            return sb.Length == 0 ? "无" : sb.ToString();
        }
    }
}
