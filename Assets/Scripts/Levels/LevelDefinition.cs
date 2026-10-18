using System.Collections.Generic;
using System.Text;
using TapGJ.Core;
using TapGJ.Events;
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

        [Tooltip("通关后显示的剧情/过渡文本（留空 = 没有）。会被 LevelLoader 在胜利那一刻记到 LevelOutro 上，" +
                 "由局内 UI 的结算面板展示；「过渡关卡」就是靠它把玩家引到下一章。")]
        [TextArea(2, 8)] public string outroText;

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

        [Header("回合事件")]
        [Tooltip("勾上：这一关会每隔 rules.EventIntervalMin~Max 回合随机触发一条事件（第 1 回合不触发）。" +
                 "不勾：完全没有事件，行为和加事件系统之前一致")]
        public bool useEvents;

        [Tooltip("本关启用的事件池：只从这里面抽。留空（同时勾了上面的开关）= 一个事件都不会有，" +
                 "Inspector 会报出来。想按关裁剪就少放几条，想全用就把目录里的都拖进来")]
        public RoundEventDefinition[] roundEvents = new RoundEventDefinition[0];

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

            // 开局初始复合元素是**关卡层**的字段（`LevelDefinition.initialComposites`）：
            // LevelLoader 进关时才把它拷进 `rules.InitialComposites`（见 LevelLoader.BuildSetup）。
            // 校验必须做**同一个拷贝**，否则「这一关摆了初始复合元素」在规则副本里根本看不到，
            // 于是 `Rules.CollectIssues` 会误报「总开关白勾」「分解牌永远用不出去」两条假警告。
            effective.InitialComposites = initialComposites ?? new CompositePlacement[0];

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
            CollectEventIssues(effective, into);

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

        /// <summary>
        /// 回合事件这一块的检查：开关与池子配没配一致、每条事件在本关适不适用、回合上限够不够撞上第一次事件。
        ///
        /// 「适不适用」直接问事件自己的 <see cref="RoundEventDefinition.IsApplicable"/> ——
        /// 也就是运行期抽取时用的同一套判断，所以 Inspector 里报的「会被跳过」和 Play 时跳过的**必然是同一批**。
        /// 为了让它判断得准，这里会真的开一局（引擎只在内存里跑，不碰场景），否则「场上一个小怪都没有」
        /// 这类判断在空棋盘上永远成立、每条都误报。
        /// </summary>
        void CollectEventIssues(Rules effective, List<string> into)
        {
            bool hasPool = roundEvents != null && roundEvents.Length > 0;

            if (!useEvents)
            {
                if (hasPool)
                    into.Add($"配了 {roundEvents.Length} 条回合事件，但 useEvents 没勾 → 它们永远不会触发。"
                             + "要玩就勾上；确定不要就顺手清掉，免得以后误以为它们生效了。");
                return;
            }

            if (!hasPool)
            {
                into.Add("勾了回合事件开关（useEvents），但事件池是空的 → 这一关一个事件都不会有。"
                         + "要么把事件资产拖进 roundEvents，要么把开关关掉。");
                return;
            }

            var seenIds = new HashSet<string>();
            foreach (var ev in roundEvents)
            {
                if (ev == null)
                {
                    into.Add("事件池里有空槽位。");
                    continue;
                }

                if (!seenIds.Add(ev.Id))
                    into.Add($"事件池里「{ev.DisplayName}」出现了两次（同一个 Id「{ev.Id}」）—— "
                             + "「本局不重复」是按 Id 记的，重了等于只有一条。");
            }

            int min = System.Math.Max(1, effective.EventIntervalMin);
            int max = System.Math.Max(min, effective.EventIntervalMax);
            int earliest = 1 + min;
            int latest = 1 + max;

            if (earliest > effective.RoundLimit)
            {
                into.Add($"回合上限 {effective.RoundLimit}，而事件最早也要第 {earliest} 回合才触发"
                         + $"（第 1 回合不触发 + 间隔 {min}~{max}）→ 这一关永远不会有事件。"
                         + "要么把回合上限调大，要么把 rules.EventIntervalMin/Max 调小（短关卡建议 2~3）。");
            }
            else if (latest > effective.RoundLimit)
            {
                into.Add($"回合上限 {effective.RoundLimit}，但事件最晚可能第 {latest} 回合才第一次触发 → "
                         + "有些局可能一个事件都碰不到。想让节奏更稳就把 EventIntervalMax 调小。");
            }

            var probe = BuildProbeEngine(effective);
            if (probe == null) return;

            foreach (var ev in roundEvents)
            {
                if (ev == null) continue;
                if (ev.IsApplicable(probe, out var reason)) continue;

                into.Add($"事件「{ev.DisplayName}」在本关不适用：{reason} → 抽到会被跳过（换下一条、不算抽过）。");
            }
        }

        /// <summary>
        /// 开一局**只在内存里**的引擎，专门用来问「事件在本关适不适用」。
        /// 用和真正进关同一套解析路径（字符画布局 / 随机铺盘），所以棋盘状态和实际开局一致。
        /// 失败返回 null（此时上面已经报过配置错误了，不必重复报）。
        /// </summary>
        GameEngine BuildProbeEngine(Rules effective)
        {
            try
            {
                LevelSetup setup = null;
                if (!string.IsNullOrWhiteSpace(layoutText))
                {
                    if (!BoardLayoutParser.TryParse(layoutText, effective.BoardWidth, effective.BoardHeight,
                            effective.ElementPool, out setup, out _))
                        setup = null;
                }

                var engine = new GameEngine(effective, 1);
                engine.Start(setup);
                return engine;
            }
            catch (System.Exception)
            {
                return null;
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
            sb.Append(useEvents
                ? $"　事件 {(roundEvents == null ? 0 : roundEvents.Length)} 条（每 {rules.EventIntervalMin}~{rules.EventIntervalMax} 回合）"
                : "　事件 关");
            if (!rules.BottlesEnabled) sb.Append("　无元素瓶");
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

            if (rules.SpecialCardUnlockRound > 1)
                parts.Add($"第 {rules.SpecialCardUnlockRound} 回合起才发");

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
