using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    /// <summary>
    /// 「下一关」的端到端测试：真的搭一套 GameSession（棋盘 / 手牌 / 输入 / 关卡加载器）、真的换一关，
    /// 验证换完之后关卡定义、规则、回合数、日志都换了一套。
    ///
    /// 目录查找本身（哪一关在谁后面、最后一关停止）在 LevelSystemTests 里逐条钉过，
    /// 这里钉的是「点下去之后发生了什么」。
    /// </summary>
    public class LevelSwitchTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
            GameLaunchContext.Clear();
        }

        [Test]
        public void 下一关_换关卡定义与引擎_回合归零_并留一条日志()
        {
            var first = MakeLevel("aaa", "第 1 关", roundLimit: 15);
            var second = MakeLevel("bbb", "第 2 关", roundLimit: 7);
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { first, second };

            try
            {
                var session = BuildScene(catalog, out var loader, out var levelRoot);

                loader.LoadLevel(first, 4242);

                Assert.AreSame(first, loader.CurrentLevel);
                Assert.AreEqual("第 1 关", loader.CurrentLevelName);
                Assert.AreEqual("第 2 关", loader.NextLevelName, "按钮上要写下一关的名字");
                Assert.AreEqual("下一关 → 第 2 关", Hud.NextLevelButtonLabel(session));
                Assert.AreEqual(15, session.Engine.Rules.RoundLimit, "现在跑的是第 1 关的规则");
                Assert.AreEqual(1, session.Engine.Round);
                Assert.AreEqual(4242, loader.CurrentSeed);

                var beforeEngine = session.Engine;

                Assert.IsTrue(session.GoToNextLevel(), "第 1 关之后应该有下一关");

                Assert.AreSame(second, loader.CurrentLevel);
                Assert.AreEqual("第 2 关", loader.CurrentLevelName);
                Assert.IsNull(loader.NextLevelName, "最后一关不能把第一关显示成下一关");
                Assert.IsFalse(session.HasNextLevel);
                Assert.AreNotSame(beforeEngine, session.Engine, "换关要重建引擎，不能接着上一关的残局打");
                Assert.AreEqual(7, session.Engine.Rules.RoundLimit, "现在跑的是第 2 关的规则");
                Assert.AreEqual(1, session.Engine.Round, "换关 = 新的一局，从第 1 回合开始");
                Assert.AreEqual(0, session.Engine.PendingStepCount, "换关不该把上一关的步骤带过来");
                Assert.IsFalse(session.Engine.IsOver);
                Assert.AreSame(second, GameLaunchContext.Level,
                    "启动上下文也要指到下一关：之后有人重进场景，进的还是这一关");

                // 换关要在日志里说一声（用户点了按钮总得有反馈）
                var notes = new List<string>();
                session.DrainNotes(notes);
                Assert.IsTrue(notes.Exists(n => n.Contains("切到下一关") && n.Contains("第 2 关")),
                    "换关后应该留一条日志：" + string.Join(" / ", notes));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void 下一关_目录里只有一关时按钮点不动()
        {
            var only = MakeLevel("only", "唯一的一关", roundLimit: 9);
            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { only };

            try
            {
                var session = BuildScene(catalog, out var loader, out _);
                loader.LoadLevel(only, 111);

                Assert.IsFalse(loader.HasNextLevel);
                Assert.IsNull(loader.NextLevelName);
                Assert.AreEqual("下一关（没有下一关）", Hud.NextLevelButtonLabel(session));
                Assert.IsFalse(session.GoToNextLevel());
                Assert.AreSame(only, loader.CurrentLevel, "点不动的时候当然不该换关");
                Assert.AreEqual(9, session.Engine.Rules.RoundLimit);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(only);
            }
        }

        [Test]
        public void 下一关_换的是新种子_固定种子的关卡仍然用它的固定种子()
        {
            var first = MakeLevel("aaa", "第 1 关", roundLimit: 15);
            var puzzle = MakeLevel("bbb", "谜题关", roundLimit: 15);
            puzzle.useFixedSeed = true;
            puzzle.fixedSeed = 20240607;

            var catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            catalog.levels = new[] { first, puzzle };

            try
            {
                var session = BuildScene(catalog, out var loader, out _);
                loader.LoadLevel(first, 4242);
                Assert.AreEqual(4242, loader.CurrentSeed);

                Assert.IsTrue(session.GoToNextLevel());
                Assert.AreEqual(20240607, loader.CurrentSeed, "要求固定种子的关卡不能被随机化");
                Assert.AreEqual(20240607, session.Engine.Seed);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(puzzle);
            }
        }

        // ------------------------------------------------------------ 场景骨架

        /// <summary>
        /// 搭一套能跑的最小场景：GameSession + BoardView + HandView + ClickRouter + LevelLoader。
        /// 关卡预制体故意留空（这一关没有机制）—— 换关要验的是「关卡 / 规则 / 引擎」换了没有。
        /// </summary>
        GameSession BuildScene(LevelCatalog catalog, out LevelLoader loader, out Transform levelRoot)
        {
            var root = new GameObject("level-switch-root");
            _root = root;

            var systems = new GameObject("Systems");
            systems.transform.SetParent(root.transform, false);
            var session = systems.AddComponent<GameSession>();
            var input = systems.AddComponent<ClickRouter>();

            var boardGo = new GameObject("BoardAnchor");
            boardGo.transform.SetParent(root.transform, false);
            var board = boardGo.AddComponent<BoardView>();

            var handGo = new GameObject("HandAnchor");
            handGo.transform.SetParent(root.transform, false);
            var hand = handGo.AddComponent<HandView>();

            var levelRootGo = new GameObject("LevelRoot");
            levelRootGo.transform.SetParent(root.transform, false);
            levelRoot = levelRootGo.transform;

            var loaderGo = new GameObject("LevelLoader");
            loaderGo.transform.SetParent(root.transform, false);
            loader = loaderGo.AddComponent<LevelLoader>();

            // 私有 [SerializeField] 用 SerializedObject 填（和别的用例同一套手法），
            // 顺便把「字段被改名」这件事钉在编译/用例上。
            SetRef(session, "board", board);
            SetRef(session, "hand", hand);
            SetRef(session, "input", input);
            SetRef(loader, "session", session);
            SetRef(loader, "levelRoot", levelRoot);
            SetRef(loader, "catalog", catalog);
            SetRef(loader, "inlineLevel", null);

            return session;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static LevelDefinition MakeLevel(string id, string displayName, int roundLimit)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.levelId = id;
            level.displayName = displayName;
            level.sceneName = "Game";
            level.rules = new Rules
            {
                BoardWidth = 4,
                BoardHeight = 4,
                CreepsPerElement = 0, // 空棋盘：换关只看规则和回合，不靠随机铺盘
                InitialHandSize = 0,
                RoundLimit = roundLimit,
            };
            level.layoutText = "....\n....\n....\n....";
            return level;
        }
    }
}
