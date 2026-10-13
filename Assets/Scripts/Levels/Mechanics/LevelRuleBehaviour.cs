using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 关卡机制的基类：把 ILevelRule 的六个成员都给了空实现，
    /// 子类只需要覆盖自己关心的那两三个。
    ///
    /// 用法：把它（或它的子类）挂在**关卡预制体**上。LevelLoader 实例化关卡预制体之后
    /// 会用 GetComponentsInChildren&lt;ILevelRule&gt;(true) 收集，所以挂在子物体上也能生效。
    ///
    /// 一个组件 = 一个机制；同一关可以挂多个，按挂载顺序依次调用。
    /// </summary>
    public abstract class LevelRuleBehaviour : MonoBehaviour, ILevelRule
    {
        [Tooltip("HUD / 日志里的目标说明。留空则用子类给的默认文案。")]
        [SerializeField] string goalDescription;

        public virtual string GoalDescription =>
            string.IsNullOrEmpty(goalDescription) ? DefaultGoalDescription : goalDescription;

        /// <summary>子类给一句默认文案；默认就是类名。</summary>
        protected virtual string DefaultGoalDescription => GetType().Name;

        public virtual void OnLevelStart(GameEngine engine) { }

        public virtual void OnRoundStart(GameEngine engine) { }

        public virtual void OnEncounter(GameEngine engine, EncounterInfo info) { }

        public virtual bool TryExtraWin(GameEngine engine, out string message)
        {
            message = null;
            return false;
        }

        public virtual bool TryExtraLose(GameEngine engine, out string message)
        {
            message = null;
            return false;
        }

        /// <summary>默认不给进度条（Label 为空时引擎会忽略它）。</summary>
        public virtual GoalProgress GetGoalProgress(GameEngine engine) => default;

        /// <summary>进度条/比例用的小工具（Core 不引用 UnityEngine，所以这里自己来）。</summary>
        protected static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
