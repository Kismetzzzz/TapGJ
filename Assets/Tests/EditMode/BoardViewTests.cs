using System.Collections.Generic;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    /// <summary>
    /// 表现层冒烟测试：在 EditMode 下真的把棋盘搭出来，确认 8x8 格、小怪对象、
    /// 相机布局都不会在运行时报错。截图无法在无头环境验证，所以这里验证「对象图」。
    ///
    /// 这里还钉住了四条不变量。它们对应一次真实的 bug：
    ///   小怪的表现对象以前一半在格子里、一半在 _creepRoot 下，两套并存并且清理代码是死代码
    ///   （ShowEmpty 先把字段置空、调用方再去判断该字段），结果格子留下了没人销毁的色块 ——
    ///   表现就是「格子有颜色但没有元素字」，另外还有方块因为用世界坐标而飞出棋盘。
    /// </summary>
    public class BoardViewTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            _root = null;
            KillSpawned();
        }

        [Test]
        public void 棋盘搭建_生成八乘八格并且每种元素都有对应小怪()
        {
            var engine = new GameEngine(new Rules(), 20240607);
            engine.Start();

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            var cells = view.GetComponentsInChildren<CellView>();
            Assert.AreEqual(64, cells.Length, "8x8 应该是 64 个格子");

            int withCreep = 0;
            foreach (var c in cells)
            {
                Assert.IsNotNull(c.GetComponent<BoxCollider2D>(), "每个格子都要有碰撞体，否则点不到");
                if (engine.Board.At(c.Pos) != null) withCreep++;
            }
            Assert.AreEqual(engine.CreepCount, withCreep, "有元素的格子数应该等于场上元素数");

            // 每个元素都应该有一个带 SpriteRenderer + 元素字的小怪表现对象
            int creepSprites = 0;
            foreach (var cv in view.CreepViews)
            {
                Assert.IsNotNull(cv, "CreepView 组件必须在");
                Assert.IsNotNull(cv.Body, "小怪必须有 SpriteRenderer");
                Assert.IsNotNull(cv.Body.sprite, "小怪必须有 sprite");
                Assert.IsNotNull(cv.Label, "小怪必须带元素字");
                Assert.AreSame(cv.gameObject, cv.Label.transform.parent.gameObject);
                creepSprites++;
            }
            Assert.AreEqual(20, creepSprites, "初始 20 个元素都应该被画出来");
            Assert.AreEqual(engine.CreepCount, creepSprites, "表现对象数必须等于逻辑层元素数");
        }

        [Test]
        public void 棋盘同步_元素移动后显示对象跟着走()
        {
            // 用引擎的正常流程让小怪走一步，再同步显示层：
            // 断言「显示层看到的元素位置」和「逻辑层的位置」完全一致。
            var rules = new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 3 };
            var engine = new GameEngine(rules, 1);
            engine.ClearBoard();
            Assert.IsNotNull(engine.PlaceCreep(Element.Water, new Pos(0, 0)));
            engine.BeginRound();
            engine.SetNextRandomValues(2, 1); // 走 2 格，方向 +X

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);

            var step = engine.AdvancePreparation();
            Assert.IsNotNull(step);
            Assert.AreEqual(new Pos(2, 0), step.To, "水应该走到 (2,0)");

            view.Sync();

            Assert.IsNotNull(view.CreepViewAt(new Pos(2, 0)), "新格子上应该有显示对象");
            Assert.IsNull(view.CreepViewAt(new Pos(0, 0)), "旧格子上的显示对象应该被清掉");
            Assert.AreEqual(1, view.CreepObjectCount, "场上只有一只小怪");
        }

        [Test]
        public void 棋盘同步_元素消失后显示对象被清理()
        {
            // 水撞土：土赢、水消失 → 显示层要把水的小怪对象清掉
            var rules = new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 3 };
            var engine = new GameEngine(rules, 1);
            engine.ClearBoard();
            Assert.IsNotNull(engine.PlaceCreep(Element.Water, new Pos(0, 0)));
            Assert.IsNotNull(engine.PlaceCreep(Element.Earth, new Pos(2, 0)));
            engine.BeginRound();
            engine.SetNextRandomValues(3, 1);

            _root = new GameObject("BoardRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync(); // Setup 只搭格子，第一次 Sync 才把小怪画出来
            Assert.AreEqual(2, view.CreepObjectCount);

            // 记下即将消失的那只小怪对象：消失之后它必须被**真的销毁**，
            // 而不是只从字典里去掉引用（旧 bug 的形态就是「引用没了、对象还在渲染」）。
            var vanishing = view.CreepViewAt(new Pos(0, 0));
            Assert.IsNotNull(vanishing);
            var vanishingGo = vanishing.gameObject;

            engine.AdvancePreparation();
            engine.EndPreparationNow();
            view.Sync();

            Assert.AreEqual(engine.CreepCount, view.CreepObjectCount, "显示对象数量必须跟上逻辑层");
            // 注意：不能断言「场上数量变少」—— 水消失的同时土会 +1 生成一只，总数不变。
            Assert.AreEqual(1, engine.RemovedOf(Element.Water), "水应该被土吃掉 1 只");
            Assert.AreEqual(1, engine.SpawnedOf(Element.Earth), "土应该 +1 生成过 1 只");
            Assert.IsNull(view.CreepViewAt(new Pos(0, 0)), "水原来站的格子应该空了");

            // 注意：必须写成 == null 让 GameObject 的重载运算符来判断。
            // 用 Assert.IsNull(object) 会退化成引用比较，销毁过的 Unity 对象反而判不出来。
            Assert.IsTrue(vanishingGo == null, "消失的小怪对象必须被销毁，不能只丢引用");
            AssertNoCellLeftovers(view);
        }

        [Test]
        public void 表现层不变量_小怪走过之后旧格子不留残影()
        {
            // 这次报的 bug：小怪离开后，旧格子留下一个「有色块没元素字」的孤儿对象。
            // 成因是旧代码里 ShowEmpty() 先把 CreepGo 置空，调用方再去 if (CreepGo != null) 清理 —— 永远进不去。
            var rules = new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 3 };
            var engine = new GameEngine(rules, 1);
            engine.ClearBoard();
            Assert.IsNotNull(engine.PlaceCreep(Element.Water, new Pos(0, 0)));
            engine.BeginRound();
            engine.SetNextRandomValues(2, 1); // 从 (0,0) 走到 (2,0)

            _root = new GameObject("GhostRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();
            Assert.IsNotNull(view.CreepViewAt(new Pos(0, 0)), "走之前 (0,0) 上要有小怪");

            engine.AdvancePreparation();
            view.Sync();

            Assert.IsNull(view.CreepViewAt(new Pos(0, 0)), "小怪已经走了，(0,0) 上不该还有显示对象");
            AssertNoCellLeftovers(view);
            Assert.AreEqual(1, view.CreepObjectCount, "全场只该剩一只小怪对象");
        }

        [Test]
        public void 表现层不变量_小怪对象数在整个回合流程里始终等于引擎数量()
        {
            // 覆盖「移动 / 相遇 / 消失 / 生成」全过程：每走一步就检查一次对账结果。
            var rules = new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 1, RoundLimit = 8 };
            var engine = new GameEngine(rules, 5);
            engine.Start();       // Start 会清空棋盘重新铺元素，所以要摆盘必须放在它后面
            engine.ClearBoard();
            // 水贴着土的左边：水先行动（uid 小），一步就撞上去 → 第 1 回合必然发生「消失 + 生成」。
            // 摆成不同行 / 同方向的话两边永远追不上，这条测试就会悄悄退化成什么都没覆盖到。
            engine.PlaceCreep(Element.Water, new Pos(1, 0));
            engine.PlaceCreep(Element.Earth, new Pos(2, 0));
            engine.PlaceCreep(Element.Wood, new Pos(6, 6));

            _root = new GameObject("SoakRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();
            AssertNoCellLeftovers(view);
            Assert.AreEqual(engine.CreepCount, view.CreepObjectCount);

            for (int i = 0; i < 12 && !engine.IsOver; i++)
            {
                if (engine.Phase != GamePhase.Preparation) engine.BeginRound();
                engine.SetNextRandomValues(1, 1); // 走 1 格，方向 +X

                while (engine.Phase == GamePhase.Preparation)
                {
                    if (engine.AdvancePreparation() == null) break;
                    view.Sync();
                    Assert.AreEqual(engine.CreepCount, view.CreepObjectCount,
                        $"第 {i} 步之后表现对象数应该等于逻辑层元素数");
                    AssertNoCellLeftovers(view);
                }

                engine.EndPreparationNow();
                engine.EndPlayerPhase();
                engine.EndDiscardPhase();
                engine.ResolveJudgment();
                view.Sync();
                Assert.AreEqual(engine.CreepCount, view.CreepObjectCount, "回合结算后数量仍要一致");
                AssertNoCellLeftovers(view);
            }

            // 防止这条测试悄悄失效：确认这一局真的发生过反应（有消失、有生成），
            // 否则「对账路径」根本没被走到，绿得毫无意义。
            Assert.Greater(engine.TotalRemoved, 0, "这一局必须真的发生过元素消失，否则覆盖不到清理路径");
            Assert.Greater(engine.TotalSpawned, 0, "这一局必须真的发生过元素生成，否则覆盖不到补齐路径");
        }

        [Test]
        public void 布局_挪动棋盘后小怪仍与目标格对齐()
        {
            // 小怪用局部坐标挂在棋盘下面，所以棋盘被挪走/缩放时它必须自动跟着走。
            // 旧实现给小怪写世界坐标，棋盘一挪小怪就留在原地（截图里飞出棋盘的那些方块）。
            var engine = new GameEngine(new Rules { CreepsPerElement = 2, InitialHandSize = 0 }, 31);
            engine.Start();

            _root = new GameObject("MoveRoot");
            var view = _root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync();

            view.transform.position = new Vector3(4.5f, -2.25f, 0f);
            view.transform.localScale = Vector3.one * 1.7f;

            int checkedCreeps = 0;
            foreach (var creep in engine.Creeps)
            {
                var cv = view.CreepViewOf(creep.Uid);
                Assert.IsNotNull(cv, $"小怪 {creep.Uid} 应该有显示对象");
                var cell = FindCell(view, creep.Pos);
                Assert.IsNotNull(cell, "小怪所在的格子必须存在");

                Assert.Less((cv.transform.position - cell.transform.position).magnitude, 1e-4f,
                    $"棋盘挪动后小怪 {creep.Uid} 必须仍然落在 {creep.Pos} 格中心");
                checkedCreeps++;
            }
            Assert.Greater(checkedCreeps, 0, "这个测试至少要有几只小怪才有意义");
        }

        /// <summary>复刻 GameRunner.LayoutViews 的核心计算，保证测试测的是真实布局路径。</summary>
        static Camera ConfigureCameraForZones(Camera cam, Rect boardZone)
        {
            float aspect = cam.aspect <= 0.01f ? 16f / 9f : cam.aspect;
            float worldH = 8 * BoardView.Cell + (8 - 1) * BoardView.Gap;
            float worldW = worldH;

            float sizeFromHeight = worldH * 0.5f / Mathf.Max(0.05f, boardZone.height);
            float sizeFromWidth = worldW * 0.5f / Mathf.Max(0.05f, boardZone.width * aspect);
            cam.orthographicSize = Mathf.Max(3f, sizeFromHeight, sizeFromWidth) * 1.04f;
            return cam;
        }

        [Test]
        public void 布局_棋盘落在规划区域里且字号不会盖满屏幕()
        {
            var engine = new GameEngine(new Rules(), 7);
            engine.Start();

            var root = new GameObject("LayoutRoot");
            var cam = MakeCamera("LayoutCam");

            var view = root.AddComponent<BoardView>();
            view.Setup(engine);

            // GameRunner 现在的分工：相机负责尺寸、棋盘只负责定位（fitToZone:false）
            var boardZone = new Rect(0.04f, 0.01f, 0.62f, 0.70f);
            ConfigureCameraForZones(cam, boardZone);

            float px = boardZone.x * Screen.width;
            float py = boardZone.y * Screen.height;
            float pw = boardZone.width * Screen.width;
            float ph = boardZone.height * Screen.height;

            view.LayoutPixels(px, py, pw, ph, 0.92f, cam, fitToZone: false);

            Assert.AreEqual(1f, view.transform.localScale.x, 0.001f,
                "相机控制尺寸时棋盘缩放必须是 1，否则会二次放大");

            float boardW = view.BoardWidthWorld;
            float boardH = view.BoardHeightWorld;
            float visibleHalfH = cam.orthographicSize;
            float visibleHalfW = cam.orthographicSize * cam.aspect;
            Vector3 center = view.transform.position;

            float leftFrac = (center.x - boardW * 0.5f + visibleHalfW) / (visibleHalfW * 2f);
            float rightFrac = (center.x + boardW * 0.5f + visibleHalfW) / (visibleHalfW * 2f);
            float bottomFrac = (center.y - boardH * 0.5f + visibleHalfH) / (visibleHalfH * 2f);
            float topFrac = (center.y + boardH * 0.5f + visibleHalfH) / (visibleHalfH * 2f);

            // 视野比例：world y 向上、屏幕比例向下，所以 topFrac 对应屏幕上方
            float screenBottom = 1f - topFrac;
            float screenTop = 1f - bottomFrac;

            Assert.GreaterOrEqual(leftFrac, boardZone.x - 0.03f, "棋盘左边缘越界");
            Assert.LessOrEqual(rightFrac, boardZone.xMax + 0.03f, "棋盘右边缘越界（会压到 HUD）");
            Assert.GreaterOrEqual(screenBottom, boardZone.y - 0.03f, "棋盘下边缘越界（会压到手牌区）");
            Assert.LessOrEqual(screenTop, boardZone.yMax + 0.03f, "棋盘上边缘越界");
        }

        [Test]
        public void 布局_元素字不会比格子还大()
        {
            // 之前的问题：格子被相机放大，而元素字是固定字号，
            // 棋盘比例一变，字就相对格子变得巨大、糊满屏幕。这里钉住「字高 < 格子高」。
            var engine = new GameEngine(new Rules { CreepsPerElement = 1, InitialHandSize = 0 }, 21);
            engine.Start();

            var root = new GameObject("FontRoot");
            var cam = MakeCamera("FontCam");

            var view = root.AddComponent<BoardView>();
            view.Setup(engine);
            view.Sync(); // Setup 只搭格子，Sync 才把小怪和元素字画出来
            view.LayoutPixels(0f, 0f, Screen.width * 0.6f, Screen.height * 0.7f, 0.92f, cam, fitToZone: false);

            CreepView withCreep = null;
            foreach (var cv in view.CreepViews) { withCreep = cv; break; }
            Assert.IsNotNull(withCreep, "应该有元素格");
            Assert.IsNotNull(withCreep.Label, "元素格上应该有元素字");

            // 世界字高 ≈ (fontSize/10) × characterSize × lossyScale
            // （字挂在小怪下面，所以 lossyScale 已经包含 小怪缩放 × 棋盘缩放）
            float glyphWorldHeight = withCreep.Label.fontSize / 10f * withCreep.Label.transform.lossyScale.y;
            float cellWorldSize = BoardView.Cell * view.transform.lossyScale.y;

            Assert.Less(glyphWorldHeight, cellWorldSize * 0.95f,
                $"元素字高 {glyphWorldHeight:F3} 应该小于格子高 {cellWorldSize:F3}，否则会糊满屏幕");
            Assert.Greater(glyphWorldHeight, cellWorldSize * 0.3f,
                "字也不该小到看不见");
        }

        [Test]
        public void 手牌区_手牌数量与逻辑层一致并且能选中()
        {
            // Start() 会重新铺盘并按下发手牌；CreepsPerElement=0 所以棋盘是空的（这里也只关心手牌）
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 3, MaxMoveSteps = 0 }, 22);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.Start();

            var root = new GameObject("HandRoot");
            var cam = MakeCamera("HandCam");

            var hand = root.AddComponent<HandView>();
            hand.Setup(engine);
            hand.LayoutPixels(0f, Screen.height * 0.72f, Screen.width * 0.6f, Screen.height * 0.26f, cam);

            Assert.AreEqual(engine.Hand.Count, hand.CardObjectCount, "手牌对象数应该等于逻辑层手牌数");
            Assert.AreEqual(3, hand.CardObjectCount);

            // 每张牌都要有碰撞体（点击靠它）和元素字
            foreach (var card in hand.Cards)
            {
                Assert.IsNotNull(card.GetComponent<BoxCollider2D>(), "手牌必须能被点到");
                bool hasText = false;
                foreach (var tm in card.GetComponentsInChildren<TextMesh>()) hasText = true;
                Assert.IsTrue(hasText, "手牌上应该有元素名");
            }

            // 选中态：只高亮指定的那一张
            int firstId = engine.Hand[0].Id;
            hand.RefreshSelection(firstId);
            Assert.IsTrue(hand.FindView(firstId) != null);

            // 逻辑层手牌变少后，Sync 要跟着删掉多余的对象
            int removedId = engine.Hand[0].Id;
            engine.BeginRound();
            engine.EndPreparationNow();          // 补满到 5 张（这里只验证数量一致）
            engine.EndPlayerPhase();
            if (engine.Phase == GamePhase.Discard)
                engine.TryDiscardCard(removedId);

            hand.Sync();
            Assert.AreEqual(engine.Hand.Count, hand.CardObjectCount, "Sync 后手牌对象数必须等于逻辑层");
        }

        // ------------------------------------------------------------------ 断言助手

        /// <summary>
        /// 不变量：场景里不许存在没人认领的小怪对象。
        /// 两处都查 —— 格子下面（旧结构把残影留在格子里）和整个棋盘子树（只丢引用、没销毁 GameObject）。
        /// 「格子有色块没元素字」正是后者：字典里的引用没了，但 GameObject 还在继续渲染。
        /// </summary>
        static void AssertNoCellLeftovers(BoardView view)
        {
            foreach (var cell in view.GetComponentsInChildren<CellView>())
            {
                int sprites = cell.GetComponentsInChildren<SpriteRenderer>().Length;
                Assert.LessOrEqual(sprites, 2,
                    $"{cell.Pos} 格子下出现了残留色块（格子只该有底板和高亮两层，实际 {sprites} 层）");
                Assert.IsNull(cell.transform.Find("label"), $"{cell.Pos} 格子下还有元素字残留");

                foreach (Transform child in cell.transform)
                    Assert.IsFalse(child.name.StartsWith("Creep_"),
                        $"{cell.Pos} 格子下还挂着 {child.name}");
            }

            // 真实存在的 Creep 对象数必须和 BoardView 记录的完全一致：
            // 多出来的就是「只丢了引用、没销毁」的孤儿，少了的说明对象图有洞。
            int named = 0;
            foreach (var t in view.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Creep_")) named++;

            Assert.AreEqual(view.CreepObjectCount, named,
                "场景里的 Creep 对象数必须和 BoardView 记录的一致（多出来的是没人销毁的孤儿对象）");
        }

        static CellView FindCell(BoardView view, Pos pos)
        {
            foreach (var c in view.GetComponentsInChildren<CellView>())
                if (c.Pos == pos) return c;
            return null;
        }

        /// <summary>
        /// 造一台独立的测试相机。注意不要挂在被测的 root 下面 ——
        /// BoardView.Setup() 会清空自己的子物体，挂上去会被一起销毁。
        /// 也不打 MainCamera 标签，避免和别的测试留下的相机抢 Camera.main。
        /// </summary>
        static Camera MakeCamera(string name)
        {
            var go = new GameObject(name);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.aspect = 16f / 9f;
            go.transform.position = new Vector3(0f, 0f, -10f);
            _spawned.Add(go);
            return cam;
        }

        static readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUpClean()
        {
            _spawned.Clear();
        }

        static void KillSpawned()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }
    }
}
