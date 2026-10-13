namespace TapGJ.View
{
    /// <summary>
    /// 游戏内 HUD 的接入点。
    ///
    /// <see cref="GameSession"/> 只认这个接口、**不认识任何具体 HUD 类型**，于是：
    ///   · 临时 IMGUI 的 <see cref="Hud"/> 和以后正式的 uGUI `GameHud` 可以**并存**（迁移期两边都在也能跑）
    ///   · 删掉其中任意一个，主控一行都不用改
    ///
    /// 挂钩方式（两种都行，重复挂也安全）：
    ///   · 把实现挂在 GameSession **同一个物体**上 —— GameSession.Awake 会自己扫出来
    ///   · 挂在别的物体上 —— 在 Awake 里调 <see cref="GameSession.RegisterHud"/>
    ///
    /// ⚠ 单向依赖：HUD 调 GameSession 的公开方法/事件，GameSession 不反过来查 HUD 的状态。
    /// 所以「按钮推进阶段」这类流程仍然是 HUD → GameSession 的调用，换 UI 不影响回合状态机。
    /// </summary>
    public interface IGameHud
    {
        /// <summary>
        /// 把主控交给 HUD。同一个实例可能被调用多次（同物体扫描 + 自行注册），
        /// 实现必须是**幂等**的。
        /// </summary>
        void Setup(GameSession session);
    }
}
