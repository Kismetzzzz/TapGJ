using System.Text;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**场上只剩「指定的那几种元素」**（例如「只剩水和火」）。
    ///
    /// 为什么是组件而不是一种 GoalKind：内置的 7 种目标都只能盯**一种**元素的数量，
    /// 而这条要同时看五种 —— 允许的必须留着、不允许的必须清空。理由和
    /// <see cref="EqualElementCountsRule"/> 完全一样：一次性的关系型条件该是自己的组件。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`，否则内置判定会先跑（默认是「只剩水元素」），
    ///   可能在你这条规则之前就把这一关判赢了。
    ///
    /// ⚠ 设计这一关时要自己确认「要被清掉的那种元素真的清得掉」：
    ///   元素池里只放「允许的元素 + 要被清掉的元素」，而且要清掉的那种必须**有牌能打它**
    ///   （相克让它消失，或相生把它变成允许的元素）。否则这一关永远赢不了 ——
    ///   校验器不会替你发现这一点（它只检查「元素池里有没有水」这种单元素条件）。
    /// </summary>
    public sealed class OnlyTheseElementsRule : LevelRuleBehaviour
    {
        [Tooltip("允许留在场上的元素（多选）。不在这个集合里的元素必须一个都不剩，才算赢。\n" +
                 "例：「只剩水和火」就只勾水、火。")]
        [SerializeField] ElementMask allowed = ElementMask.None;

        [Tooltip("勾上（默认）：集合里每种元素都至少要有 1 个 —— 防止靠「把某一种也清光」走捷径。\n" +
                 "⚠ 这会带来一个可以自己锁死自己的局面：把允许的元素清光就再也造不出来了（只能重开）。\n" +
                 "关掉它，「只剩水」这种更短的路也算赢。")]
        [SerializeField] bool requireEachAllowedPresent = true;

        /// <summary>允许的元素集合（编辑器工具/测试用）。</summary>
        public ElementMask Allowed => allowed;

        protected override string DefaultGoalDescription
        {
            get
            {
                if (allowed == ElementMask.None) return "场上只剩（未配置允许元素）";

                return requireEachAllowedPresent
                    ? $"场上只剩「{allowed.Names()}」，且每种都有"
                    : $"场上只剩「{allowed.Names()}」";
            }
        }

        public override bool TryExtraWin(GameEngine engine, out string message)
            => TryWin(engine, allowed, requireEachAllowedPresent, out message);

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            if (engine == null) return default;

            int allowedOnBoard = 0;
            int forbiddenOnBoard = 0;
            foreach (var e in ElementDefs.All)
            {
                int n = engine.Board.CountOf(e);
                if (allowed.Has(e)) allowedOnBoard += n;
                else forbiddenOnBoard += n;
            }

            bool won = TryWin(engine, allowed, requireEachAllowedPresent, out var detail);
            int visible = allowedOnBoard + forbiddenOnBoard;

            return new GoalProgress
            {
                Description = GoalDescription,
                // 达成时一定填满：否则会出现「已经赢了但进度条没满」
                Fill = won ? 1f : (visible <= 0 ? 0f : Clamp01((float)allowedOnBoard / visible)),
                Label = $"{CountsLine(engine)}（{(won ? "已达成" : detail)}）",
            };
        }

        /// <summary>
        /// 纯逻辑版本（测试不用建 GameObject 就能验；机制作者也能拿它做预览）。
        /// <paramref name="detail"/> 无论成不成立都会填一句人话。
        /// </summary>
        public static bool TryWin(GameEngine engine, ElementMask allowed, bool requireEachAllowedPresent,
            out string message)
        {
            message = null;
            if (engine == null) return false;

            if (allowed == ElementMask.None)
            {
                message = "没有配置「允许的元素」";
                return false;
            }

            int allowedOnBoard = 0;
            var leftovers = new StringBuilder();

            foreach (var e in ElementDefs.All)
            {
                int n = engine.Board.CountOf(e);

                if (allowed.Has(e))
                {
                    allowedOnBoard += n;

                    if (n == 0 && requireEachAllowedPresent)
                    {
                        message = $"还缺{ElementDefs.Name(e)}元素";
                        return false;
                    }

                    continue;
                }

                if (n <= 0) continue;

                if (leftovers.Length > 0) leftovers.Append('、');
                leftovers.Append(ElementDefs.Name(e)).Append(n);
            }

            if (leftovers.Length > 0)
            {
                message = $"还没清掉：{leftovers}";
                return false;
            }

            // 清空棋盘不算赢：目标说的是「只剩水和火」，不是「什么都没有」
            if (allowedOnBoard <= 0)
            {
                message = "场上一个允许的元素都没有（清空棋盘不算赢）";
                return false;
            }

            message = $"胜利：场上只剩「{allowed.Names()}」";
            return true;
        }

        static string CountsLine(GameEngine engine)
        {
            var sb = new StringBuilder();
            foreach (var e in ElementDefs.All)
            {
                int n = engine.Board.CountOf(e);
                if (n <= 0) continue;

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(ElementDefs.Name(e)).Append(n);
            }

            return sb.Length == 0 ? "场上没有元素" : sb.ToString();
        }
    }
}
