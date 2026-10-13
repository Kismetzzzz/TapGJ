using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 场景接线校验的测试。
    ///
    /// 场景本身由人手搭（本工程不再运行时自动创建任何东西），所以这里不测「某个具体场景」，
    /// 而是测校验器**能不能查出问题**：造几种漏引用的组合，断言它报出的字段名与提示。
    /// 这样「漏拖一个引用」从「运行时才发现」变成「点一下菜单就知道」。
    /// </summary>
    public class SceneWiringTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
        }

        // ---------------------------------------------------------------- 组件级

        [Test]
        public void 校验_主控缺引用时逐条列出字段名和该拖什么()
        {
            var issues = new List<WiringIssue>();
            GameSession.CheckWiring(null, null, null, issues);

            Assert.AreEqual(3, issues.Count, "board / hand / input 三个引用都该报出来");
            Assert.IsTrue(issues.Exists(i => i.Target == "GameSession" && i.Field == "board" && i.IsError),
                "应该报出 GameSession.board");
            Assert.IsTrue(issues.Exists(i => i.Target == "GameSession" && i.Field == "hand" && i.IsError),
                "应该报出 GameSession.hand");
            Assert.IsTrue(issues.Exists(i => i.Target == "GameSession" && i.Field == "input" && i.IsError),
                "应该报出 GameSession.input");

            foreach (var issue in issues)
                Assert.IsTrue(issue.Message.Contains("拖进来"),
                    $"提示必须说清该把什么拖进来，实际是：{issue.Message}");
        }

        [Test]
        public void 校验_输入分发缺相机时报出字段名()
        {
            var issues = new List<WiringIssue>();
            ClickRouter.CheckWiring(null, issues);

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("worldCamera", issues[0].Field);
            Assert.IsTrue(issues[0].IsError);
        }

        [Test]
        public void 校验_能在场景物体上找出没连的引用()
        {
            // 场景物体上的实例路径：校验器从根往下找所有 ISceneWiringCheck 组件
            _root = new GameObject("SceneRoot");
            var systems = new GameObject("Systems");
            systems.transform.SetParent(_root.transform, false);
            systems.AddComponent<ClickRouter>(); // 故意不连相机

            var issues = SceneValidator.Collect(_root);

            Assert.AreEqual(1, issues.Count, SceneValidator.Report(issues));
            Assert.AreEqual("ClickRouter", issues[0].Target);
            Assert.AreEqual("worldCamera", issues[0].Field);
        }

        [Test]
        public void 校验_引用齐全的物体不报问题()
        {
            _root = new GameObject("WiredRoot");
            _root.AddComponent<BoardView>();
            _root.AddComponent<HandView>();

            var issues = SceneValidator.Collect(_root);

            Assert.AreEqual(0, issues.Count, SceneValidator.Report(issues));
            Assert.IsTrue(SceneValidator.Report(issues).Contains("✔"), "没问题时报告应该是通过的样子");
        }

        // ---------------------------------------------------------------- 场景级

        [Test]
        public void 校验_场景级_没有主控没有输入没有正交相机时各报一条()
        {
            var issues = SceneValidator.CollectSceneLevel(0, 0, 0);

            Assert.AreEqual(3, issues.Count, SceneValidator.Report(issues));
            Assert.IsTrue(issues.Exists(i => i.Field == "GameSession" && i.IsError));
            Assert.IsTrue(issues.Exists(i => i.Field == "ClickRouter" && i.IsError));
            Assert.IsTrue(issues.Exists(i => i.Field == "Camera" && !i.IsError), "相机只是提示，不是错误");
        }

        [Test]
        public void 校验_场景级_两个主控时报错()
        {
            var issues = SceneValidator.CollectSceneLevel(2, 1, 1);

            Assert.AreEqual(1, issues.Count);
            Assert.IsTrue(issues[0].IsError);
            Assert.IsTrue(issues[0].Message.Contains("2"), "要说清有几个：" + issues[0].Message);
        }

        [Test]
        public void 校验_场景级_齐全时不报问题()
        {
            Assert.AreEqual(0, SceneValidator.CollectSceneLevel(1, 1, 1).Count);
            // 多个正交相机是允许的（分屏/UI 相机），只要求至少一个
            Assert.AreEqual(0, SceneValidator.CollectSceneLevel(1, 1, 3).Count);
        }

        // ---------------------------------------------------------------- 骨架与接线清单

        [Test]
        public void 骨架_菜单搭出来的骨架接线完整且字段名没有失效()
        {
            // 这条用例守的是 README 第七节那张「接线清单」：
            //   · SceneScaffold 用 SerializedObject 按字段名赋值，字段改名就会记进 LastWiringFailures
            //   · 搭完之后跑一遍校验器，必须是全绿（场景级检查也一起过）
            // 也就是说：谁把 GameSession.board 改名了，这里立刻红。
            bool hadCamera = Camera.main != null;
            GameObject session = null;

            try
            {
                session = TapGJ.EditorTools.SceneScaffold.BuildSkeleton();

                Assert.AreEqual(0, TapGJ.EditorTools.SceneScaffold.LastWiringFailures.Count,
                    "字段名失效了：" + string.Join("；", TapGJ.EditorTools.SceneScaffold.LastWiringFailures));

                var issues = SceneValidator.Collect(session.scene);
                Assert.AreEqual(0, issues.Count, SceneValidator.Report(issues));

                // 骨架里几个关键组件都该在
                Assert.IsNotNull(session.GetComponent<GameSession>());
                Assert.IsNotNull(Object.FindFirstObjectByType<ClickRouter>());
                Assert.IsNotNull(Object.FindFirstObjectByType<BoardView>());
                Assert.IsNotNull(Object.FindFirstObjectByType<HandView>());
                Assert.IsNotNull(Object.FindFirstObjectByType<TapGJ.Levels.LevelLoader>(), "骨架该带上 LevelLoader");

                // uGUI 两件套：Canvas 是正式 UI 的落点，EventSystem 少了按钮就点不动
                Assert.IsNotNull(Object.FindFirstObjectByType<Canvas>(), "骨架该带上 Canvas");
                Assert.IsNotNull(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(),
                    "骨架该带上 EventSystem —— 没有它 uGUI 收不到点击");
            }
            finally
            {
                // 测试不该留下场景垃圾：骨架是按名字命名的几个根物体
                foreach (var root in session != null ? session.scene.GetRootGameObjects() : new GameObject[0])
                {
                    if (root.name == "Systems" || root.name == "Anchors" || root.name == "LevelRoot"
                        || root.name == "Canvas" || root.name == "EventSystem")
                        Object.DestroyImmediate(root);
                }

                if (!hadCamera && Camera.main != null) Object.DestroyImmediate(Camera.main.gameObject);
            }
        }

        // ---------------------------------------------------------------- 报告文本

        [Test]
        public void 校验_报告文本带上字段名与统计()
        {
            var issues = new List<WiringIssue>();
            GameSession.CheckWiring(null, null, null, issues);

            var text = SceneValidator.Report(issues);

            StringAssert.Contains("3 个错误", text);
            StringAssert.Contains("GameSession.board", text);
        }
    }
}
