using System;
using TapGJ.Flow;
using TapGJ.Levels;
using UnityEngine;
using UnityEngine.Serialization;

namespace TapGJ.View
{
    /// <summary>
    /// 章节与关卡的配置。**只负责“显示与解锁”**，不含任何玩法规则 ——
    /// 玩法全在 <see cref="LevelDefinition"/>（元素池 / 牌池 / 字符画布局 / 目标 / 事件）。
    ///
    /// 一条关卡卡片有两种状态：
    ///   · <see cref="Level.definition"/> 为空 → **待开放**：只能看目标，点不进去（还没做的内容）
    ///   · 绑了关卡 → 按「上一关通关没有」决定是 **开始** 还是 **待解锁**
    ///
    /// 解锁链是**跨章节展平**的一条线（第 1 章第 5 关通关 → 第 2 章第 1 关解锁），
    /// 并且**跳过没绑关卡的条目**：否则开发期只要有一关还没做，后面所有关卡都会被永久锁死。
    /// </summary>
    [CreateAssetMenu(menuName = "TapGJ/章节配置", fileName = "ChapterCatalog")]
    public sealed class ChapterCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Level
        {
            public string title;
            [TextArea(2, 8)] public string goal;
            [FormerlySerializedAs("level")]
            [Tooltip("拖入对应的关卡定义资产（Level_XX.asset）；留空时显示待开放。")]
            public LevelDefinition definition;
        }

        [Serializable]
        public sealed class Chapter
        {
            public string title;
            [TextArea(2, 6)] public string description;
            public Level[] levels = Array.Empty<Level>();
        }

        public Chapter[] chapters = Array.Empty<Chapter>();

        // ---------------------------------------------------------------- 查询

        /// <summary>按章节/关卡下标取条目；越界或空槽位返回 null。</summary>
        public Level LevelAt(int chapterIndex, int levelIndex)
        {
            if (chapters == null || chapterIndex < 0 || chapterIndex >= chapters.Length) return null;

            var chapter = chapters[chapterIndex];
            if (chapter?.levels == null || levelIndex < 0 || levelIndex >= chapter.levels.Length) return null;

            return chapter.levels[levelIndex];
        }

        /// <summary>
        /// 这一条在展平顺序里**前面最近的一个已绑定关卡**（跨章节）。没有则返回 null = 它是第一关。
        ///
        /// ⚠ 只数**绑了关卡的**条目：`level == null` 的“待开放”关卡不参与解锁链，
        /// 否则开发期没做的那一关会把后面全部锁死。
        /// </summary>
        public Level PreviousBound(int chapterIndex, int levelIndex)
        {
            if (chapters == null) return null;

            Level last = null;
            for (int c = 0; c < chapters.Length; c++)
            {
                var chapter = chapters[c];
                if (chapter?.levels == null) continue;

                for (int i = 0; i < chapter.levels.Length; i++)
                {
                    if (c == chapterIndex && i == levelIndex) return last;

                    var level = chapter.levels[i];
                    if (level?.definition != null) last = level;
                }
            }

            return null;
        }

        /// <summary>
        /// 这一关解锁没有。
        /// 没绑关卡 = false（还没做，谈不上解锁）；链上第一关 = true；其余看上一关是否已通关。
        /// </summary>
        public bool IsUnlocked(int chapterIndex, int levelIndex)
        {
            var level = LevelAt(chapterIndex, levelIndex);
            if (level?.definition == null) return false;

            var previous = PreviousBound(chapterIndex, levelIndex);
            return previous == null || ProgressStore.IsCleared(previous.definition.levelId);
        }

        /// <summary>关卡卡片底部那行状态字：待开放 / 待解锁 / 开始 / 已通关。</summary>
        public string Badge(int chapterIndex, int levelIndex)
        {
            var level = LevelAt(chapterIndex, levelIndex);
            if (level == null) return string.Empty;

            if (level.definition == null) return "待开放";
            if (ProgressStore.IsCleared(level.definition.levelId)) return "已通关";

            return IsUnlocked(chapterIndex, levelIndex) ? "开始" : "待解锁";
        }

        /// <summary>本章有没有至少一关现在就能进（用来决定页底那句提示怎么写）。</summary>
        public bool HasUnlockedLevel(int chapterIndex)
        {
            var chapter = (chapters != null && chapterIndex >= 0 && chapterIndex < chapters.Length)
                ? chapters[chapterIndex] : null;
            if (chapter?.levels == null) return false;

            for (int i = 0; i < chapter.levels.Length; i++)
                if (IsUnlocked(chapterIndex, i)) return true;

            return false;
        }
    }
}
