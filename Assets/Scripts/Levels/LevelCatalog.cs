using UnityEngine;

namespace TapGJ.Levels
{
    /// <summary>
    /// 关卡列表。菜单同学读它就能列出所有关卡；LevelLoader 也用它回退默认关卡。
    /// </summary>
    [CreateAssetMenu(fileName = "LevelCatalog", menuName = "TapGJ/关卡目录 (LevelCatalog)", order = 2)]
    public sealed class LevelCatalog : ScriptableObject
    {
        [Tooltip("主界面场景名（SceneFlow.LoadMenu 用）")]
        public string menuSceneName = "MainMenu";

        public LevelDefinition[] levels;

        public int IndexOf(string levelId)
        {
            for (int i = 0; i < (levels != null ? levels.Length : 0); i++)
                if (levels[i] != null && levels[i].levelId == levelId)
                    return i;
            return -1;
        }

        public LevelDefinition GetByIndex(int i)
            => (levels != null && i >= 0 && i < levels.Length) ? levels[i] : null;

        /// <summary>
        /// 「当前这一关之后」是哪一关。HUD 上的「下一关」按钮用它（<see cref="LevelLoader.GoToNextLevel"/>）。
        ///
        /// 规则（都是为了「按钮永远知道该去哪儿、并且说得出名字」）：
        ///   · 按 levelId 找到当前关 → 取下一个**非空**关卡，**最后一关绕回第一关**（目录是循环的）
        ///   · 找不到当前关（调试试玩了一个不在目录里的关卡 / levelId 为空）→ 回到目录第一关
        ///   · 目录里没有**别的**关卡（空目录、全是洞、或只剩当前这一关）→ 返回 null = 没有下一关
        ///
        /// ⚠ 目录里允许存在 None 洞（关卡被删了没清目录）：这里会跳过它们，不会把 None 当成一关。
        /// </summary>
        public LevelDefinition NextAfter(string levelId)
        {
            if (levels == null || levels.Length == 0) return null;

            int current = IndexOf(levelId);

            int playable = 0;
            for (int i = 0; i < levels.Length; i++)
                if (levels[i] != null) playable++;

            if (playable == 0) return null;
            if (current >= 0 && playable == 1) return null; // 目录里只有当前这一关，没有「下一关」

            // 从当前关的**下一格**开始往后扫一圈（最后一关会绕回开头）；找不到当前关就从第 0 格开始扫
            int start = current < 0 ? 0 : current + 1;
            for (int step = 0; step < levels.Length; step++)
            {
                int i = (start + step) % levels.Length;
                var candidate = levels[i];
                if (candidate == null) continue;
                if (i == current) continue; // 绕了一圈只剩自己

                return candidate;
            }

            return null;
        }
    }
}
