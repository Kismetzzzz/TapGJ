using System.Collections.Generic;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 调试用的「解锁关卡」操作（**纯逻辑，没有 UI**）。
    ///
    /// 编辑器的调试窗口（<see cref="LevelDebugWindow"/>）和几个菜单项都走这里，EditMode 测试也直接调它。
    ///
    /// 它只改 PlayerPrefs 里的「已通关」标记：
    ///   · 不碰关卡资产、也不碰最好成绩（用 <see cref="ProgressStore.MarkClearedOnly"/>）——
    ///     试玩时不该把自己的测试成绩混进真实的回合数记录
    ///   · 只认**已绑定关卡**的条目，口径和 <see cref="ChapterCatalog.PreviousBound"/> 完全一致，
    ///     所以「解锁到这里」之后看到的解锁状态，和玩家真的一关关打过去一模一样
    /// </summary>
    public static class LevelUnlockTools
    {
        /// <summary>展平顺序里所有**已绑定关卡**的条目（待开放的条目不参与解锁链）。</summary>
        public static List<ChapterCatalog.Level> BoundLevels(ChapterCatalog catalog)
        {
            var result = new List<ChapterCatalog.Level>();
            if (catalog == null || catalog.chapters == null) return result;

            foreach (var chapter in catalog.chapters)
            {
                if (chapter?.levels == null) continue;
                foreach (var level in chapter.levels)
                    if (level?.level != null) result.Add(level);
            }

            return result;
        }

        public static int TotalCount(ChapterCatalog catalog) => BoundLevels(catalog).Count;

        public static int ClearedCount(ChapterCatalog catalog)
        {
            int n = 0;
            foreach (var level in BoundLevels(catalog))
                if (ProgressStore.IsCleared(level.level.levelId)) n++;
            return n;
        }

        /// <summary>
        /// 「解锁下一关」要打哪一个：展平顺序里**第一个还没通关的已绑定关卡**。
        /// 全都通关了返回 null。
        /// </summary>
        public static LevelDefinition NextToUnlock(ChapterCatalog catalog)
        {
            foreach (var level in BoundLevels(catalog))
                if (!ProgressStore.IsCleared(level.level.levelId)) return level.level;

            return null;
        }

        /// <summary>
        /// 把 <paramref name="target"/> **之前**的已绑定关卡全部标成已通关（目标本身不动）——
        /// 也就是「直接解锁到这一关」。返回这次新标了几关。
        /// </summary>
        public static int UnlockUpTo(ChapterCatalog catalog, LevelDefinition target)
        {
            if (target == null) return 0;

            int changed = 0;
            foreach (var level in BoundLevels(catalog))
            {
                if (level.level == target) break;
                if (ProgressStore.IsCleared(level.level.levelId)) continue;

                ProgressStore.MarkClearedOnly(level.level.levelId);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 解锁下一关：把第一个还没通关的标成已通关 → 它后面那一关就解锁了。
        /// 返回被标掉的那一关（全都通关了返回 null）。
        /// </summary>
        public static LevelDefinition UnlockNext(ChapterCatalog catalog)
        {
            var next = NextToUnlock(catalog);
            if (next != null) ProgressStore.MarkClearedOnly(next.levelId);
            return next;
        }

        /// <summary>全部解锁（只标这个目录里出现过的关卡）。返回新标了几关。</summary>
        public static int UnlockAll(ChapterCatalog catalog)
        {
            int changed = 0;
            foreach (var level in BoundLevels(catalog))
            {
                if (ProgressStore.IsCleared(level.level.levelId)) continue;

                ProgressStore.MarkClearedOnly(level.level.levelId);
                changed++;
            }

            return changed;
        }

        /// <summary>清空进度（只清这个目录里出现过的关卡）。返回清掉了几关。</summary>
        public static int ResetAll(ChapterCatalog catalog)
        {
            int changed = 0;
            foreach (var level in BoundLevels(catalog))
            {
                if (!ProgressStore.IsCleared(level.level.levelId)) continue;

                ProgressStore.Reset(level.level.levelId);
                changed++;
            }

            return changed;
        }
    }
}
