using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 回合事件的数据资产基类：**每个事件 = 一个 ScriptableObject 子类 + 一个资产**。
    ///
    /// 为什么是 SO 而不是普通类：
    ///   · 事件池（`LevelDefinition.roundEvents`）要能**引用**它 —— 普通类在 Unity 里没法被引用；
    ///   · 每个事件自己的参数（给几张牌、转成什么元素、多少格）直接显示在 Inspector 里，
    ///     调数值不用改代码；加一条**同类型变体**（「给 5 张木牌」）= 复制资产改参数；
    ///   · EditMode 测试可以 `ScriptableObject.CreateInstance<Event_GiveElementCards>()` 直接造，
    ///     不用建资产文件。
    ///
    /// 加一种**新的效果类型**才需要写 C#（继承本类 + 实现 Apply/Revert，见 5 个现成实现）。
    ///
    /// ⚠ 本类是 UnityEngine 对象，**不能进 Core**（Core 必须保持纯 C#，见 IRoundEvent 的注释）；
    /// 引擎只认 <see cref="IRoundEvent"/> 接口，所以 Scripts/Core 里一行 UnityEngine 都没有。
    /// </summary>
    public abstract class RoundEventDefinition : ScriptableObject, IRoundEvent
    {
        [Header("身份（玩家看得到的就是这两行）")]
        [Tooltip("唯一标识，用于「本局不重复」与日志。留空会自动用资产名。")]
        [SerializeField] string eventId;

        [Tooltip("HUD 上显示的事件名，例如「静滞结界」。留空会用资产名。")]
        [SerializeField] string displayName;

        [Tooltip("一句话说明，会显示在 HUD 的「本回合事件」和日志里。")]
        [TextArea(2, 4)]
        [SerializeField] string description;

        public string Id => string.IsNullOrEmpty(eventId) ? name : eventId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;

        /// <summary>
        /// 这条事件在当前关卡适不适用。默认恒适用；具体事件覆盖它来挡掉「本关用不了」的情况。
        /// 不适用的理由会同时出现在 Inspector 的关卡校验和运行期日志里。
        /// </summary>
        public virtual bool IsApplicable(GameEngine engine, out string reason)
        {
            reason = null;
            return true;
        }

        public abstract void Apply(GameEngine engine);

        /// <summary>瞬时事件不用撤销，留空即可。</summary>
        public virtual void Revert(GameEngine engine) { }

        /// <summary>资产名 → 默认 Id / 显示名。只是省一次手填，不改就不生效。</summary>
        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(eventId)) eventId = name;
            if (string.IsNullOrEmpty(displayName)) displayName = name;
        }

        public override string ToString() => $"{DisplayName}（{Id}）";
    }
}
