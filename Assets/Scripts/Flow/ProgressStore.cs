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

        public static void Reset(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            PlayerPrefs.DeleteKey($"TapGJ.cleared.{levelId}");
            PlayerPrefs.DeleteKey($"TapGJ.best.{levelId}");
            PlayerPrefs.Save();
        }
    }
}
