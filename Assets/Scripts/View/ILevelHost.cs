namespace TapGJ.View
{
    /// <summary>
    /// 「能重建当前这一关 / 能换到下一关」的东西。实际实现是 LevelLoader。
    ///
    /// GameSession 靠它做重开与「下一关」：换关必须走关卡加载器，否则关卡布置、关卡定义的规则
    /// 和关卡预制体上的机制组件全都会丢（以前 Restart 直接 new 一个引擎，就只能拿到场景里那份兜底规则）。
    ///
    /// ⚠ 「当前是哪一关、下一关是哪一关」的知识**全在实现方**（它有目录）。
    /// 主控和 HUD 只问「有没有下一关 / 它叫什么 / 切过去」，自己不认识 LevelCatalog。
    /// </summary>
    public interface ILevelHost
    {
        void RebuildLevel(int seed);

        /// <summary>当前关卡的名字（给 HUD 显示「现在在玩哪一关」）。没有关卡时返回 null。</summary>
        string CurrentLevelName { get; }

        /// <summary>
        /// 有没有「下一关」可去。没有关卡目录、目录里只有这一关（或只剩 None 洞）→ false。
        /// HUD 用它决定「下一关」按钮能不能点。
        /// </summary>
        bool HasNextLevel { get; }

        /// <summary>下一关的名字（写在按钮上，点之前就知道要去哪一关）。没有下一关时返回 null。</summary>
        string NextLevelName { get; }

        /// <summary>
        /// 切到下一关：换关卡定义 → 重建关卡预制体与机制 → 开一局新种子。
        /// 返回 false = 没有下一关（按钮该是灰的）或还没准备好（缺引用）。
        /// </summary>
        bool GoToNextLevel();
    }
}
