using System.Collections.Generic;
using System.Text;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels
{
    /// <summary>
    /// 一关的完整定义（数据资产）。
    /// 玩法数据放在这里而不是场景里，所以菜单同学不打开 Game.unity 也能列出、描述关卡。
    /// 用菜单 TapGJ ▸ 新建关卡（定义 + 预制体 + 加入目录）生成，或右键 Create ▸ TapGJ ▸ 关卡定义。
    /// </summary>
    [CreateAssetMenu(fileName = "Level_", menuName = "TapGJ/关卡定义 (LevelDefinition)", order = 1)]
    public sealed class LevelDefinition : ScriptableObject
    {
        [Header("身份")]
        public string levelId;
        public string displayName;
        [TextArea] public string description;
        public Sprite previewImage;

        [Header("进入方式")]
        [Tooltip("这一关放在哪个场景里（默认 Game）")]
        public string sceneName = "Game";

        [Tooltip("关卡预制体：这一关的局部美术 + 机制组件（继承 LevelRuleBehaviour）。可以留空")]
        public GameObject levelPrefab;

        [Header("随机种子")]
        [Tooltip("勾上：这一关每次都用 fixedSeed（谜题关用，保证同一种局面）。" +
                 "不勾：优先用菜单传进来的种子，菜单没传就随机。")]
        public bool useFixedSeed;

        [Tooltip("useFixedSeed 勾上时使用的种子")]
        public int fixedSeed = 20240607;

        [Header("玩法")]
        public Rules rules = new Rules();

        [Header("初始棋盘")]
        [Tooltip("字符画：金木水火土 = 元素，. = 空格，# = 墙；行数 = 高度、每行长度 = 宽度。留空 = 随机铺盘")]
        [TextArea(8, 12)] public string layoutText;

        [Tooltip("固定起手牌；空数组 = 按 rules.InitialHandSize 随机")]
        public Element[] fixedInitialHand;

        [Tooltip("开局就摆在棋盘上的复合元素（留空 = 开局没有复合元素，" +
                 "此时只能靠融合牌现场合一个出来）。占格冲突 / 坐标越界的条目会被跳过并记一条日志")]
        public CompositePlacement[] initialComposites = new CompositePlacement[0];

        // ---------------------------------------------------------------- 校验

        /// <summary>
        /// 这一关「能不能玩」的检查：规则死局 + 布局能不能解析 + 机制组件有没有挂。
        /// 返回空列表 = 没有已知问题。
        ///
        /// 编辑器面板、菜单「校验所有关卡」和 LevelLoader 都调它，
        /// 所以设计期看到的问题和 Play 时报的问题一定一模一样。
        /// </summary>
        public void CollectAuthoringIssues(List<string> into)
        {
            if (into == null) return;

            if (string.IsNullOrWhiteSpace(levelId))
                into.Add("levelId 是空的。进度存档与菜单跳转都靠它，必须非空且唯一。");
            if (string.IsNullOrWhiteSpace(displayName))
                into.Add("displayName 是空的，关卡列表里会显示成空白。");
            if (rules == null)
            {
                into.Add("rules 是 null（资产坏了或没初始化）。");
                return;
            }

            // 元素池/牌池留空 = 加这两个字段之前存的旧数据，运行时会按「全部」处理。
            // 先把它说出来（这时还是原始值），后面的检查一律在副本上做 —— 校验不该改动资产本身。
            if (rules.ElementPool == ElementMask.None)
                into.Add("可用元素一个都没勾。运行时按「全部元素」处理（兼容旧数据），但建议显式勾上。");
            if (rules.CardPool == ElementMask.None)
                into.Add("可抽卡牌一个都没勾。运行时按「全部元素」处理（兼容旧数据），但建议显式勾上。");

            var effective = rules.Clone();

            // 规则数值本身合不合法（越界、负数之类会抛异常，这里抓成一句人话继续往下查）
            try
            {
                effective.Validate();
            }
            catch (System.Exception e)
            {
                into.Add($"规则数值非法：{e.Message}");
            }

            // 元素 / 牌池的死局与矛盾（与运行期共用同一套检查）
            effective.CollectIssues(into);

            // 布局
            if (string.IsNullOrWhiteSpace(layoutText))
            {
                if (effective.CreepsPerElement == 0)
                    into.Add("布局留空（走随机铺盘）且 CreepsPerElement = 0 → 开局是一个元素都没有的空棋盘。");
            }
            else if (!BoardLayoutParser.TryParse(layoutText, effective.BoardWidth, effective.BoardHeight,
                         effective.ElementPool, out _, out var error))
            {
                into.Add($"字符画布局有问题：{error}");
            }

            // 固定起手牌 vs 牌池
            if (fixedInitialHand != null)
            {
                foreach (var e in fixedInitialHand)
                    if (!effective.CardPool.Has(e))
                        into.Add($"固定起手牌里有{ElementDefs.Name(e)}牌，但它不在可抽卡牌「{effective.CardPool.Names()}」里。");
            }

            CollectCompositeIssues(effective, into);

            // 自定义目标必须有机制组件，否则这一关永远不会胜利
            if (effective.Goal.Kind == GoalKind.Custom)
            {
                if (levelPrefab == null)
                {
                    into.Add("胜利条件是「自定义」，但没有关卡预制体 → 机制组件没地方挂，这一关永远赢不了。");
                }
                else if (!HasEnabledRule(levelPrefab))
                {
                    into.Add("胜利条件是「自定义」，但关卡预制体上一个启用的机制组件都没有 → 这一关永远赢不了。"
                             + "把继承 LevelRuleBehaviour 的组件挂上去（父物体或子物体都行）。");
                }
            }

            // 再让每个机制组件自己说说有没有配置隐患 —— 机制比关卡更懂自己需要什么。
            // 例：EqualElementCountsRule 要检查「必须都在场」的那几种元素在布局里到底有没有。
            if (levelPrefab == null) return;

            foreach (var rule in levelPrefab.GetComponentsInChildren<ILevelRule>(true))
            {
                if (rule == null) continue;
                if (rule is MonoBehaviour mb && !mb.enabled) continue;
                if (rule is ILevelRuleValidator validator) validator.CollectRuleIssues(this, into);
            }
        }

        /// <summary>
        /// 初始复合元素这一块的检查：两只挤一格、摆在墙里、和布局里的元素抢格子。
        ///
        /// 另外两条由别处统一报，这里不重复：
        ///   · 坐标越界 → <see cref="Rules.CollectIssues"/>
        ///   · 「配了初始复合元素但总开关没勾」→ <see cref="Rules.CollectIssues"/>（那一套更懂总开关）
        /// 总开关没勾时这些初始复合元素**不会被摆上棋盘**，但下面这些坐标错误仍然值得先指出来。
        /// </summary>
        void CollectCompositeIssues(Rules effective, List<string> into)
        {
            if (initialComposites == null || initialComposites.Length == 0) return;

            var used = new HashSet<int>();
            foreach (var entry in initialComposites)
            {
                var pos = new Pos(entry.X, entry.Y);

                int key = entry.Y * effective.BoardWidth + entry.X;
                if (!used.Add(key))
                    into.Add($"初始复合元素有两条都放在 {pos} 上 → 后一条会被跳过（一格最多一个占格者）。");

                // 和字符画布局叠在一起才检查得出「摆在墙上 / 和元素抢格子」。
                if (string.IsNullOrWhiteSpace(layoutText)) continue;
                if (!BoardLayoutParser.TryParse(layoutText, effective.BoardWidth, effective.BoardHeight,
                        effective.ElementPool, out var setup, out _))
                    continue; // 布局本身的问题已经由上面那条报出来了

                if (key < 0 || key >= setup.Blocked.Length) continue;

                if (setup.Blocked[key])
                    into.Add($"初始复合元素「{CompositeDefs.Name(entry.Kind)}」摆在 {pos}，但布局里那一格是墙（#）→ 会被跳过。");
                else if (setup.Cells[key].HasValue)
                    into.Add($"初始复合元素「{CompositeDefs.Name(entry.Kind)}」摆在 {pos}，但布局里那一格是"
                             + $"{ElementDefs.Name(setup.Cells[key].Value)}元素 → 会被跳过（一格最多一个占格者）。");
            }
        }

        /// <summary>预制体（含子物体）上有没有启用的 ILevelRule。挂载顺序 = 执行顺序。</summary>
        public static bool HasEnabledRule(GameObject prefab)
        {
            if (prefab == null) return false;

            var found = prefab.GetComponentsInChildren<ILevelRule>(true);
            foreach (var rule in found)
            {
                if (rule == null) continue;
                if (rule is MonoBehaviour mb && !mb.enabled) continue;
                return true;
            }
            return false;
        }

        /// <summary>面板上显示的一行摘要（元素池 / 牌池 / 目标 / 机制数量）。</summary>
        public string Summary()
        {
            if (rules == null) return "（rules 是 null）";

            // 留空 = 旧数据，运行时会按「全部」处理，摘要里也照这个显示，避免看成「一个元素都没有」
            var elements = rules.ElementPool == ElementMask.None ? ElementMask.All : rules.ElementPool;
            var cards = rules.CardPool == ElementMask.None ? ElementMask.All : rules.CardPool;

            var sb = new StringBuilder();
            sb.Append($"棋盘 {rules.BoardWidth}×{rules.BoardHeight}");
            sb.Append($"　元素 {elements.Names()}");
            sb.Append($"　牌池 {cards.Names()}");
            sb.Append($"　复合元素 {CompositeFeatureText(rules)}");
            sb.Append($"　空格出牌 {EmptyCellCardText(rules)}");
            sb.Append($"　回合上限 {rules.RoundLimit}");
            sb.Append($"　目标 {rules.Goal.Describe()}");

            if (rules.Goal.Kind == GoalKind.Custom && levelPrefab != null)
            {
                int n = 0;
                foreach (var rule in levelPrefab.GetComponentsInChildren<ILevelRule>(true))
                    if (rule != null && (!(rule is MonoBehaviour mb) || mb.enabled)) n++;
                sb.Append($"（机制 {n} 个）");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 面板上那一小段「复合元素 开/关」的文字。
        /// 开着的时候连「能给哪些牌、开局摆了几个」一起写出来，省得往下翻。
        /// </summary>
        public static string CompositeFeatureText(Rules rules)
        {
            if (rules == null) return "关";
            if (!rules.UseCompositeElements) return "关";

            var parts = new List<string>();
            var cards = new List<string>();
            if ((rules.SpecialCardPool & SpecialCardMask.Fusion) != 0) cards.Add("融合牌");
            if ((rules.SpecialCardPool & SpecialCardMask.Decompose) != 0) cards.Add("分解牌");
            parts.Add(cards.Count == 0
                ? "不发秘术牌"
                : string.Join("+", cards) + $"（权重 {rules.ElementCardWeight}/{rules.FusionCardWeight}/{rules.DecomposeCardWeight}）");

            if (rules.InitialComposites != null && rules.InitialComposites.Length > 0)
                parts.Add($"开局 {rules.InitialComposites.Length} 个");

            return "开（" + string.Join("、", parts) + "）";
        }

        /// <summary>
        /// 面板上那一小段「元素牌打在空格上」的文字（图1 第 2 条）。
        /// 默认是「召唤」，所以摘要里写出来能让设计师一眼看到这一关的玩法开关状态。
        /// </summary>
        public static string EmptyCellCardText(Rules rules)
        {
            if (rules == null) return "召唤";
            return rules.EmptyCellCard == EmptyCellCardMode.Summon ? "召唤" : "拒绝（旧玩法）";
        }
    }
}
