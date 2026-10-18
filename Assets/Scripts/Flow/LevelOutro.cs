namespace TapGJ.Flow
{
    /// <summary>
    /// 「上一关通关时该说点什么」——过渡关卡/剧情的**跨场景**传递通道。
    ///
    /// 为什么要有它：剧情文本挂在关卡资产上（<c>LevelDefinition.outroText</c>），
    /// 但展示它的时候通常已经离开关卡场景了（结算面板 → 回主菜单 → 进下一章），
    /// 所以需要一个静态的、跨场景活着的小盒子把话带过去。
    ///
    /// 写入方只有一处：<c>LevelLoader.OnLevelFinished</c>（胜利时写、失败/无文本时清空）。
    /// 读取方：局内 UI 的结算面板（立刻显示），以及以后的主界面（回菜单后显示）。
    /// 读完**不会自动清空** —— 由展示方自己决定什么时候调用 <see cref="Clear"/>，
    /// 否则「先回菜单再显示」的写法会读到空。
    /// </summary>
    public static class LevelOutro
    {
        /// <summary>刚通关那一关的剧情文本；null / 空 = 没有。</summary>
        public static string Current { get; private set; }

        public static bool HasText => !string.IsNullOrEmpty(Current);

        public static void Set(string text) => Current = string.IsNullOrEmpty(text) ? null : text;

        public static void Clear() => Current = null;
    }
}
