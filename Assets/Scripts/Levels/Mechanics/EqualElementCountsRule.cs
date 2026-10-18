using System.Collections.Generic;
using System.Text;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 胜利条件：**场上各种元素的数量相等**（比例相等）。
    ///
    /// 为什么用 LevelRuleBehaviour 而不是加一种 GoalKind：
    /// 现有的 7 种内置目标都在描述「某一种元素的数量」（只剩水 / ≤N / ≥N / 只剩一种…），
    /// 而这个目标描述的是**元素之间的关系**，判定时要同时看所有元素。
    /// 内置种类是给「很多关都会复用」的通用目标准备的；一次性的关系型目标就该是自己的组件 ——
    /// 这也正是 ILevelRule 存在的意义（引擎和场景都不用为它改一行）。
    ///
    /// ⚠ 必须在关卡上把 `rules.Goal` 选成 `Custom`：
    /// 不选的话内置判定会先跑（默认是「只剩水元素」），可能在你这条规则之前就把这一关判赢了。
    ///
    /// 判定时机：胜利只在**回合末的判定阶段**结算，而玩家行动阶段之后棋盘就冻结了
    /// （弃牌阶段不改棋盘），所以「你在行动阶段凑出的相等局面」一定会在判定时被看到 ——
    /// 但下一回合的准备阶段小怪会走动、破坏它，所以这是个**动态**目标。
    /// </summary>
    public sealed class EqualElementCountsRule : LevelRuleBehaviour, ILevelRuleValidator
    {
        [Tooltip("不勾（默认）：只比较**场上出现**的元素，所以「只剩一种元素」也算比例相等。\n" +
                 "勾上：要求本关可用元素池里的每一种都在场上 —— 这样玩家没办法靠「删到只剩一种」走捷径。")]
        [SerializeField] bool requireAllPoolElements;

        [Tooltip("允许的最大差额（最多 − 最少）。0 = 严格相等。\n" +
                 "差额只放宽数量，不放宽「必须在场」：勾了上面那项时，某个元素一个都没有仍然不算赢。")]
        [SerializeField, Min(0)] int tolerance;

        protected override string DefaultGoalDescription => requireAllPoolElements
            ? "场上每种可用元素都有，且数量都相等"
            : "场上出现过的元素数量都相等（只剩一种也算）";

        /// <summary>允许的最大差额（编辑器工具 / 测试用）。</summary>
        public int Tolerance => tolerance;

        /// <summary>是不是要求「元素池里每种都在场」（编辑器工具 / 测试用）。</summary>
        public bool RequireAllPoolElements => requireAllPoolElements;

        // ---------------------------------------------------------------- 判定

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (!IsBalanced(engine, requireAllPoolElements, tolerance, out var detail))
            {
                message = null;
                return false;
            }

            message = $"胜利：{detail}（{CountsLine(engine, Considered(engine, requireAllPoolElements))}）";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            var considered = Considered(engine, requireAllPoolElements);
            if (considered.Count == 0 || engine.CreepCount == 0)
                return new GoalProgress { Description = GoalDescription, Fill = 0f, Label = "场上没有元素" };

            int min = int.MaxValue, max = 0;
            foreach (var e in considered)
            {
                int c = engine.Board.CountOf(e);
                if (c < min) min = c;
                if (c > max) max = c;
            }

            // 达成时一定要填满，否则会看到「已经赢了但进度条只有 2/3」
            bool won = IsBalanced(engine, requireAllPoolElements, tolerance, out _);
            float fill = won ? 1f : (max <= 0 ? 0f : Clamp01((float)min / max));

            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = fill,
                Label = $"{CountsLine(engine, considered)}（差 {max - min}）",
            };
        }

        /// <summary>
        /// 纯逻辑版本（测试不用建 GameObject 就能验；机制作者也可以拿它做预览）。
        /// <paramref name="detail"/> 是人话说明，无论成不成立都会填。
        /// </summary>
        public static bool IsBalanced(GameEngine engine, bool requireAllPoolElements, int tolerance, out string detail)
        {
            detail = null;
            if (engine == null) return false;

            var considered = Considered(engine, requireAllPoolElements);
            if (considered.Count == 0)
            {
                detail = "场上没有元素";
                return false;
            }

            int min = int.MaxValue, max = 0, total = 0;
            foreach (var e in considered)
            {
                int c = engine.Board.CountOf(e);

                // 「必须都在场」这一条不受 tolerance 影响：差 1 个和完全没有不是一回事
                if (c <= 0 && requireAllPoolElements)
                {
                    detail = $"还缺{ElementDefs.Name(e)}元素";
                    return false;
                }

                if (c < min) min = c;
                if (c > max) max = c;
                total += c;
            }

            if (total <= 0)
            {
                detail = "场上没有元素";
                return false;
            }

            if (max - min > tolerance)
            {
                detail = $"最多 {max} 个、最少 {min} 个，差 {max - min}（允许 {tolerance}）";
                return false;
            }

            detail = considered.Count == 1
                ? $"只剩{ElementDefs.Name(considered[0])}元素"
                : "每种元素数量都相等";
            return true;
        }

        // ---------------------------------------------------------------- 配置自查

        /// <summary>
        /// 关卡面板 / 「校验所有关卡」/ 进关时都会调到这里。
        /// 两种配置隐患都是"这一关永远赢不了 / 目标会退化成别的目标"，光看数值看不出来。
        /// </summary>
        public void CollectRuleIssues(LevelDefinition level, List<string> into)
        {
            if (level == null || into == null || level.rules == null) return;

            var pool = level.rules.ElementPool;
            if (pool == ElementMask.None) return; // 留空的旧数据运行时会按「全部」处理，别在这里误报

            if (!requireAllPoolElements)
            {
                // 不勾的时候「只剩一种元素」也算赢，而那条路更短 —— 关卡会退化成 SingleElement
                if (pool.Count() > 1)
                    into.Add("这个机制没勾「Require All Pool Elements」：「只剩一种元素」也会算赢，"
                             + "而相克每张牌稳定少一个元素，玩家多半会直接删到只剩一种，"
                             + "根本不会去凑比例，这一关就退化成了「只剩一种元素」目标。"
                             + "想让「比例相等」真的被玩到，就把它勾上。");
                return;
            }

            // 勾了「必须都在场」但布局是空的 = 随机铺盘。
            // 而 CreepsPerElement 的含义是「每种可用元素各铺几个」—— 随机铺盘给每种的数量**一样多**，
            // 于是这个目标开局就基本达成了，关卡变成「别打乱它」而不是「凑出它」。
            if (string.IsNullOrWhiteSpace(level.layoutText))
            {
                into.Add("勾了「必须都在场」，但布局是空的（走随机铺盘）—— 随机铺盘给每种元素铺的数量是一样多的，"
                         + "所以这一关开局就基本达成这个目标了。这个目标要用字符画摆一个**不相等**的开局。");
                return;
            }

            // 池子里每一种都必须能出现在棋盘上，否则永远赢不了。
            // ⚠ **不能只看字符画布局**：出牌也能把新元素放到场上（图1 第 1、2 条）——
            //   空格召唤（打木牌到空格上）和相生（打木牌到水怪上）都能造出布局里没画的元素。
            //   只按布局判会误报「永远赢不了」，这正是这一关（level_02）踩过的坑。
            var onBoard = new List<Element>();
            foreach (var e in ElementDefs.All)
                if (pool.Has(e) && level.layoutText.Contains(ElementDefs.Name(e)))
                    onBoard.Add(e);

            var missing = new List<string>();
            foreach (var e in ElementDefs.All)
            {
                if (!pool.Has(e)) continue;
                if (onBoard.Contains(e)) continue;

                // 布局里没有，但玩家出牌能造出来 → 不是死局
                if (level.rules.CardSourceFor(e, onBoard) != null) continue;

                missing.Add(ElementDefs.Name(e));
            }

            if (missing.Count > 0)
                into.Add($"勾了「必须都在场」，但「{string.Join("", missing)}」在字符画布局里一个都没有，"
                         + "而且**靠出牌也造不出来**（空格召唤要有它的元素牌；相生要它作为赢家打在场上已有的某种元素上，"
                         + "两条都不成立）—— 除非有别的机制会生成它，否则这种元素永远不会出现，这一关永远赢不了。");
        }

        // ---------------------------------------------------------------- 工具

        /// <summary>这局要比较哪些元素：勾了「必须都在场」就按元素池，否则按场上实际出现的。</summary>
        static List<Element> Considered(GameEngine engine, bool requireAllPoolElements)
        {
            var list = new List<Element>(ElementDefs.Count);
            if (engine == null) return list;

            foreach (var e in ElementDefs.All)
            {
                bool include = requireAllPoolElements
                    ? engine.Rules.ElementPool.Has(e)
                    : engine.Board.CountOf(e) > 0;
                if (include) list.Add(e);
            }
            return list;
        }

        static string CountsLine(GameEngine engine, List<Element> considered)
        {
            if (considered.Count == 0) return "场上没有元素";

            var sb = new StringBuilder();
            foreach (var e in considered)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(ElementDefs.Name(e)).Append(engine.Board.CountOf(e));
            }
            return sb.ToString();
        }
    }
}
