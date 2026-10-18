using System.Collections.Generic;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Levels
{
    /// <summary>
    /// 进场景时解析关卡、实例化关卡预制体、组装 GameEngine 并交给 GameSession。
    ///
    /// 关卡来源优先级：菜单传来的（GameLaunchContext）→ 目录第一个 → 场景内 inlineLevel。
    /// 它在自己的 Awake 里跑，早于 GameSession 的 Start（Unity 保证），所以直接调 session.Begin 就行。
    ///
    /// 关卡预制体上的 ILevelRule（见 Mechanics/）会被自动收集并挂到引擎上 ——
    /// 这就是「每关不同的机制」，不需要引擎或场景为某一关写任何特判。
    /// 重开也走这里（session 通过 ILevelHost 回调 RebuildLevel），所以机制组件的计数会跟着重置。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelLoader : MonoBehaviour, ISceneWiringCheck, ILevelHost
    {
        [Header("必需引用")]
        [SerializeField] GameSession session;
        [SerializeField] Transform levelRoot;

        [Header("关卡来源（优先用菜单传入的，其次是下面这个调试用关卡）")]
        [Tooltip("调试用：填了就优先玩这一关（胜过目录第一关）。关卡资产的 Inspector 上有「Play 这一关」按钮直接帮你填。")]
        [SerializeField] LevelDefinition inlineLevel;

        [Header("关卡目录（直接 Play 且没填 inlineLevel 时用第一关）")]
        [SerializeField] LevelCatalog catalog;

        public LevelDefinition CurrentLevel { get; private set; }
        public int CurrentSeed { get; private set; }

        /// <summary>
        /// 目录里「当前这一关的下一关」，在每次 <see cref="RebuildLevel"/> 时刷新（目录是静态资产，不用每帧算）。
        /// null = 没有下一关 → HUD 的按钮置灰。
        /// </summary>
        public LevelDefinition NextLevel { get; private set; }

        /// <summary>当前关卡预制体的实例（重开时会被销毁重建）。</summary>
        public GameObject CurrentInstance { get; private set; }

        /// <summary>当前生效的关卡机制，按挂载顺序。</summary>
        public IReadOnlyList<ILevelRule> CurrentRules => _rules;

        readonly List<ILevelRule> _rules = new List<ILevelRule>();

        void Awake()
        {
            if (!CheckRequiredRefs()) return;

            var level = ResolveLevel();
            if (level == null)
            {
                Debug.LogError("[LevelLoader] 没有可用的关卡：菜单没传、目录是空的、场景里也没指 inlineLevel。");
                return;
            }

            LoadLevel(level, InitialSeed());
        }

        /// <summary>
        /// 装载一关并开一局。
        ///
        /// Awake 走「<see cref="ResolveLevel"/> + <see cref="InitialSeed"/>」这条路调它；
        /// 测试 / 「下一关」之外的换关入口也可以直接指定关卡调它 ——
        /// 不用反射去碰私有的 Awake，也保证「登记宿主 + 订阅结算」不会被漏掉。
        /// </summary>
        public void LoadLevel(LevelDefinition level, int seed)
        {
            if (level == null || session == null || levelRoot == null) return;

            CurrentLevel = level;

            // 登记在前：GameSession.Start 里那个「没有关卡」的兜底警告就不会误报，
            // 之后的重开 / 下一关也会回到这里而不是丢掉关卡。
            // 订阅前先摘一次：重复装载（测试里会）不该挂两遍。
            session.SetLevelHost(this);
            session.LevelFinished -= OnLevelFinished;
            session.LevelFinished += OnLevelFinished;

            RebuildLevel(seed);
        }

        void OnDestroy()
        {
            if (session != null)
            {
                session.LevelFinished -= OnLevelFinished;
                session.SetLevelHost(null);
            }
        }

        /// <summary>
        /// 按当前关卡重新开一局：销毁旧的关卡预制体实例（机制组件的计数一起归零）、
        /// 重新实例化并收集机制、重建引擎。
        /// </summary>
        public void RebuildLevel(int seed)
        {
            if (CurrentLevel == null || session == null || levelRoot == null) return;

            if (CurrentInstance != null)
            {
                Destroy(CurrentInstance);
                CurrentInstance = null;
            }

            CurrentSeed = SeedForRequest(seed);
            CurrentInstance = InstantiateLevelPrefab();

            CollectRules(CurrentInstance);
            WarnAuthoringIssues();

            // 规则只在这里补一次默认值（旧资产里没有元素池/牌池字段 → 反序列化出来是空），
            // 之后解析布局和建引擎都用这同一份，避免「解析用空池、引擎用全部池」这种不一致。
            var rules = CurrentLevel.rules.Clone();
            rules.Validate();

            // 初始复合元素挂在 rules 上（不是挂在 LevelSetup 上）：这一关的布局可能留空走随机铺盘，
            // 那时 BuildSetup 返回 null，但初始复合元素照样要摆上棋盘。
            rules.InitialComposites = CurrentLevel.initialComposites ?? new CompositePlacement[0];

            var setup = BuildSetup(CurrentLevel, rules);
            var engine = new GameEngine(rules, CurrentSeed);
            engine.AttachRules(_rules);
            // 回合事件池：只有在关卡勾了 useEvents 时才挂 —— 没勾的关卡拿到的引擎和加事件系统之前逐位一致。
            engine.AttachEventPool(CurrentLevel.useEvents ? CurrentLevel.roundEvents : null);
            engine.Start(setup);

            // 「下一关」也在这一步刷新：CurrentLevel 刚变过，现在算出来的一定是对的那一关。
            NextLevel = catalog != null ? catalog.NextAfter(CurrentLevel.levelId) : null;

            session.Begin(engine, CurrentSeed);
        }

        // ---------------------------------------------------------------- 换关（HUD 的「下一关」）

        /// <summary>HUD 顶栏显示的「现在在玩哪一关」。</summary>
        public string CurrentLevelName => CurrentLevel == null ? null : DisplayNameOf(CurrentLevel);

        public bool HasNextLevel => NextLevel != null;

        public string NextLevelName => NextLevel == null ? null : DisplayNameOf(NextLevel);

        /// <summary>
        /// 切到下一关（目录里当前关的下一关，最后一关绕回第一关）。
        ///
        /// 逐条说明：
        ///   · **种子**：传 0 给 <see cref="RebuildLevel"/> = 「这一关该用什么种子就用什么」——
        ///     要求固定种子的关卡仍然用 fixedSeed，其余换一个新随机种子（不像「重开」是复现同一局）
        ///   · **启动上下文**：顺手把 <see cref="GameLaunchContext"/> 也指到下一关，
        ///     这样万一之后有人重进场景（`SceneFlow.ReloadLevel`），进的还是下一关而不是最初那一关
        ///   · **进度**：不写任何通关记录 —— 换关是导航，不是胜利
        /// </summary>
        public bool GoToNextLevel()
        {
            if (NextLevel == null || session == null || levelRoot == null) return false;

            CurrentLevel = NextLevel;
            GameLaunchContext.Set(CurrentLevel, 0);
            RebuildLevel(0);
            return true;
        }

        /// <summary>关卡名：优先 displayName，没填就用 levelId（按钮上总要有字）。</summary>
        static string DisplayNameOf(LevelDefinition level)
            => string.IsNullOrEmpty(level.displayName) ? level.levelId : level.displayName;

        GameObject InstantiateLevelPrefab()
        {
            if (CurrentLevel.levelPrefab == null || levelRoot == null) return null;
            return Instantiate(CurrentLevel.levelPrefab, levelRoot, false);
        }

        /// <summary>
        /// 从**实例**上收集机制（不能从资产上收：机制带运行时状态，写到资产里就串关了）。
        /// includeInactive = true，允许把机制挂在默认隐藏的子物体上。
        /// </summary>
        void CollectRules(GameObject instance)
        {
            _rules.Clear();
            if (instance == null) return;

            var found = instance.GetComponentsInChildren<ILevelRule>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                if (found[i] is MonoBehaviour mb && !mb.enabled) continue; // 关掉的组件当成没挂
                _rules.Add(found[i]);
            }
        }

        /// <summary>
        /// 用关卡自己的校验器报问题（和编辑器面板、菜单「校验所有关卡」是同一套检查）。
        /// 只在进关时报一次，所以不会刷屏；但每一局重开都会再报一次，避免你改完没注意到。
        /// </summary>
        void WarnAuthoringIssues()
        {
            var issues = new List<string>();
            CurrentLevel.CollectAuthoringIssues(issues);
            if (issues.Count == 0) return;

            foreach (var issue in issues)
                Debug.LogError($"[LevelLoader] 关卡「{CurrentLevel.displayName}」({CurrentLevel.levelId})：{issue}");

            Debug.LogError("[LevelLoader] 上面是这一关的配置问题。菜单 TapGJ ▸ 校验所有关卡 可以一次看完所有关卡，"
                           + "选中关卡资产时 Inspector 里也会实时显示。");
        }

        /// <summary>
        /// 赢了就写进度（给菜单同学解锁用），并把这一关的剧情文本交给 <see cref="LevelOutro"/>。
        ///
        /// ⚠ 剧情要在**任何提前返回之前**写：没有 levelId 的调试关卡也能有剧情。
        /// </summary>
        void OnLevelFinished(bool victory, string message)
        {
            if (CurrentLevel == null) return;

            LevelOutro.Set(victory ? CurrentLevel.outroText : null);

            if (!victory || string.IsNullOrEmpty(CurrentLevel.levelId)) return;
            if (session == null || session.Engine == null) return;

            ProgressStore.MarkCleared(CurrentLevel.levelId, session.Engine.Round);
        }

        /// <summary>
        /// 关卡来源优先级：
        ///   1. 菜单传来的（GameLaunchContext）—— 正常游戏流程走这条
        ///   2. inlineLevel —— 调试用，「我想直接玩这一关」时它优先于目录
        ///   3. 目录第一关 —— 直接按 Play 时的默认关卡
        /// </summary>
        LevelDefinition ResolveLevel()
        {
            if (GameLaunchContext.HasLevel) return GameLaunchContext.Level;
            if (inlineLevel != null) return inlineLevel;
            if (catalog != null && catalog.levels != null && catalog.levels.Length > 0 && catalog.levels[0] != null)
                return catalog.levels[0];
            return null;
        }

        /// <summary>
        /// 这一关第一次开局用哪个种子：关卡要求固定种子 → 菜单传进来的 → 0（表示随机）。
        /// </summary>
        int InitialSeed()
        {
            if (CurrentLevel != null && CurrentLevel.useFixedSeed) return CurrentLevel.fixedSeed;
            if (GameLaunchContext.HasLevel && GameLaunchContext.Seed != 0) return GameLaunchContext.Seed;
            return 0;
        }

        /// <summary>
        /// 一次「重建」用哪个种子。
        /// 固定种子的关卡永远用 fixedSeed（谜题关被随机化就没意义了）；
        /// 其余情况请求什么用什么，请求 0（没指定）才去随机。
        /// 重开同一关走的是 session.Restart(Seed) → 请求的是当前种子，所以能复现同一局。
        /// </summary>
        int SeedForRequest(int requested)
        {
            if (CurrentLevel != null && CurrentLevel.useFixedSeed) return CurrentLevel.fixedSeed;
            return requested != 0 ? requested : Random.Range(1, int.MaxValue);
        }

        /// <summary>
        /// 用已经补好默认值的 <paramref name="rules"/>（不是关卡资产上那份原始数据）来解析布局，
        /// 并把可用元素一起传进去：布局里画了池外元素会当场当成解析错误报出来。
        /// </summary>
        static LevelSetup BuildSetup(LevelDefinition level, Rules rules)
        {
            // 布局留空 = 随机铺盘（走 GameEngine 的默认路径，只铺本关可用元素）
            if (string.IsNullOrWhiteSpace(level.layoutText)) return null;

            if (BoardLayoutParser.TryParse(level.layoutText, rules.BoardWidth, rules.BoardHeight,
                    rules.ElementPool, out var setup, out var error))
            {
                setup.InitialHand = level.fixedInitialHand;
                return setup;
            }

            Debug.LogError($"[LevelLoader] 关卡 {level.displayName}（{level.levelId}）布局解析失败，退回随机铺盘：{error}");
            return null;
        }

        // ---------------------------------------------------------------- 接线检查

        public void CollectWiringIssues(List<WiringIssue> into)
        {
            CheckWiring(session, levelRoot, into);
        }

        public static void CheckWiring(GameSession session, Transform levelRoot, List<WiringIssue> into)
        {
            Wiring.Require(session, nameof(LevelLoader), nameof(session),
                "把 Systems/GameSession 拖进来。", into);
            Wiring.Require(levelRoot, nameof(LevelLoader), nameof(levelRoot),
                "把 LevelRoot 拖进来（关卡预制体挂在它下面）。", into);
        }

        bool CheckRequiredRefs()
        {
            var issues = new List<WiringIssue>();
            CollectWiringIssues(issues);
            if (issues.Count == 0) return true;

            foreach (var issue in issues)
                Debug.LogError($"[TapGJ] {issue}");

            Debug.LogError("[TapGJ] LevelLoader 缺少必需引用，已停用。菜单 TapGJ ▸ 校验当前场景 会列出所有问题。");
            enabled = false;
            return false;
        }
    }
}
