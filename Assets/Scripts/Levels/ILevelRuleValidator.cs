using System.Collections.Generic;

namespace TapGJ.Levels
{
    /// <summary>
    /// 机制可以自己报名「这一关的配置有没有隐患」。
    ///
    /// 关卡校验器知道布局、元素池、目标，但**不知道某个机制需要什么** ——
    /// 比如「场上每种元素都要有」这种机制，必须知道玩家到底能不能把那种元素弄出来。
    /// 让机制自己检查，比在 LevelDefinition 里给每个机制写特判好：
    /// 以后加新机制不用再改关卡校验器。
    ///
    /// 实现它的是挂载在关卡预制体上的组件（见 Mechanics/EqualElementCountsRule 的例子）。
    /// </summary>
    public interface ILevelRuleValidator
    {
        /// <summary>把这一关配置上的隐患写进 <paramref name="into"/>（人话；空 = 没问题）。</summary>
        void CollectRuleIssues(LevelDefinition level, List<string> into);
    }
}
