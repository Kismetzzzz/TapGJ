using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Levels.Mechanics
{
    /// <summary>
    /// 示例机制一：每隔 N 回合，在随机空格生成若干个指定元素。
    /// 演示 OnRoundStart + SpawnAt + QueueNotice（机制产生的变化也能被逐条播出来）。
    ///
    /// 关卡里可以配成「每 3 回合多冒一个火元素」，逼玩家别拖太久。
    /// </summary>
    public sealed class SpawnOnRoundRule : LevelRuleBehaviour
    {
        [Header("生成什么")]
        [Tooltip("要生成的元素")]
        [SerializeField] Element element = Element.Fire;

        [Tooltip("每隔几回合生成一次（第 N、2N、3N… 回合开始时）")]
        [SerializeField, Min(1)] int everyRounds = 3;

        [Tooltip("每次生成几个（位置随机，棋盘满了就跳过）")]
        [SerializeField, Min(1)] int countPerTime = 1;

        [Tooltip("生成时要不要在日志里说一句")]
        [SerializeField] bool announce = true;

        protected override string DefaultGoalDescription =>
            $"每 {everyRounds} 回合场上多出 {countPerTime} 个{ElementDefs.Name(element)}元素";

        public override void OnRoundStart(GameEngine engine)
        {
            if (everyRounds <= 0) return;
            if (engine.Round % everyRounds != 0) return;

            for (int i = 0; i < countPerTime; i++)
            {
                var cell = engine.RandomEmptyCell();
                if (cell.IsNone)
                {
                    // 棋盘满了不算失败：生成不了就是生成不了，说一声就行。
                    if (announce) engine.QueueNotice($"机制想生成{ElementDefs.Name(element)}，但棋盘已经满了", Pos.None, element);
                    return;
                }

                engine.SpawnAt(cell, element);
                if (announce)
                    engine.QueueNotice($"关卡机制：第 {engine.Round} 回合在 {cell} 生成 1 个{ElementDefs.Name(element)}元素", cell, element);
            }
        }
    }
}
