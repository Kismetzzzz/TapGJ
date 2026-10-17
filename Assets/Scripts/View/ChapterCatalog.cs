using System;
using TapGJ.Levels;
using UnityEngine;

namespace TapGJ.View
{
    [CreateAssetMenu(menuName = "TapGJ/章节配置", fileName = "ChapterCatalog")]
    public sealed class ChapterCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Level
        {
            public string title;
            [TextArea(2, 8)] public string goal;
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
    }
}
