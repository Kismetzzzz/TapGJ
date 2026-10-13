using TapGJ.Levels;

namespace TapGJ.Flow
{
    /// <summary>
    /// 跨场景传递「要进哪一关」。静态字段不会随场景切换清空，所以不需要 DontDestroyOnLoad。
    /// 菜单用 SceneFlow.LoadLevel 写入，LevelLoader 读；只在 LoadMenu 时清空。
    /// </summary>
    public static class GameLaunchContext
    {
        public static LevelDefinition Level { get; private set; }
        public static int Seed { get; private set; }
        public static bool HasLevel { get; private set; }

        public static void Set(LevelDefinition level, int seed)
        {
            Level = level;
            Seed = seed;
            HasLevel = level != null;
        }

        public static void Clear()
        {
            Level = null;
            Seed = 0;
            HasLevel = false;
        }
    }
}
