using UnityEngine;

namespace TapGJ.Flow
{
    /// <summary>
    /// 通关进度（PlayerPrefs 存储）。菜单同学用 IsCleared 显示解锁状态。
    /// 键名约定：TapGJ.cleared.&lt;levelId&gt;（0/1）、TapGJ.best.&lt;levelId&gt;（回合数）。
    /// </summary>
    public static class ProgressStore
    {
        public static bool IsCleared(string levelId)
            => !string.IsNullOrEmpty(levelId) && PlayerPrefs.GetInt($"TapGJ.cleared.{levelId}", 0) == 1;

        /// <summary>标记通关并记下最好成绩（回合数越少越好）。</summary>
        public static void MarkCleared(string levelId, int rounds)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            PlayerPrefs.SetInt($"TapGJ.cleared.{levelId}", 1);

            int best = BestRounds(levelId);
            if (best <= 0 || rounds < best)
                PlayerPrefs.SetInt($"TapGJ.best.{levelId}", rounds);

            PlayerPrefs.Save();
        }

        /// <summary>最好成绩（回合数）；0 = 还没通关记录。</summary>
        public static int BestRounds(string levelId)
            => string.IsNullOrEmpty(levelId) ? 0 : PlayerPrefs.GetInt($"TapGJ.best.{levelId}", 0);

        /// <summary>
        /// **只**把「已通关」这一位打上，不写最好成绩。
        ///
        /// 给调试工具用（编辑器里的「解锁下一关 / 解锁全部」）：试玩时想把后面的关卡打开，
        /// 但不该把自己的测试成绩混进真实的回合数记录里。玩法流程**不要**用它 —— 正常通关走
        /// <see cref="MarkCleared"/>。
        /// </summary>
        public static void MarkClearedOnly(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            PlayerPrefs.SetInt($"TapGJ.cleared.{levelId}", 1);
            PlayerPrefs.Save();
        }

        public static void Reset(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            PlayerPrefs.DeleteKey($"TapGJ.cleared.{levelId}");
            PlayerPrefs.DeleteKey($"TapGJ.best.{levelId}");
            PlayerPrefs.Save();
        }
    }
}
