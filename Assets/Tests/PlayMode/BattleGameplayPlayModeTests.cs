using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public class BattleGameplayPlayModeTests
    {
        readonly List<LevelDefinition> _levels = new List<LevelDefinition>();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            Object.FindFirstObjectByType<GameSession>().AnimationsEnabled = false;
            yield return WaitForAction();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            foreach (var level in _levels) Object.Destroy(level);
            _levels.Clear();
            GameLaunchContext.Clear();
        }

        static IEnumerator WaitForAction()
        {
            var session = Object.FindFirstObjectByType<GameSession>();
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Engine.Phase != GamePhase.PlayerAction || session.Busy) && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(GamePhase.PlayerAction, session.Engine.Phase);
            Assert.IsFalse(session.Busy);
            yield return null;
        }

        LevelDefinition MakeLevel(int cards = 6, GoalKind goal = GoalKind.SurviveRounds)
        {
            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            _levels.Add(level);
            level.levelId = "ui_test";
            level.displayName = "界面对局检查";
            level.layoutText = "....\n.金..\n....";
            level.rules = new Rules
            {
                BoardWidth = 4, BoardHeight = 3, MaxMoveSteps = 1, CreepsPerElement = 0,
                InitialHandSize = cards, HandLimit = cards, MaxDiscardsPerRound = 2, RoundLimit = 4,
                Goal = new GoalSpec { Kind = goal, Rounds = 1 }
            };
            level.fixedInitialHand = Enumerable.Repeat(Element.Water, cards).ToArray();
            return level;
        }

        static void AssertHandMatches(GameSession session, BattleUiController ui)
        {
            var shown = ui.Cards.Where(c => c.gameObject.activeSelf).ToArray();
            CollectionAssert.AreEqual(session.Engine.Hand.Select(c => c.Id), shown.Select(c => c.CardId));
            CollectionAssert.AreEqual(session.Engine.Hand.Select(c => c.Kind), shown.Select(c => c.Kind));
            Assert.AreEqual(session.SelectedCardId, ui.SelectedCardIndex < 0 ? -1 : ui.Cards[ui.SelectedCardIndex].CardId);
        }

        [UnityTest]
        public IEnumerator 真实关卡_六张手牌出牌弃牌都按卡牌ID操作()
        {
            var loader = Object.FindFirstObjectByType<LevelLoader>();
            var session = Object.FindFirstObjectByType<GameSession>();
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            loader.LoadLevel(MakeLevel(), 123);
            yield return WaitForAction();
            AssertHandMatches(session, hud.Ui);
            Assert.AreEqual(6, session.Engine.Hand.Count);
            Assert.AreEqual(0, session.Hand.CardObjectCount, "界面接入后不重复生成旧手牌");
            Assert.IsFalse(session.Input.enabled, "背景空白区域不能通过旧输入触发棋盘操作");
            hud.Ui.Cards[5].Button.onClick.Invoke();
            Assert.AreEqual(session.Engine.Hand[5].Id, session.SelectedCardId, "新增的第六个手牌按钮也要绑定真实卡牌");
            hud.Ui.Cards[5].Button.onClick.Invoke();
            Assert.AreEqual(-1, session.SelectedCardId);
            int playedId = session.Engine.Hand[0].Id;
            hud.Ui.SelectCard(0);
            Assert.AreEqual(playedId, session.SelectedCardId);
            var cell = session.Engine.CellsForElementCard(session.SelectedCard).First(p => session.Engine.Board.At(p) == null);
            var board = session.Board;
            float u = (cell.X * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardWidthWorld;
            float v = (cell.Y * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardHeightWorld;
            var local = hud.Viewport.BoardUvToLocal(new Vector2(u, v));
            Assert.IsTrue(hud.Viewport.TryCellAtLocalPoint(local, out var mapped));
            Assert.AreEqual(cell, mapped);
            var click = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = RectTransformUtility.WorldToScreenPoint(null, hud.Viewport.rectTransform.TransformPoint(local))
            };
            hud.Viewport.OnPointerClick(click);
            yield return null;
            yield return null;
            float playReadyUntil = Time.realtimeSinceStartup + 10;
            while (session.Busy && Time.realtimeSinceStartup < playReadyUntil) yield return null;
            Assert.IsFalse(session.Busy);
            yield return null;
            Assert.IsNull(session.Engine.FindCard(playedId));
            Assert.AreEqual(Element.Water, session.Engine.Board.At(cell).Element);
            AssertHandMatches(session, hud.Ui);
            hud.transform.Find("Composition/Controls/NextRoundButton").GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(GamePhase.Discard, session.Engine.Phase);
            float discardReadyUntil = Time.realtimeSinceStartup + 10;
            while ((session.Busy || session.Engine.AwaitingStepPlayback) && Time.realtimeSinceStartup < discardReadyUntil)
                yield return null;
            Assert.IsFalse(session.Busy);
            Assert.IsFalse(session.Engine.AwaitingStepPlayback);
            yield return null;
            for (int i = 0; i < 2; i++)
            {
                int id = session.Engine.Hand[0].Id;
                hud.Ui.SelectCard(0);
                yield return null;
                Assert.AreEqual(id, session.SelectedCardId, "弃牌阶段必须保留选择，等待按钮确认");
                var discardButton = hud.transform.Find("Composition/Controls/DiscardButton").GetComponent<Button>();
                Assert.IsTrue(discardButton.IsInteractable(), "应等阶段动画结束后再点击弃牌按钮");
                discardButton.onClick.Invoke();
                Assert.IsNull(session.Engine.FindCard(id),
                    $"弃牌应成功；busy={session.Busy}，剩余次数={session.Engine.DiscardsRemaining}，原因={session.Engine.FailReason}");
            }
            int retained = session.Engine.Hand[0].Id;
            hud.Ui.SelectCard(0);
            hud.Ui.DiscardRequested.Invoke(0);
            Assert.IsNotNull(session.Engine.FindCard(retained), "达到弃牌上限后不得消耗卡牌");
            yield return null;
            AssertHandMatches(session, hud.Ui);
            hud.Restart();
            yield return WaitForAction();
            Assert.AreEqual(0, hud.GetComponentsInChildren<CardDissolveEffect>(true).Length,
                "重开应清掉上局手牌消散副本");
        }

        [UnityTest]
        public IEnumerator 手牌排布_一到十二张居中且选中卡面不超出手牌区()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var loader = Object.FindFirstObjectByType<LevelLoader>();
            foreach (int count in new[] { 1, 3, 5, 6, 12 })
            {
                loader.LoadLevel(MakeLevel(count), 84);
                yield return WaitForAction();
                var handRect = (RectTransform)hud.Ui.Cards[0].transform.parent;
                var shown = hud.Ui.Cards.Take(count).ToArray();
                float first = ((RectTransform)shown[0].transform).anchoredPosition.x;
                float last = ((RectTransform)shown[count - 1].transform).anchoredPosition.x;
                Assert.AreEqual(handRect.rect.width, first + last, .1f, "整组手牌应围绕容器中央排布");
                if (count == 5)
                {
                    Assert.Less(last - first, 1000, "五张牌放大后两侧仍应留边");
                    var displayedSize = shown[0].ArtworkSize * shown[0].transform.localScale.x;
                    Assert.GreaterOrEqual(displayedSize.x, 170, "五张牌的卡面应足够大，便于看清与点击");
                    Assert.GreaterOrEqual(displayedSize.y, 275);
                }
                float previousRight = float.NegativeInfinity;
                foreach (var card in shown)
                {
                    var bounds = new Vector3[4];
                    ((RectTransform)card.transform.Find("Artwork")).GetWorldCorners(bounds);
                    var localX = bounds.Select(p => handRect.InverseTransformPoint(p).x).ToArray();
                    Assert.GreaterOrEqual(localX.Min() - previousRight, count == 5 ? 24 : 8,
                        "卡面之间应留出可见间隔，不能重叠或靠得太近");
                    if (count == 5 && !float.IsNegativeInfinity(previousRight))
                        Assert.LessOrEqual(localX.Min() - previousRight, 36, "五张牌之间的空隙应适中，不能散得太开");
                    previousRight = localX.Max();
                }
                for (int i = 0; i < count; i++)
                {
                    hud.Ui.SelectCard(i);
                    var corners = new Vector3[4];
                    ((RectTransform)shown[i].transform.Find("Artwork")).GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        Assert.IsTrue(handRect.rect.Contains((Vector2)handRect.InverseTransformPoint(corner)),
                            $"{count} 张手牌中第 {i + 1} 张选中后仍应完整留在手牌区；"
                            + $"corner={handRect.InverseTransformPoint(corner)}，hand={handRect.rect}，"
                            + $"card={((RectTransform)shown[i].transform).rect}，position={((RectTransform)shown[i].transform).anchoredPosition}，"
                            + $"artwork={((RectTransform)shown[i].transform.Find("Artwork")).anchoredPosition}，scale={shown[i].transform.localScale}");
                        var composition = (RectTransform)hud.transform.Find("Composition");
                        float imageY = composition.rect.yMax - composition.InverseTransformPoint(corner).y;
                        Assert.GreaterOrEqual(imageY, 764, "选中后的卡面应留在棋盘下方");
                    }
                    Assert.AreEqual(handRect.childCount - 1, shown[i].transform.GetSiblingIndex(), "选中的牌应显示在最前面");
                }
            }
        }

        [UnityTest]
        public IEnumerator 出牌补位_保留每张牌的对象并从原位消散()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var session = hud.Session;
            var level = MakeLevel(5);
            level.fixedInitialHand = new[] { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth };
            Object.FindFirstObjectByType<LevelLoader>().LoadLevel(level, 123);
            yield return WaitForAction();
            var before = hud.Ui.Cards.Take(5).ToArray();
            var ids = before.Select(c => c.CardId).ToArray();
            var positions = before.Select(c => ((RectTransform)c.transform).anchoredPosition).ToArray();
            hud.Ui.SelectCard(1);
            var artwork = (RectTransform)before[1].transform.Find("Artwork");
            Vector3 dissolvePosition = artwork.TransformPoint(artwork.rect.center);
            Quaternion dissolveRotation = artwork.rotation;
            var cell = session.Engine.CellsForElementCard(session.SelectedCard).First(p => session.Engine.Board.At(p) == null);
            session.PlaySelectedCardAt(cell);
            hud.Ui.SetHand(session.Engine.Hand);
            var effect = hud.GetComponentsInChildren<CardDissolveEffect>().Single();
            Assert.Less(Vector3.Distance(dissolvePosition, effect.transform.position), .01f);
            Assert.Less(Quaternion.Angle(dissolveRotation, effect.transform.rotation), .01f);
            Assert.AreEqual(artwork.gameObject.layer, effect.gameObject.layer, "相机模式下的消散也应显示在 UI 层");
            for (int i = 0; i < before.Length; i++)
            {
                if (i == 1) continue;
                var retained = hud.Ui.Cards.Single(c => c.CardId == ids[i]);
                Assert.AreSame(before[i], retained, "出牌后不能把剩余卡面重新绑定到别的槽位");
                Assert.Less(Vector2.Distance(positions[i], ((RectTransform)retained.transform).anchoredPosition), .01f,
                    "补位开始前应保留原位置，不能瞬移");
            }
            yield return new WaitForSecondsRealtime(.5f);
            Assert.Less(Vector3.Distance(dissolvePosition, effect.transform.position), .01f, "补位不能带走正在消散的牌");
            Assert.Greater(Vector2.Distance(positions[0], ((RectTransform)before[0].transform).anchoredPosition), 1,
                "剩余牌应平滑补到新的居中位置");
            AssertHandMatches(session, hud.Ui);
            for (int i = 0; i < session.Engine.Hand.Count; i++)
            {
                session.CancelSelection();
                hud.Ui.SetSelectedCardId(-1);
                hud.Ui.Cards[i].Button.onClick.Invoke();
                Assert.AreEqual(session.Engine.Hand[i].Id, session.SelectedCardId, "补位后按钮仍选中当前位置的真实卡牌");
            }
            hud.Ui.ClearSelection();
            hud.Ui.enabled = false;
            hud.Ui.enabled = true;
            int requests = 0;
            hud.Ui.CardSelected.AddListener(_ => requests++);
            hud.Ui.Cards[2].Button.onClick.Invoke();
            Assert.AreEqual(1, requests, "重启界面不能重复注册重排后的按钮事件");
            Assert.AreEqual(session.Engine.Hand[2].Id, session.SelectedCardId);
        }

        [UnityTest]
        public IEnumerator 设置暂停_禁止出牌推进并在关闭后恢复()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var session = hud.Session;
            int count = session.Engine.Hand.Count;
            int round = session.Engine.Round;
            int selectedId = session.Engine.Hand[0].Id;
            session.SelectHandCard(selectedId);
            hud.Ui.OpenSettings();
            Assert.IsTrue(session.Paused);
            Assert.AreEqual(0, Time.timeScale);
            Assert.IsFalse(session.SelectHandCard(session.Engine.Hand[0].Id));
            session.EndPlayerRound();
            yield return new WaitForSecondsRealtime(.15f);
            Assert.AreEqual(GamePhase.PlayerAction, session.Engine.Phase);
            Assert.AreEqual(round, session.Engine.Round);
            Assert.AreEqual(count, session.Engine.Hand.Count);
            Assert.AreEqual(selectedId, session.SelectedCardId, "暂停不能丢掉选中的手牌");
            hud.Ui.CloseSettings();
            Assert.IsFalse(session.Paused);
            Assert.AreEqual(1, Time.timeScale);
            Assert.IsTrue(session.SelectHandCard(session.Engine.Hand[0].Id));
        }

        [UnityTest]
        public IEnumerator 结算重开换关_刷新真实关卡与手牌()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var session = hud.Session;
            var loader = Object.FindFirstObjectByType<LevelLoader>();
            loader.LoadLevel(MakeLevel(), 77);
            yield return WaitForAction();
            var engine = session.Engine;
            hud.Ui.NextRoundRequested.Invoke();
            hud.Ui.NextRoundRequested.Invoke();
            for (int i = 0; i < 20 && !engine.ResultReported; i++) yield return null;
            Assert.AreEqual(GamePhase.Victory, engine.Phase);
            Assert.IsTrue(hud.transform.Find("Composition/ResultPanel").gameObject.activeSelf);
            hud.Restart();
            yield return WaitForAction();
            Assert.AreNotSame(engine, session.Engine);
            Assert.IsFalse(hud.transform.Find("Composition/ResultPanel").gameObject.activeSelf);
            AssertHandMatches(session, hud.Ui);
            Assert.IsTrue(session.GoToNextLevel());
            yield return WaitForAction();
            Assert.AreEqual("level_01", loader.CurrentLevel.levelId);
            AssertHandMatches(session, hud.Ui);
        }

        [UnityTest]
        public IEnumerator 秘术牌_显示真实类型并使用独立文字卡面()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var loader = Object.FindFirstObjectByType<LevelLoader>();
            foreach (bool fusion in new[] { true, false })
            {
                var level = MakeLevel(5);
                level.rules.UseCompositeElements = true;
                level.rules.SpecialCardPool = SpecialCardMask.All;
                loader.LoadLevel(level, 91);
                yield return WaitForAction();
                hud.Session.Engine.ClearHandForTest();
                for (int i = 0; i < 5; i++) hud.Session.Engine.AddSpecialCardForTest(
                    fusion ? CardKind.FusionCard : CardKind.DecomposeCard);
                yield return null;
                AssertHandMatches(hud.Session, hud.Ui);
                foreach (var card in hud.Ui.Cards.Where(c => c.gameObject.activeSelf))
                {
                    Assert.AreEqual(fusion ? CardKind.FusionCard : CardKind.DecomposeCard, card.Kind);
                    Assert.IsFalse(card.CardFace.gameObject.activeSelf, "秘术牌不得误用金元素卡面");
                }
            }
        }

        [UnityTest]
        public IEnumerator 不可见棋盘_格子中心贴合背景且间隙不出牌()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            var board = hud.Session.Board;
            Assert.IsTrue(board.TransparentCells);
            Assert.AreEqual(8, hud.Session.Engine.Board.Width);
            Assert.AreEqual(8, hud.Session.Engine.Board.Height);
            var composition = hud.transform.Find("Composition");
            var compositionRect = (RectTransform)composition;
            var viewport = hud.Viewport;
            Canvas.ForceUpdateCanvases();
            var renderedMesh = viewport.canvasRenderer.GetMesh();
            Assert.IsNotNull(renderedMesh);
            Assert.Greater(renderedMesh.vertexCount, 0);
            var vertices = renderedMesh.vertices;
            var textureCoordinates = renderedMesh.uv;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    var uv = new Vector2((x * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardWidthWorld,
                        (y * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardHeightWorld);
                    var local = viewport.BoardUvToLocal(uv);
                    Assert.IsTrue(viewport.TryCellAtLocalPoint(local, out var actual));
                    Assert.AreEqual(new Pos(x, y), actual, "所有元素的显示中心都必须命中同一格");
                    int vertex = System.Array.FindIndex(textureCoordinates, value => Vector2.Distance(value, uv) < .00001f);
                    Assert.GreaterOrEqual(vertex, 0, "实际渲染网格必须包含每个格子中心");
                    Assert.Less(Vector2.Distance(local, vertices[vertex]), .01f, "实际渲染位置和点击映射必须重合");
                }
            // 独立读图标记的前排、后排与中间格子中心，容许手绘边缘的小幅不规则。
            var cells = new[] { new Pos(0, 0), new Pos(7, 0), new Pos(0, 7), new Pos(7, 7),
                new Pos(3, 3), new Pos(4, 4), new Pos(0, 3), new Pos(7, 3) };
            var backgroundCenters = new[] { new Vector2(481, 724), new Vector2(1423, 724),
                new Vector2(560, 265), new Vector2(1348, 265), new Vector2(894, 512), new Vector2(1018, 446),
                new Vector2(516, 512), new Vector2(1388, 512) };
            for (int i = 0; i < cells.Length; i++)
            {
                var p = cells[i];
                var uv = new Vector2((p.X * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardWidthWorld,
                    (p.Y * (board.CellSize + board.CellGap) + board.CellSize / 2) / board.BoardHeightWorld);
                var position = composition.InverseTransformPoint(viewport.rectTransform.TransformPoint(viewport.BoardUvToLocal(uv)));
                var imagePosition = new Vector2(position.x - compositionRect.rect.xMin, compositionRect.rect.yMax - position.y);
                Assert.Less(Vector2.Distance(backgroundCenters[i], imagePosition), 5, $"背景格子 {p} 的中心应重合");
            }
            for (int i = 0; i < 7; i++)
            {
                var uv = new Vector2((i * (board.CellSize + board.CellGap) + board.CellSize + board.CellGap / 2) / board.BoardWidthWorld,
                    (board.CellSize / 2) / board.BoardHeightWorld);
                Assert.IsFalse(viewport.TryCellAtLocalPoint(viewport.BoardUvToLocal(uv), out _), "格子接缝处不得落入相邻格");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator 真实界面_保存两种尺寸的渲染预览()
        {
            var hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            hud.Session.Engine.ForceHandForTest(new[] { Element.Metal, Element.Wood, Element.Water, Element.Fire, Element.Earth });
            hud.Ui.ResetHand();
            yield return null;
            var canvas = hud.GetComponent<Canvas>();
            var camera = Camera.main;
            int previousMask = camera.cullingMask;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "HandSpacing"));
            Directory.CreateDirectory(directory);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                var target = new RenderTexture(size.x, size.y, 24);
                var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                var previousTarget = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.cullingMask = 1 << canvas.gameObject.layer;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    yield return null;
                    yield return null;
                    yield return new WaitForSecondsRealtime(.3f);
                    Assert.IsTrue(hud.Ui.Cards[0].Button.IsInteractable(), "画面预览应处于可操作的玩家阶段");
                    Canvas.ForceUpdateCanvases();
                    hud.GetComponent<BattleUiLayout>().Refresh();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(directory, $"gameplay-{size.x}x{size.y}.png"), pixels.EncodeToPNG());
                    Assert.Greater(pixels.GetPixels32().Count(c => c.r + c.g + c.b > 90), size.x * size.y / 2);
                }
                finally
                {
                    camera.targetTexture = null;
                    camera.cullingMask = previousMask;
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    RenderTexture.active = previousTarget;
                    target.Release();
                    Object.Destroy(target);
                    Object.Destroy(pixels);
                }
            }
        }
    }
}
