using System.Reflection;
using NUnit.Framework;
using TapGJ.Flow;
using TapGJ.View;

namespace TapGJ.Tests
{
    /// <summary>
    /// 「退出游戏」按钮的测试。
    ///
    /// 真正的退出只能发生一次，而且测试里没法复现（编辑器里它等于结束 Play），
    /// 所以「点几下才退、什么时候作废」这套会写错的逻辑被抽成纯状态机 <see cref="Hud.QuitConfirm"/>，
    /// 这里用假时钟直接演练一串点击 —— 不需要开 Play，也不会把编辑器关掉。
    /// 「确认之后调 <see cref="SceneFlow.QuitGame"/>」只剩一行，由编译器和最后一条用例共同钉住。
    /// </summary>
    public class HudQuitTests
    {
        [Test]
        public void 退出_第一下只确认_第二下才真的退出()
        {
            var quit = new Hud.QuitConfirm();

            Assert.IsFalse(quit.Armed(0f), "一开始不该是确认态");
            Assert.IsFalse(quit.Click(0f), "第一下只是问一句，不能退出");
            Assert.IsTrue(quit.Armed(0.5f), "第一下之后应该进入确认态");
            Assert.IsTrue(quit.Click(1f), "第二下才是真的退出");
            Assert.IsFalse(quit.Armed(1f), "退出之后状态要清干净，复位回初始态");
        }

        [Test]
        public void 退出_按钮上的字跟着确认态变()
        {
            Assert.AreEqual("退出游戏", Hud.QuitButtonLabel(false));
            Assert.AreEqual("确认退出游戏？", Hud.QuitButtonLabel(true),
                "第二下必须能看出自己在点什么，否则会被当成「按钮坏了」");
        }

        [Test]
        public void 退出_确认超时作废_超时后再点又只是第一下()
        {
            var quit = new Hud.QuitConfirm();
            quit.Click(0f);

            float after = Hud.QuitConfirm.ConfirmSeconds + 0.01f;
            Assert.IsFalse(quit.Armed(after), "超时之后不该还挂着确认态");
            Assert.IsFalse(quit.Click(after), "超时后的这一下要当成新的第一下，不能退出");
            Assert.IsTrue(quit.Armed(after), "它应该重新进入确认态，而不是不理人");
        }

        [Test]
        public void 退出_点了别的按钮会作废确认()
        {
            // Hud.DrawPanel 里每个别的按钮按下时都会调一次 Disarm()（免得确认态一直挂着）。
            var quit = new Hud.QuitConfirm();
            quit.Click(0f);
            quit.Disarm();

            Assert.IsFalse(quit.Armed(0.5f));
            Assert.IsFalse(quit.Click(0.5f), "作废之后再点一下只是重新确认，不该退出");
        }

        [Test]
        public void 退出_入口在SceneFlow上_由它分流编辑器与构建()
        {
            // 守的是「编辑器里 Application.Quit 什么都不做」这个坑：退出必须有一层能分流的实现。
            // 谁把它从 SceneFlow 上删掉 / 改名，这条用例立刻红（HUD 的按钮就点不动了）。
            var method = typeof(SceneFlow).GetMethod("QuitGame",
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(method, "SceneFlow.QuitGame 是「退出游戏」按钮的唯一入口");
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(0, method.GetParameters().Length, "它不该要参数：HUD 只负责点，不负责决定怎么退");
        }
    }
}
