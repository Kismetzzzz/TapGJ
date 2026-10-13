using TapGJ.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapGJ.Flow
{
    /// <summary>
    /// 场景切换的唯一入口。主界面同学只碰这里 + LevelCatalog + ProgressStore，不用打开 Game.unity。
    /// </summary>
    public static class SceneFlow
    {
        /// <summary>主界面场景名。菜单场景可以在自己 Awake 里把它设成自己的名字。</summary>
        public static string MenuSceneName = "MainMenu";

        /// <summary>进入一关：记下关卡与种子，切到关卡所在的场景。</summary>
        public static void LoadLevel(LevelDefinition level, int seed = 0)
        {
            if (level == null)
            {
                Debug.LogError("[SceneFlow] 关卡为空，无法进入。");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(level.sceneName))
            {
                Debug.LogError($"[SceneFlow] 场景「{level.sceneName}」不在 Build Settings 里，无法进入。"
                               + "在 File ▸ Build Settings 里把 Game 场景勾上。");
                return;
            }

            GameLaunchContext.Set(level, seed);
            SceneManager.LoadScene(level.sceneName);
        }

        /// <summary>回主界面。</summary>
        public static void LoadMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(MenuSceneName))
            {
                Debug.LogError($"[SceneFlow] 主界面场景「{MenuSceneName}」不在 Build Settings 里。");
                return;
            }

            GameLaunchContext.Clear();
            SceneManager.LoadScene(MenuSceneName);
        }

        /// <summary>重进当前这关（菜单的「再来一次」用）。</summary>
        public static void ReloadLevel()
        {
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
        }

        /// <summary>
        /// 退出游戏（HUD 的「退出游戏」按钮走这里，见 <see cref="TapGJ.View.Hud"/>）。
        /// 放在本类而不是 HUD 里，理由和另外三个方法一样：**离开当前场景这件事只有一处实现**，
        /// 换 UI / 以后加「回主界面再退」都只改这里。
        ///
        /// ⚠ 编辑器里 <see cref="Application.Quit"/> **什么都不做**（Play 模式跑在编辑器进程里），
        ///   所以编辑器下改成结束 Play —— 按下去的表现和真机一致：游戏结束。
        ///   这也是为什么退出必须先过这一层：HUD 不该知道自己在编辑器里还是在构建里。
        /// </summary>
        public static void QuitGame()
        {
#if UNITY_EDITOR
            Debug.Log("[SceneFlow] 退出游戏（编辑器里 = 结束 Play）。");
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Debug.Log("[SceneFlow] 退出游戏。");
            Application.Quit();
#endif
        }
    }
}
