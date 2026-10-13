using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapGJ.View
{
    /// <summary>场景接线问题。Error = 会跑不起来，Warning = 能跑但多半不是你要的。</summary>
    public struct WiringIssue
    {
        public bool IsError;
        /// <summary>出问题的组件名，例如 GameSession。</summary>
        public string Target;
        /// <summary>没连的字段名，例如 board。</summary>
        public string Field;
        /// <summary>人话说明：该把什么拖进来。</summary>
        public string Message;

        public override string ToString()
            => $"{(IsError ? "✘" : "⚠")} {Target}.{Field}：{Message}";
    }

    /// <summary>
    /// 组件自己报告「我哪个必需字段没连」。
    /// 放在 Runtime 而不是 Editor 里，是为了让 EditMode 测试能直接调用这些检查（不用反射、不用开场景）。
    /// </summary>
    public interface ISceneWiringCheck
    {
        void CollectWiringIssues(List<WiringIssue> into);
    }

    /// <summary>
    /// 场景接线校验：把「漏拖引用」从「运行时才发现」提前到「点一下菜单就知道」。
    /// 菜单 TapGJ ▸ 校验当前场景 只是把这里的结论画出来；逻辑全在 Collect 里，所以能被测试覆盖。
    /// </summary>
    public static class SceneValidator
    {
        // ---------------------------------------------------------------- 组件级检查

        public static List<WiringIssue> Collect(GameObject root)
        {
            var issues = new List<WiringIssue>();
            if (root == null) return issues;

            CollectFrom(root, issues);
            return issues;
        }

        public static List<WiringIssue> Collect(Scene scene)
        {
            var issues = new List<WiringIssue>();

            foreach (var root in scene.GetRootGameObjects())
                CollectFrom(root, issues);

            // ---- 场景级检查：只能有一个主控，相机必须是正交
            int sessions = 0, routers = 0, orthoCameras = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb is GameSession) sessions++;
                    if (mb is ClickRouter) routers++;
                }

                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    if (cam.orthographic) orthoCameras++;
            }

            issues.AddRange(CollectSceneLevel(sessions, routers, orthoCameras));
            return issues;
        }

        /// <summary>
        /// 场景级检查的纯函数版本（数量由调用方数好）。
        /// 单独拆出来是为了能在 EditMode 测试里覆盖 —— 测试没法轻易造一个干净的空场景。
        /// </summary>
        public static List<WiringIssue> CollectSceneLevel(int sessionCount, int routerCount, int orthographicCameraCount)
        {
            var issues = new List<WiringIssue>();

            if (sessionCount == 0)
                issues.Add(Error("Scene", "GameSession",
                    "场景里没有 GameSession —— 它是回合流程的主控，必须有一个（菜单 TapGJ ▸ 在当前场景搭建骨架 可以生成）"));
            else if (sessionCount > 1)
                issues.Add(Error("Scene", "GameSession",
                    $"场景里有 {sessionCount} 个 GameSession —— 只能有一个，否则两边会各跑一局"));

            if (routerCount == 0)
                issues.Add(Error("Scene", "ClickRouter", "场景里没有 ClickRouter —— 点了棋盘不会有反应"));

            if (orthographicCameraCount == 0)
                issues.Add(Warning("Scene", "Camera",
                    "场景里没有正交相机 —— 2D 棋盘看起来会不对（相机 Projection 设成 Orthographic）"));

            return issues;
        }

        static void CollectFrom(GameObject root, List<WiringIssue> into)
        {
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb is ISceneWiringCheck check)
                    check.CollectWiringIssues(into);
        }

        // ---------------------------------------------------------------- 报告文本

        /// <summary>把问题列表拼成给弹窗 / Console 的文本。</summary>
        public static string Report(List<WiringIssue> issues)
        {
            var sb = new System.Text.StringBuilder();
            if (issues == null || issues.Count == 0)
            {
                sb.AppendLine("✔ 场景接线完整：必需引用都连上了。");
                return sb.ToString();
            }

            int errors = 0, warnings = 0;
            foreach (var i in issues)
            {
                if (i.IsError) errors++; else warnings++;
            }

            sb.AppendLine($"场景接线检查：{errors} 个错误、{warnings} 个提示");
            sb.AppendLine("──────────────");
            for (int n = 0; n < issues.Count; n++)
                sb.AppendLine(issues[n].ToString());

            return sb.ToString();
        }

        // ---------------------------------------------------------------- 给组件用的构造助手

        public static WiringIssue Error(string target, string field, string message)
            => new WiringIssue { IsError = true, Target = target, Field = field, Message = message };

        public static WiringIssue Warning(string target, string field, string message)
            => new WiringIssue { IsError = false, Target = target, Field = field, Message = message };
    }

    /// <summary>组件里检查「必需引用」的统一入口：空就记一条 Error，附带「该拖什么进来」的提示。</summary>
    public static class Wiring
    {
        public static void Require(Object value, string target, string field, string hint, List<WiringIssue> into)
        {
            if (value != null) return; // Unity 的重载运算符：被销毁的对象也算空
            into.Add(SceneValidator.Error(target, field, $"没有连。{hint}"));
        }

        /// <summary>把一条问题就地报出来（组件自己的 CollectWiringIssues 里用）。</summary>
        public static void Report(List<WiringIssue> into, bool isError, string target, string field, string message)
        {
            into.Add(isError
                ? SceneValidator.Error(target, field, message)
                : SceneValidator.Warning(target, field, message));
        }
    }
}
