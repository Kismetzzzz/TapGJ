using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 打出元素牌的作用方式。图1 只写了「对小怪使用卡牌（选择卡牌，点击元素格的格子使用，视为发生作用）」，
    /// 没说清牌面元素与目标元素的关系，这里给出三种可切换的解释，默认第 1 种（与图2 的表自洽）。
    /// </summary>
    public enum CardEffectMode
    {
        /// <summary>
        /// 默认：牌 = 一个虚拟元素，与目标格上的元素按图2 的表发生反应。
        /// 相克对：牌面元素克制目标 → 目标直接消失；反过来这张牌打出去场上无变化，所以不能出。
        /// 相生对：牌面元素赢 → 目标就地变成牌面元素，并在旁边生成 1 个牌面元素；
        ///         目标元素赢 → 目标不变，牌自己变成目标元素落到旁边。
        /// 同元素（如金×金）不发生反应，该次出牌不合法。
        /// </summary>
        CardAsVirtualElement = 0,

        /// <summary>牌只能打在同元素的小怪上：该小怪消失，并按「牌面元素 × 牌面元素」的规则生成（默认不生成）。</summary>
        SameElementOnly = 1,

        /// <summary>牌只能打在同元素的小怪上：该小怪消失，并生成 1 个同元素（净效果 = 格子平移）。</summary>
        SameElementSpawnOne = 2,
    }

    /// <summary>
    /// 元素牌打在**空格**上的处理方式。图1 玩家行动阶段的第 2 条写得明确：
    /// 「玩家可以对空的棋盘格使用卡牌，可以在该格召唤该元素小怪」。
    ///
    /// ⚠ <see cref="Summon"/> 故意是 0：加这个字段之前存下来的关卡 / 场景资产反序列化出来就是 0，
    /// 于是**旧资产自动拿到图1 的新规则**，不需要挨个改资产。想回到「空格打不出去」就选 <see cref="Reject"/>。
    /// </summary>
    public enum EmptyCellCardMode
    {
        /// <summary>默认：元素牌打在空格上 → 在该格召唤 1 个牌面元素的小怪（图1 第 2 条）。</summary>
        Summon = 0,

        /// <summary>旧行为：空格不是合法目标，这次出牌被拒绝（不消耗手牌）。</summary>
        Reject = 1,
    }

    /// <summary>
    /// 秘术牌（融合牌 / 分解牌）的集合（位掩码）。
    /// Inspector 会渲染成多选下拉框，和 <see cref="ElementMask"/> 一样是给关卡作者的开关。
    /// </summary>
    [Flags]
    public enum SpecialCardMask
    {
        None = 0,
        /// <summary>融合牌：把相邻的两个元素合成一个复合元素。</summary>
        Fusion = 1 << 0,
        /// <summary>分解牌：把场上的复合元素拆回两张元素牌。</summary>
        Decompose = 1 << 1,
        All = Fusion | Decompose,
    }

    /// <summary>SpecialCardMask 的显示工具（纯 C#，和 <see cref="ElementSet"/> 同一个套路）。</summary>
    public static class SpecialCardSet
    {
        /// <summary>掩码里有没有这一种。</summary>
        public static bool Has(this SpecialCardMask mask, SpecialCardMask one) => (mask & one) != 0;

        /// <summary>
        /// 人话：「融合」「分解」「融合+分解」/「（空）」—— 报错文案与 Inspector 都用它。
        ///
        /// ⚠ 判定一律**按位**，不要用 `mask == SpecialCardMask.All` 这种整体比较：
        /// Unity 的多选下拉框在「两个都勾上」时写进来的是 **-1**（所有位都置 1，包括没定义的高位），
        /// 整体比较会把它看成「只有融合」，报错文案就会漏说分解牌。
        /// </summary>
        public static string Names(this SpecialCardMask mask)
        {
            bool fusion = mask.Has(SpecialCardMask.Fusion);
            bool decompose = mask.Has(SpecialCardMask.Decompose);

            if (fusion && decompose) return "融合+分解";
            if (fusion) return "融合";
            if (decompose) return "分解";
            return "（空）";
        }
    }

    /// <summary>
    /// 全部可调数值集中在这里，改规则不用翻代码。
    /// </summary>
    [Serializable]
    public class Rules
    {
        public int BoardWidth = 8;
        public int BoardHeight = 8;

        /// <summary>
        /// 本关**会出现**的元素（默认全部）。它同时约束三件事：
        /// 随机铺盘只用这些元素、字符画布局只允许这些字符、机制 SpawnAt 也只生成这些元素。
        ///
        /// 留空（None）会被 <see cref="Validate"/> 修成「全部」——
        /// 这样加字段之前就存在的场景 / 关卡资产（反序列化出来是 0）仍然是「五种元素都有」。
        /// </summary>
        public ElementMask ElementPool = ElementMask.All;

        /// <summary>
        /// 本关**能抽到**的牌（默认全部）。必须是 <see cref="ElementPool"/> 的子集：
        /// 打出池外元素的牌发生相生时，会把棋盘上的元素变成池外元素，
        /// 于是「本关只有金水」这个约定就被破坏了。这条会在 <see cref="CollectIssues"/> 里被指出来。
        /// </summary>
        public ElementMask CardPool = ElementMask.All;

        /// <summary>每种初始元素在棋盘上生成几个（5 种 × 4 个 = 20 个，占 64 格的 31%）。</summary>
        public int CreepsPerElement = 4;

        /// <summary>准备阶段每只小怪每回合的随机移动步数上限（0~MaxMoveSteps）。</summary>
        public int MaxMoveSteps = 3;

        /// <summary>玩家初始手牌数量。</summary>
        public int InitialHandSize = 5;

        /// <summary>
        /// 手牌上限。抽牌阶段会把手牌补到这个数量；弃牌阶段要把手牌弃回这个数量以内。
        /// 图1 只写了「抽牌直至 5 张」「弃牌直至至多 5 张」，两处都用 5，所以这里当作同一个上限。
        /// </summary>
        public int HandLimit = 5;

        /// <summary>弃牌阶段每回合最多能弃几张（图1：「弃牌直至至多 5 张」）。</summary>
        public int MaxDiscardsPerRound = 5;

        /// <summary>
        /// 玩家行动阶段每回合最多能打出几张牌。0 = 不限制（当前默认，与图1 的字面一致）。
        ///
        /// 实测（60 局，见 README 第四节）：相生/相克语义修正之后「不限」已经不再是问题
        /// （只剩一种元素 78%、只剩水元素 33%）。上限主要用来调节奏：
        /// 4 张两种条件都最好；1 张太难（24/60、13/60）。
        /// </summary>
        public int MaxCardsPerRound = 0;

        /// <summary>元素小瓶子容量，满瓶换算成 1 张对应元素牌。</summary>
        public int BottleCapacity = 5;

        // ------------------------------------------------------------ 回合事件
        // 「每隔 min~max 回合随机来一条本关启用的事件」（第 1 回合不触发，第一次落在 1+[min,max] 回合）。
        // 两个数值留在这里（而不是 LevelDefinition）是因为 Rules 是纯 C#、能被批量跑批与测试直接用；
        // 「本关启用哪些事件」是一组资产引用，只能放在 LevelDefinition 上。

        /// <summary>事件间隔下限（回合）。默认 3。</summary>
        public int EventIntervalMin = 3;

        /// <summary>事件间隔上限（回合）。默认 6 —— 15 回合的一局大概能碰到 2~3 次。</summary>
        public int EventIntervalMax = 6;

        /// <summary>回合上限，超过即失败。</summary>
        public int RoundLimit = 15;

        /// <summary>
        /// 胜利目标（种类 + 参数）。改这里就能换每关的胜负条件。
        /// 选 <see cref="GoalKind.Custom"/> 时内置判定完全不参与，改由关卡预制体上的 ILevelRule 决定。
        /// </summary>
        public GoalSpec Goal = GoalSpec.WaterOnly();

        /// <summary>元素牌的作用方式（图1 未定义清楚，见 CardEffectMode 注释）。</summary>
        public CardEffectMode CardEffect = CardEffectMode.CardAsVirtualElement;

        /// <summary>
        /// 元素牌打在**空格**上怎么办。图1 第 2 条：「玩家可以对空的棋盘格使用卡牌，
        /// 可以在该格召唤该元素小怪」—— 默认 <see cref="EmptyCellCardMode.Summon"/> 就是照它实现的。
        ///
        /// 选了召唤之后，一张元素牌有两个用途（两个都占每回合出牌上限、都消耗这张牌）：
        ///   · 打在元素格上 → 图2 的反应（原有玩法）
        ///   · 打在空格上   → 在该格召唤 1 个牌面元素的小怪（新玩法）
        /// 墙格、复合元素占着的格子都不是空格，两种都用不了。
        ///
        /// 想回到「只能反应」的老玩法就把这一项选成 <see cref="EmptyCellCardMode.Reject"/>
        /// （配置校验不管这一项：它是玩法选择，不是死局）。
        /// </summary>
        public EmptyCellCardMode EmptyCellCard = EmptyCellCardMode.Summon;

        // ------------------------------------------------------------ 秘术牌 / 复合元素

        /// <summary>
        /// **复合元素这一整套玩法的总开关**。
        ///
        /// 不勾 = 这一关**完全没有**复合元素，也没有秘术牌：
        ///   · 抽牌不会抽到融合 / 分解牌（<see cref="SpecialCardPool"/> 与三个权重一律不生效）
        ///   · 场上不会出现复合元素，<see cref="InitialComposites"/> 会被忽略
        ///   · 融合牌 / 分解牌用不出去（连"用不出去的原因"都直接指向这个开关）
        ///
        /// 勾上之后再用 <see cref="SpecialCardPool"/> 挑要哪几张秘术牌、用权重定抽牌比例。
        ///
        /// ⚠ 默认 false = 与加这一层玩法之前**逐位一致**；旧关卡资产反序列化出来也是 false，
        /// 所以不需要任何兼容代码。想让某一关玩，就在它的 Rules 里把这个勾上。
        /// </summary>
        public bool UseCompositeElements;

        /// <summary>
        /// 本关**能抽到**哪些秘术牌（只有 <see cref="UseCompositeElements"/> 勾上时才生效）。
        ///
        /// ⚠ 默认 <see cref="SpecialCardMask.None"/>。这样「总开关没勾 + 池子是空的」就是一个
        /// **完全没配过这一层玩法**的干净状态 —— 配置校验才不会把一个刚建的关卡报成
        /// 「你配了秘术牌却忘了勾开关」。编辑器里勾总开关时会**自动把它填成两张都开**，
        /// 所以正常流程下关卡作者不需要手填这个字段。
        /// </summary>
        public SpecialCardMask SpecialCardPool = SpecialCardMask.None;

        /// <summary>
        /// 抽牌权重的基准单位：元素牌里每张元素的权重。
        /// 「比例可控」就是靠这一组权重实现的 —— 抽牌时按权重挑「这一张是元素牌还是哪种秘术牌」，
        /// 挑中元素牌之后再从 <see cref="CardPool"/> 里均匀取一种元素。
        ///
        /// 例：元素牌权重 10、融合牌权重 10 → 手牌里约一半是融合牌。
        /// </summary>
        public int ElementCardWeight = 10;

        /// <summary>融合牌的抽牌权重（相对 <see cref="ElementCardWeight"/>）。0 = 抽不到。</summary>
        public int FusionCardWeight = 10;

        /// <summary>分解牌的抽牌权重（相对 <see cref="ElementCardWeight"/>）。0 = 抽不到。</summary>
        public int DecomposeCardWeight = 5;

        /// <summary>
        /// 复合元素「产生」效果的作用范围（切比雪夫距离）：
        /// 1 = 紧邻的 8 格（3×3 去掉自己），2 = 5×5。
        /// 图2 写的是「进入范围内生产」，范围多大原表没写死，所以做成可调。
        /// </summary>
        public int CompositeRange = 1;

        /// <summary>
        /// 复合元素每回合给自己**每一种**组成元素生产的份数（图2 写的是 4：木+4、火+4）。
        /// 一份 = 一个元素小怪。
        /// </summary>
        public int CompositeIngredientsPerRound = 4;

        /// <summary>
        /// 融合出来的两个元素必须**十字相邻**（上下左右紧挨着）才能合成。
        ///
        /// ⚠ 是**十字 4 格**，不是周围 8 格 —— 斜角摆着的两只元素合不了。
        /// 图2 的示意图给的就是十字范围。关掉这个开关就等于「全盘任意两只都能合」。
        /// </summary>
        public bool FusionRequiresAdjacency = true;

        /// <summary>
        /// 允不允许**场上同时存在多个复合元素**。
        ///
        /// · <c>false</c>（默认）：融合牌要求场上**一个复合元素都没有** —— 想再合一个得先用分解牌拆掉。
        ///   这是设计原文「如果开局场上不存在复合元素则必须通过融合牌来获得复合元素」的反面读法。
        /// · <c>true</c>：融合牌只要求**场上的复合元素里没有同一种**的
        ///   （寒钢已经有了就不能再合寒钢，但可以再合一个熔岩）。
        ///
        /// ⚠ 两种模式下**都没有数量上限** —— 唯一的限制就是这条前置条件本身。
        /// 另外 <see cref="InitialComposites"/> 摆几个都不受它管（开局想摆 4 个也行）。
        /// </summary>
        public bool AllowMultipleComposites;

        /// <summary>
        /// 复合元素「产生」时，两种组成元素的**产出顺序**：
        ///
        /// · <c>true</c>（默认）**随机交错** —— 两种元素按随机顺序交替产出，
        ///   不会出现「先把木铺满最近的一圈、再铺火」这种明显的一坨一坨。
        /// · <c>false</c> **按配方顺序依次产完** —— 图2 表格那句「生产：木+4、火+4」的字面读法。
        ///
        /// 两种都**用引擎自己的随机源**，所以同一个种子完全可复现（不是真随机）。
        /// </summary>
        public bool CompositeRandomProductionOrder = true;

        /// <summary>
        /// 复合元素在玩家行动阶段合出来时，**当回合立刻产生一次**（图2 的要求），
        /// 之后每回合开始再产生一次。关掉的话只能等下一回合。
        /// </summary>
        public bool CompositeProducesOnCreation = true;

        /// <summary>
        /// 分解牌：拆掉复合元素时给不给回两张**对应元素的元素牌**。
        /// 关掉就只是把复合元素从场上移除（用于「分解是纯解场」的关卡）。
        /// </summary>
        public bool DecomposeGrantsElementCards = true;

        /// <summary>
        /// 开局就摆在棋盘上的复合元素。
        /// 默认空 → 开局**没有**复合元素，此时只能靠融合牌现场合一个（这就是玩法要求的起点）。
        /// 空数组也是旧资产反序列化出来的样子，所以行为与加这个玩法之前完全一致。
        /// </summary>
        public CompositePlacement[] InitialComposites = new CompositePlacement[0];

        public Rules Clone() => (Rules)MemberwiseClone();

        public int CellCount => BoardWidth * BoardHeight;

        /// <summary>本关出现的元素（固定顺序：金木水火土）。</summary>
        public Element[] PoolElements => ElementPool.ToArray();

        /// <summary>本关能抽到的牌面元素（固定顺序）。</summary>
        public Element[] PoolCards => CardPool.ToArray();

        public int PoolElementCount => ElementPool.Count();

        public int InitialCreepCount => CreepsPerElement * PoolElementCount;

        /// <summary>
        /// 本关开着的秘术牌种类数（总开关没勾时恒为 0 —— 面板上「这一关到底有没有秘术牌」看它）。
        /// </summary>
        public int SpecialCardKindCount
        {
            get
            {
                if (!UseCompositeElements) return 0;

                int n = 0;
                if ((SpecialCardPool & SpecialCardMask.Fusion) != 0) n++;
                if ((SpecialCardPool & SpecialCardMask.Decompose) != 0) n++;
                return n;
            }
        }

        /// <summary>
        /// 本关的秘术牌池里有没有这一种。
        /// ⚠ 总开关没勾时一律 false —— 调用方不用自己记得先判 <see cref="UseCompositeElements"/>。
        /// </summary>
        public bool HasSpecialCard(CardKind kind)
        {
            if (!UseCompositeElements) return false;

            switch (kind)
            {
                case CardKind.FusionCard: return (SpecialCardPool & SpecialCardMask.Fusion) != 0;
                case CardKind.DecomposeCard: return (SpecialCardPool & SpecialCardMask.Decompose) != 0;
                default: return false;
            }
        }

        /// <summary>
        /// 总开关勾着、但这一层其实是空的：没选任何可抽的秘术牌，开局也没摆初始复合元素。
        /// 表现是「设置全都勾好了，游戏里却一个复合元素都见不到」—— 值得报一句。
        /// </summary>
        public bool CompositeFeatureEnabledButInert
        {
            get
            {
                if (!UseCompositeElements) return false;
                if (InitialComposites != null && InitialComposites.Length > 0) return false;
                return DrawableSpecialCards().Length == 0;
            }
        }

        /// <summary>复合元素每回合生产多少份元素（两种组成元素各 N 份 → 总数 2N）。</summary>
        public int CompositeProducedPerRound => Math.Max(0, CompositeIngredientsPerRound) * 2;

        /// <summary>
        /// 本关**有没有可能拿到复合元素**（总开关勾着，并且"开局就摆了初始复合元素"
        /// 或"融合牌开着且元素池里存在能配对的两种元素"）。
        ///
        /// 用途：复合元素是这一套玩法唯一的产出物 —— 拿不到它就等于「融合牌和分解牌都是死牌」。
        /// </summary>
        public bool CanEverGetComposite
        {
            get
            {
                if (!UseCompositeElements) return false;

                return (InitialComposites != null && InitialComposites.Length > 0)
                       || (HasSpecialCard(CardKind.FusionCard) && FusionRecipes.AnyPairAvailable(ElementPool));
            }
        }

        /// <summary>
        /// **本关实际能抽到的秘术牌**。总开关没勾就直接空数组；勾了之后两种牌各多一个条件，
        /// 否则抽到手里也永远用不出去（那是死牌，不该进牌堆）：
        ///   · 融合牌：元素池里必须真的存在能配对的两种元素
        ///   · 分解牌：本关必须有可能出现复合元素（否则场上永远没有它的目标）
        /// 顺序固定：融合、分解（保证同种子可复现）。
        /// </summary>
        public CardKind[] DrawableSpecialCards()
        {
            var list = new List<CardKind>(2);
            if (!UseCompositeElements) return list.ToArray();

            bool canGetComposite = CanEverGetComposite;

            if (canGetComposite
                && HasSpecialCard(CardKind.FusionCard)
                && FusionRecipes.AnyPairAvailable(ElementPool)
                && Math.Max(0, FusionCardWeight) > 0)
                list.Add(CardKind.FusionCard);

            if (canGetComposite
                && HasSpecialCard(CardKind.DecomposeCard)
                && Math.Max(0, DecomposeCardWeight) > 0)
                list.Add(CardKind.DecomposeCard);

            return list.ToArray();
        }

        /// <summary>牌堆里到底有没有能抽的牌（元素牌池非空、或至少有一种能抽的秘术牌）。</summary>
        public bool HasAnyDrawableCard()
            => PoolCards.Length > 0 || DrawableSpecialCards().Length > 0;

        /// <summary>
        /// 这种元素**能不能靠玩家出牌**出现在棋盘上？能就返回一句人话（说明是哪条路），不能返回 null。
        ///
        /// 用途：关卡校验里那些「布局里没有它 / 池子里没有它 → 永远赢不了」的判断。
        /// ⚠ **光看字符画布局是不够的** —— 图1 的两条出牌规则都能把新元素放到场上，
        /// 只按布局判会误报（`EqualElementCountsRule` 的「必须都在场」就踩过：
        /// 布局里没写木，但玩家打一张木牌就能造出木）。所以这类检查一律走这里问一次。
        ///
        /// 两条路（前提都是这张牌真的抽得到：<see cref="CardPool"/> 里有它、<see cref="ElementCardWeight"/> &gt; 0）：
        ///   · **空格召唤**（<see cref="EmptyCellCard"/> = Summon）：牌面元素就是它，打在空格上直接多一只
        ///   · **相生**：把它作为**赢家**打在场上已有的某种元素上 —— 目标就地变成它，旁边再生成一只。
        ///     反过来（它是相生里输的那一方 / 相克里输的那一方）时，牌面元素**不会**出现在场上，不算。
        ///
        /// <paramref name="boardElements"/> 传「开局就在场上的元素」（布局里画了什么）：
        /// 相生那一条要先有目标小怪才谈得上。
        /// </summary>
        public string CardSourceFor(Element element, IEnumerable<Element> boardElements)
        {
            // 元素牌都发不出来（权重 0）或这张牌不在牌池里 → 两条路都不成立
            if (ElementCardWeight <= 0 || !CardPool.Has(element)) return null;

            if (EmptyCellCard == EmptyCellCardMode.Summon)
                return $"可以打一张{ElementDefs.Name(element)}牌到空格上，把它召唤出来";

            if (boardElements == null) return null;

            var reactions = new ReactionTable();
            foreach (var onBoard in boardElements)
            {
                if (onBoard == element) continue;

                var def = reactions.Get(onBoard, element);
                if (def.Rule == ReactionRule.Transform && def.Winner == element)
                    return $"打一张{ElementDefs.Name(element)}牌到{ElementDefs.Name(onBoard)}元素上，会相生出它";
            }

            return null;
        }

        /// <summary>一种秘术牌的抽牌权重（负数按 0 处理）。</summary>
        public int DrawWeightOf(CardKind kind)
        {
            switch (kind)
            {
                case CardKind.FusionCard: return Math.Max(0, FusionCardWeight);
                case CardKind.DecomposeCard: return Math.Max(0, DecomposeCardWeight);
                default: return 0;
            }
        }

        public void Validate()
        {
            // 加字段之前存下来的场景/关卡反序列化出来是 None（= 一个元素都没有），
            // 这里修成「全部」，保证旧数据的行为和加字段前完全一致。
            if (ElementPool == ElementMask.None) ElementPool = ElementMask.All;
            if (CardPool == ElementMask.None) CardPool = ElementMask.All;

            // SpecialCardPool 的默认值是 All（新资产拿到「两张秘术牌都能抽」），
            // 旧资产反序列化出来是 None —— 那是「这套玩法在加之前不存在」，**故意不修正**。

            if (BoardWidth < 1) throw new ArgumentException("BoardWidth 必须 >= 1");
            if (BoardHeight < 1) throw new ArgumentException("BoardHeight 必须 >= 1");
            if (CreepsPerElement < 0) throw new ArgumentException("CreepsPerElement 不能为负");
            if (InitialCreepCount > CellCount)
                throw new ArgumentException($"初始小怪 {InitialCreepCount} 个超过棋盘 {CellCount} 格");
            if (MaxMoveSteps < 0) throw new ArgumentException("MaxMoveSteps 不能为负");
            if (InitialHandSize < 0) throw new ArgumentException("InitialHandSize 不能为负");
            if (HandLimit < 0) throw new ArgumentException("HandLimit 不能为负");
            if (MaxDiscardsPerRound < 0) throw new ArgumentException("MaxDiscardsPerRound 不能为负");
            if (MaxCardsPerRound < 0) throw new ArgumentException("MaxCardsPerRound 不能为负");
            if (BottleCapacity < 1) throw new ArgumentException("BottleCapacity 至少为 1");
            if (EventIntervalMin < 1) throw new ArgumentException("EventIntervalMin 至少为 1");
            if (EventIntervalMax < EventIntervalMin)
                throw new ArgumentException("EventIntervalMax 不能小于 EventIntervalMin");
            if (RoundLimit < 1) throw new ArgumentException("RoundLimit 至少为 1");
            if (CompositeRange < 1) throw new ArgumentException("CompositeRange 至少为 1");
            if (CompositeIngredientsPerRound < 1) throw new ArgumentException("CompositeIngredientsPerRound 至少为 1");
            if (ElementCardWeight < 0) throw new ArgumentException("ElementCardWeight 不能为负");
            if (FusionCardWeight < 0) throw new ArgumentException("FusionCardWeight 不能为负");
            if (DecomposeCardWeight < 0) throw new ArgumentException("DecomposeCardWeight 不能为负");
        }

        /// <summary>
        /// 收集「这份配置能不能玩」的问题（不抛异常，只产出人话说明）。
        ///
        /// 编辑期的关卡校验（LevelDefinition.CollectAuthoringIssues）与运行期的 LevelLoader 都调它，
        /// 所以「Inspector 里看到的问题」和「Play 时 Console 报的问题」一定是同一套。
        /// 空列表 = 这份配置没有已知的死局。
        /// </summary>
        public void CollectIssues(List<string> into)
        {
            if (into == null) return;

            if (ElementPool == ElementMask.None)
                into.Add("可用元素一个都没勾。留空会被当成「全部元素」（兼容旧数据），但建议显式勾上。");
            if (CardPool == ElementMask.None)
                into.Add("可抽卡牌一个都没勾。留空会被当成「全部元素」（兼容旧数据），但建议显式勾上。");

            // 牌池必须是元素池的子集，否则出牌相生会把池外元素带进棋盘。
            var outside = CardPool & ~ElementPool;
            if (outside != ElementMask.None)
                into.Add($"可抽卡牌里的「{outside.Names()}」不在可用元素「{ElementPool.Names()}」里。"
                         + $"打出这类牌发生相生时，会把棋盘上的元素变成{outside.Names()}，"
                         + "和「本关只出现这些元素」矛盾。要么把它加进可用元素（表示本关允许出现），要么从牌池里去掉。");

            CollectSpecialCardIssues(into);

            // 初始复合元素的坐标必须在棋盘内（越界的会被运行期跳过，但那是关卡作者写错了）。
            if (InitialComposites != null)
            {
                for (int i = 0; i < InitialComposites.Length; i++)
                {
                    var p = InitialComposites[i];
                    if (p.X < 0 || p.X >= BoardWidth || p.Y < 0 || p.Y >= BoardHeight)
                        into.Add($"初始复合元素「{CompositeDefs.Name(p.Kind)}」的坐标 ({p.X},{p.Y}) 越界"
                                 + $"（棋盘 {BoardWidth}×{BoardHeight}）→ 进关卡时会被跳过。");
                }
            }

            switch (Goal.Kind)
            {
                case GoalKind.WaterOnly when !ElementPool.Has(Element.Water):
                    into.Add("胜利目标是「全场只剩水元素」，但可用元素里没有水 → 这一关永远赢不了。");
                    break;

                case GoalKind.SingleElement when PoolElementCount <= 1:
                    into.Add($"胜利目标是「全场只剩一种元素」，而本关可用元素只有 {PoolElementCount} 种 → 开局就满足，必定立刻胜利。");
                    break;

                case GoalKind.ElementAtMost when !ElementPool.Has(Goal.Target):
                    into.Add($"胜利目标盯着{ElementDefs.Name(Goal.Target)}元素，但本关不出现它 → 场上永远是 0 个，"
                             + "即判定从第 1 回合起就成立（开局即胜）。");
                    break;

                case GoalKind.ElementAtLeast when !ElementPool.Has(Goal.Target):
                    into.Add($"胜利目标盯着{ElementDefs.Name(Goal.Target)}元素，但本关不出现它 → 场上永远是 0 个，"
                             + $"只有目标数为 0 时才可能赢（当前是 {Goal.TargetCount}）。");
                    break;

                case GoalKind.SurviveRounds when Goal.Rounds > RoundLimit:
                    into.Add($"要撑过 {Goal.Rounds} 回合，但回合上限只有 {RoundLimit} → 第 {RoundLimit} 回合就判负，永远赢不了。");
                    break;
            }
        }

        /// <summary>
        /// 秘术牌 / 复合元素这一套的配置检查（死牌、矛盾、抽不到牌）。
        /// 单独拆出来是为了让 <see cref="CollectIssues"/> 保持可读。
        /// </summary>
        void CollectSpecialCardIssues(List<string> into)
        {
            // 总开关没勾：这一关就是「没有复合元素也没有秘术牌」。
            // 这时唯一值得说的是「你是不是忘了勾」—— 配了秘术牌/初始复合元素却忘了勾总开关，
            // 表现是「设置全都填好了但游戏里什么都不出现」，很容易查半天。
            if (!UseCompositeElements)
            {
                // ⚠ 只有「**显式配过**」才算忘勾：默认值（池子空、没有初始复合元素）是
                // 「这一关压根没碰过这层玩法」的干净状态，不该报警 —— 否则刚建的关卡一打开
                // 就是一片红，报的还是「你配了秘术牌却忘了勾开关」这种莫名其妙的话。
                bool configuredCards = SpecialCardPool != SpecialCardMask.None;
                bool configuredComposites = InitialComposites != null && InitialComposites.Length > 0;

                if (configuredCards || configuredComposites)
                {
                    var what = new List<string>();
                    if (configuredCards) what.Add($"秘术牌「{SpecialCardPool.Names()}」");
                    if (configuredComposites) what.Add($"{InitialComposites.Length} 个初始复合元素");

                    into.Add($"配了{string.Join(" 和 ", what)}，但**复合元素总开关（UseCompositeElements）没勾** → "
                             + "这些配置全部不生效：抽不到秘术牌、场上也不会出现复合元素。"
                             + "要玩这一层就把总开关勾上；确定不要就顺手把这些配置清掉，免得以后误以为它们生效了。");
                }
                return;
            }

            bool wantsFusion = HasSpecialCard(CardKind.FusionCard);
            bool wantsDecompose = HasSpecialCard(CardKind.DecomposeCard);

            if (!HasAnyDrawableCard())
            {
                into.Add("牌堆里一张能抽的牌都没有：可抽卡牌一个元素都没勾，秘术牌也全关了（或权重全为 0）。"
                         + "抽牌阶段会退化成「随便给一张金元素牌」。");
                return;
            }

            // 总开关勾了，但秘术牌一张都没有、开局也没有复合元素 → 这一层玩法是空的。
            if (CompositeFeatureEnabledButInert)
                into.Add("勾了复合元素总开关，但既没选任何秘术牌（或权重全为 0）、开局也没摆初始复合元素 → "
                         + "场上永远不会有复合元素，这个开关等于白勾。"
                         + "要么至少留一张融合牌（玩家自己合），要么在 initialComposites 里摆几个。");

            // 「只允许一个复合元素」+ 开局就摆了多个 → 玩家只能靠分解牌一个个腾位置。
            // 这不是死局，但配的人多半没意识到，所以说一句。
            if (!AllowMultipleComposites && InitialComposites != null && InitialComposites.Length > 1
                && HasSpecialCard(CardKind.FusionCard))
            {
                into.Add($"开局摆了 {InitialComposites.Length} 个复合元素，但 AllowMultipleComposites 没开 → "
                         + "玩家想再用融合牌得先用分解牌把场上的都拆掉。"
                         + "如果你就是想让它同时存在多个，把这个开关打开（打开后只限制「不能合出同一种」）。");
            }

            // 融合牌：元素池里凑不出任何一对能合成的元素 → 抽到手里也永远用不出去。
            if (wantsFusion && !FusionRecipes.AnyPairAvailable(ElementPool))
            {
                if (PoolElementCount <= 1)
                    into.Add($"开了融合牌，但本关可用元素只有「{ElementPool.Names()}」一种 → 融合需要两种不同的元素，"
                             + "这些牌永远用不出去（抽牌时会自动跳过融合牌）。要么多加一种元素，要么关掉融合牌。");
                else
                    into.Add($"开了融合牌，但可用元素「{ElementPool.Names()}」里没有能配成复合元素的组合"
                             + "（配方：金+水、水+木、木+火、火+土、土+金）→ 抽到也永远用不出去。");
            }

            // 分解牌：这一关根本不可能出现复合元素 → 分解牌永远没有目标（死牌）。
            bool canEverGetComposite = CanEverGetComposite;
            if (wantsDecompose && !canEverGetComposite)
                into.Add("开了分解牌，但本关既没有初始复合元素、又拿不到复合元素（融合牌没开或合不出来）"
                         + "→ 分解牌永远用不出去（抽牌时会自动跳过它）。");

            if (wantsDecompose && canEverGetComposite && !DecomposeGrantsElementCards)
            {
                // 纯提示：这条不是死局，只是分解牌变成了「免费清场」，写出来免得以为是 bug。
                into.Add("分解牌关掉了「拆完给回两张元素牌」→ 它现在等于一张不消耗出牌次数的移除牌，"
                         + "玩家会拿它当万能解（想留一点代价就把 DecomposeGrantsElementCards 打开）。");
            }

            if (ElementCardWeight == 0 && PoolCards.Length > 0)
                into.Add("元素牌的抽牌权重是 0 → 元素池虽然开着，但一张元素牌都抽不到，牌堆里只有秘术牌。");

            if (FusionCardWeight == 0 && wantsFusion)
                into.Add("开了融合牌，但融合牌权重是 0 → 这张牌抽不到（等于关掉了）。");
            if (DecomposeCardWeight == 0 && wantsDecompose)
                into.Add("开了分解牌，但分解牌权重是 0 → 这张牌抽不到（等于关掉了）。");

            // 复合元素是**只有融合牌才能造出来**的，所以「本关能不能造出它」只看两件事：
            //   1. 融合牌开着，并且它自己那对配方两种元素都在元素池里
            // 造不出来的复合元素不必检查 —— 否则一个只有金水的关卡会被四条不相干的警告刷屏。
            //
            // 于是这里只剩一条真正需要说的话：融合牌开着、配方也在池里，那就没问题；
            // 配方凑不齐的情况已经在上面（融合牌那段）报过了。
        }
    }
}
