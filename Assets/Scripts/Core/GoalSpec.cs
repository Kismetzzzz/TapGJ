using System;

namespace TapGJ.Core
{
    /// <summary>
    /// 胜利目标的种类。取代原来只有两个值的 WinCondition 枚举，
    /// 让「每关不同的胜利条件」成为数据而不是改引擎。
    /// </summary>
    public enum GoalKind
    {
        /// <summary>图1 原文：全场只剩水元素（必须至少有一只水）。</summary>
        WaterOnly = 0,
        /// <summary>全场只剩一种元素（空场也算，即清空）。</summary>
        SingleElement = 1,
        /// <summary>清空全场元素。</summary>
        ClearBoard = 2,
        /// <summary>撑过指定回合数（判定时回合数 ≥ Rounds 即胜利）。</summary>
        SurviveRounds = 3,
        /// <summary>场上某元素数量 ≤ TargetCount。</summary>
        ElementAtMost = 4,
        /// <summary>场上某元素数量 ≥ TargetCount。</summary>
        ElementAtLeast = 5,

        /// <summary>
        /// 自定义目标：内置判定完全不参与，胜负只由关卡预制体上的 ILevelRule 决定。
        /// 选了这个却没挂机制组件 → LevelLoader 直接报错，不会静默变成「永远不会赢」。
        /// </summary>
        Custom = 6,
    }

    /// <summary>
    /// 胜利目标 = 种类 + 参数。默认是「只剩水元素」（与旧版 WinCondition.WaterOnly 等价）。
    /// 放在 Rules.Goal 里，随关卡数据一起序列化。
    /// </summary>
    [Serializable]
    public struct GoalSpec
    {
        public GoalKind Kind;

        /// <summary>ElementAtMost / ElementAtLeast 关注哪个元素。</summary>
        public Element Target;

        /// <summary>ElementAtMost / ElementAtLeast 的数量阈值。</summary>
        public int TargetCount;

        /// <summary>SurviveRounds 要撑过的回合数。</summary>
        public int Rounds;

        public static GoalSpec WaterOnly() => new GoalSpec { Kind = GoalKind.WaterOnly };
        public static GoalSpec SingleElement() => new GoalSpec { Kind = GoalKind.SingleElement };
        public static GoalSpec ClearBoard() => new GoalSpec { Kind = GoalKind.ClearBoard };
        public static GoalSpec Survive(int rounds) => new GoalSpec { Kind = GoalKind.SurviveRounds, Rounds = rounds };
        public static GoalSpec ElementAtMost(Element e, int count) => new GoalSpec { Kind = GoalKind.ElementAtMost, Target = e, TargetCount = count };
        public static GoalSpec ElementAtLeast(Element e, int count) => new GoalSpec { Kind = GoalKind.ElementAtLeast, Target = e, TargetCount = count };
        public static GoalSpec Custom() => new GoalSpec { Kind = GoalKind.Custom };

        /// <summary>人话描述（HUD / 编辑器 / 日志都用它）。</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case GoalKind.WaterOnly: return "全场只剩水元素";
                case GoalKind.SingleElement: return "全场只剩一种元素";
                case GoalKind.ClearBoard: return "清空全场元素";
                case GoalKind.SurviveRounds: return $"撑过 {Rounds} 回合";
                case GoalKind.ElementAtMost: return $"场上{ElementDefs.Name(Target)}元素不超过 {TargetCount} 个";
                case GoalKind.ElementAtLeast: return $"场上{ElementDefs.Name(Target)}元素达到 {TargetCount} 个";
                // 真正的文案由关卡机制组件提供；GameEngine.WinConditionDescription() 会替换掉这句。
                case GoalKind.Custom: return "自定义目标（由关卡机制组件判定）";
                default: return Kind.ToString();
            }
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// 目标进度的展示数据（由 GameEngine.GetGoalProgress 生成，HUD 直接消费）。
    /// 放在 Core 里是为了让进度条的计算逻辑能被测试，同时保持 HUD 不碰引擎内部。
    /// </summary>
    public struct GoalProgress
    {
        /// <summary>目标的文字描述。</summary>
        public string Description;

        /// <summary>进度条填充比例（0~1）。</summary>
        public float Fill;

        /// <summary>进度条上的那行小字，例如「水 3 / 场上 12」。</summary>
        public string Label;
    }
}
