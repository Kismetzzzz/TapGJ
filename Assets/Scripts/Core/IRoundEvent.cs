namespace TapGJ.Core
{
    /// <summary>
    /// 「回合事件」：每隔几回合随机来一次、**只作用于当回合**的扰动（见 README 第十一节）。
    ///
    /// 和 <see cref="ILevelRule"/> 的分工：
    ///   · <see cref="ILevelRule"/> 是**这一关固定**的规则（每局都一样、写在关卡预制体上）；
    ///   · <see cref="IRoundEvent"/> 是**本局随机**的扰动（从关卡启用的事件池里抽，每局不同）。
    ///
    /// 生命周期（由 <see cref="GameEngine"/> 调度，纯 C# 侧决定，不依赖表现层）：
    ///   BeginRound → 轮到事件 → <see cref="Apply"/> → 关卡机制的 OnRoundStart → 小怪移动
    ///   → 玩家行动 / 弃牌 → 判定阶段 → <see cref="Revert"/>
    /// 所以「本回合」的准确含义是「从准备阶段开始到判定阶段为止」。
    ///
    /// ⚠ 实现类通常是 `ScriptableObject`（见 Scripts/Events/），**不要在这里引用 UnityEngine** ——
    /// 这个接口在 Core 里，Core 必须保持纯 C#，否则几千次跑批的 EditMode 测试就没了。
    /// </summary>
    public interface IRoundEvent
    {
        /// <summary>一局内的唯一标识（去重 / 不放回抽取都按它判）。</summary>
        string Id { get; }

        /// <summary>玩家看到的短名字（HUD 上就显示它，例如「静滞结界」）。</summary>
        string DisplayName { get; }

        /// <summary>玩家看到的一句话说明（例如「本回合元素不会自主相互作用」）。</summary>
        string Description { get; }

        /// <summary>
        /// 这条事件在**当前这一关**适不适用。不适用的会被跳过（换下一条），不会被抽中。
        ///
        /// 典型的不适用：把元素转成本关「可用元素」里没有的元素、「秘术馈赠」但本关没开复合元素、
        /// 「玉翁敛息」但本关瓶子容量本来就不超过目标值。理由会写进 <paramref name="reason"/>，
        /// 关卡校验与运行期日志共用同一句话。
        ///
        /// 抽取时引擎还没开始跑这一回合（棋盘是回合开始那一刻的状态），别在这里改任何东西。
        /// </summary>
        bool IsApplicable(GameEngine engine, out string reason);

        /// <summary>生效（回合开始、关卡机制之前调一次）。</summary>
        void Apply(GameEngine engine);

        /// <summary>
        /// 回合结束（判定阶段开头）恢复。
        /// 瞬时事件（给牌、转换格子）留空即可 —— 它们没有需要撤销的东西。
        /// </summary>
        void Revert(GameEngine engine);
    }
}
