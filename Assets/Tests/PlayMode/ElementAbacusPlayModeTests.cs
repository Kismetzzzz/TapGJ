using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public class ElementAbacusPlayModeTests
    {
        BattleGameplayHud _hud;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            _hud = Object.FindFirstObjectByType<BattleGameplayHud>();
            _hud.Session.AnimationsEnabled = false;
            float until = Time.realtimeSinceStartup + 15;
            while ((_hud.Session.Busy || _hud.Session.Engine.Phase != GamePhase.PlayerAction)
                   && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(GamePhase.PlayerAction, _hud.Session.Engine.Phase);
            Assert.IsFalse(_hud.Session.Busy);
            StartSession(5);
            yield return null;
        }

        void StartSession(int capacity)
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0,
                HandLimit = 0, MaxMoveSteps = 0, BottleCapacity = capacity,
                Goal = GoalSpec.Survive(500), RoundLimit = 500 }, 12);
            engine.Start();
            engine.EndPreparationNow();
            engine.ForceHandForTest(ElementDefs.All);
            _hud.Session.Begin(engine, 12);
        }

        ElementAbacusView Abacus => _hud.GetComponentInChildren<ElementAbacusView>();

        IEnumerator WaitForAbacus()
        {
            yield return null;
            float until = Time.realtimeSinceStartup + 12;
            while (Abacus.IsAnimating && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsFalse(Abacus.IsAnimating, "算盘动画应完成，不能丢失队列或卡住");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
        }

        [UnityTest]
        public IEnumerator 算盘_五档都能选牌但不会改变真实收集进度()
        {
            _hud.Session.Engine.AddBottleCharge(Element.Water, 3);
            yield return WaitForAbacus();
            var rail = _hud.transform.Find("Composition/Controls/ElementRail");
            Assert.IsNotNull(rail.GetComponent<ElementAbacusView>());
            Assert.AreEqual(5, rail.GetComponentsInChildren<Button>().Length);
            Canvas.ForceUpdateCanvases();
            foreach (var label in rail.GetComponentsInChildren<Text>())
                Assert.Greater(label.cachedTextGenerator.vertexCount, 0, $"{label.name}的文字应正常显示");
            Assert.IsTrue(rail.GetComponentsInChildren<Transform>().All(child => child.gameObject.layer == _hud.gameObject.layer),
                "算盘和整个界面应使用同一渲染层");
            foreach (var element in ElementDefs.All)
            {
                var column = rail.Find("Frame/Element_" + element);
                var button = column.GetComponent<Button>();
                Assert.IsTrue(button.IsInteractable());
                Assert.IsNotNull(column.Find("Progress").GetComponent<Text>());
                var screenPoint = RectTransformUtility.WorldToScreenPoint(null,
                    column.TransformPoint(new Vector3(28, -200)));
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
                Assert.AreEqual(button, hits.First().gameObject.GetComponentInParent<Button>(), "装饰不能拦截算盘点击");
                button.onClick.Invoke();
                yield return new WaitForSecondsRealtime(.2f);
                Assert.AreEqual(element, _hud.Session.SelectedCard.Element);
                Assert.AreEqual((int)element, _hud.Ui.SelectedElementIndex);
                Assert.AreEqual(3, Abacus.RaisedLowerCount(Element.Water), "选牌不能拨动资源珠");
                Assert.AreEqual(5, _hud.Session.Engine.Hand.Count, "选算盘不消耗卡牌");
            }

            _hud.Ui.SelectCard(0);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual((int)_hud.Session.SelectedCard.Element, _hud.Ui.SelectedElementIndex,
                "直接点手牌也要同步算盘");
            _hud.Ui.ClearSelection();
            yield return new WaitForSecondsRealtime(.2f);
            Assert.AreEqual(-1, _hud.Ui.SelectedElementIndex);
            Assert.AreEqual(3, Abacus.DisplayedCount(Element.Water), "取消选牌也不能清空进度");
        }

        [UnityTest]
        public IEnumerator 三点门槛_拨三颗后闪亮发一张牌并归位()
        {
            StartSession(3);
            _hud.Session.Engine.AddBottleCharge(Element.Fire, 2);
            yield return WaitForAbacus();
            Assert.AreEqual(2, Abacus.RaisedLowerCount(Element.Fire));
            Assert.AreEqual(0, Abacus.CompletedRewards);
            _hud.Session.Engine.AddBottleCharge(Element.Fire, 1);
            yield return null;
            yield return null;
            Assert.AreEqual(6, _hud.Session.Engine.Hand.Count);
            Assert.AreEqual(5, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf), "新牌要在闪亮飞牌结束后才入手牌区");
            var glow = _hud.transform.Find("Composition/Controls/ElementRail/Frame/Element_Fire/ChargeFlash").GetComponent<CanvasGroup>();
            float flashUntil = Time.realtimeSinceStartup + 3;
            while (glow.alpha < .1f && Time.realtimeSinceStartup < flashUntil) yield return null;
            Assert.GreaterOrEqual(glow.alpha, .1f, "集齐时元素图标必须闪亮");
            yield return WaitForAbacus();
            Assert.AreEqual(1, Abacus.CompletedRewards);
            Assert.AreEqual(0, Abacus.RaisedLowerCount(Element.Fire));
            Assert.AreEqual(6, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf));
        }

        [UnityTest]
        public IEnumerator 十点门槛_先五进位_再五发牌_上下珠全部归位()
        {
            StartSession(10);
            _hud.Session.Engine.AddBottleCharge(Element.Water, 5);
            yield return WaitForAbacus();
            Assert.AreEqual(5, Abacus.DisplayedCount(Element.Water));
            Assert.AreEqual(0, Abacus.RaisedLowerCount(Element.Water));
            Assert.AreEqual(1, Abacus.RaisedUpperCount(Element.Water));
            Assert.AreEqual(0, Abacus.CompletedRewards);
            _hud.Session.Engine.AddBottleCharge(Element.Water, 5);
            yield return new WaitForSecondsRealtime(.1f);
            Assert.AreEqual(5, Abacus.RaisedLowerCount(Element.Water));
            Assert.AreEqual(1, Abacus.RaisedUpperCount(Element.Water));
            yield return WaitForAbacus();
            Assert.AreEqual(1, Abacus.CompletedRewards);
            Assert.AreEqual(0, Abacus.RaisedLowerCount(Element.Water));
            Assert.AreEqual(0, Abacus.RaisedUpperCount(Element.Water));
            Assert.AreEqual(6, _hud.Session.Engine.Hand.Count);
        }

        [UnityTest]
        public IEnumerator 连续超过门槛_逐张发牌并保留余数_真实消失也会充能()
        {
            _hud.Session.Engine.AddBottleCharge(Element.Metal, 12);
            yield return WaitForAbacus();
            Assert.AreEqual(2, Abacus.CompletedRewards);
            Assert.AreEqual(2, Abacus.DisplayedCount(Element.Metal));
            Assert.AreEqual(7, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf));
            var creep = _hud.Session.Engine.PlaceCreep(Element.Wood, new Pos(0, 0));
            Assert.IsTrue(_hud.Session.Engine.DestroyCreep(creep.Uid));
            yield return WaitForAbacus();
            Assert.AreEqual(1, Abacus.DisplayedCount(Element.Wood));
        }

        [UnityTest]
        public IEnumerator 暂停停止拨珠_临时门槛和重新开局都能同步()
        {
            _hud.Session.Engine.SetBottleCapacityForRound(10);
            yield return WaitForAbacus();
            _hud.Session.Engine.AddBottleCharge(Element.Earth, 5);
            yield return new WaitForSecondsRealtime(.06f);
            _hud.Ui.OpenSettings();
            var bead = (RectTransform)_hud.transform.Find("Composition/Controls/ElementRail/Frame/Element_Earth/LowerBead0");
            var position = bead.anchoredPosition;
            yield return new WaitForSecondsRealtime(.25f);
            Assert.AreEqual(position, bead.anchoredPosition, "暂停期间不能继续播放拨珠");
            _hud.Ui.CloseSettings();
            yield return WaitForAbacus();
            Assert.AreEqual(1, Abacus.RaisedUpperCount(Element.Earth));
            _hud.Session.Engine.AddBottleCharge(Element.Earth, 5);
            yield return null;
            StartSession(5);
            yield return WaitForAbacus();
            Assert.AreEqual(0, Abacus.CompletedRewards);
            Assert.AreEqual(0, Abacus.DisplayedCount(Element.Earth));
            Assert.AreEqual(5, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf));
        }

        [UnityTest]
        public IEnumerator 回合事件降低门槛_飞牌后保留余数_恢复更新门槛()
        {
            var engine = _hud.Session.Engine;
            engine.AddBottleCharge(Element.Water, 4);
            yield return WaitForAbacus();

            Assert.AreEqual(1, engine.ApplyBottleCapacity(3));
            yield return null;
            yield return null;
            Assert.AreEqual(6, engine.Hand.Count);
            Assert.AreEqual(5, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf), "事件新牌在飞牌结束后显示");
            yield return WaitForAbacus();
            Assert.AreEqual(1, Abacus.CompletedRewards);
            Assert.AreEqual(1, Abacus.DisplayedCount(Element.Water));
            Assert.AreEqual(6, _hud.Ui.Cards.Count(card => card.gameObject.activeSelf));
            var counter = _hud.transform.Find("Composition/Controls/ElementRail/Frame/Element_Water/Progress").GetComponent<Text>();
            Assert.AreEqual("1/3", counter.text);

            engine.RestoreBottleCapacity();
            yield return WaitForAbacus();
            Assert.AreEqual("1/5", counter.text);
            Assert.AreEqual(1, Abacus.CompletedRewards, "恢复门槛不能再次飞牌");
            Assert.AreEqual(6, engine.Hand.Count);
        }

        [UnityTest]
        public IEnumerator 算盘_缺少对应手牌时不误选且暂停会阻止操作()
        {
            var rail = _hud.transform.Find("Composition/Controls/ElementRail");
            _hud.Session.Engine.ForceHandForTest(new[] { Element.Water });
            yield return null;
            rail.Find("Frame/Element_Water").GetComponent<Button>().onClick.Invoke();
            int selected = _hud.Session.SelectedCardId;
            rail.Find("Frame/Element_Fire").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.AreEqual(selected, _hud.Session.SelectedCardId);
            Assert.AreEqual((int)Element.Water, _hud.Ui.SelectedElementIndex);
            _hud.Ui.OpenSettings();
            yield return null;
            Assert.IsTrue(rail.GetComponentsInChildren<Button>().All(button => !button.IsInteractable()));
            _hud.Ui.CloseSettings();
        }

        [UnityTest]
        public IEnumerator 算盘_录制十点进位发牌过程()
        {
            StartSession(10);
            yield return null;
            var canvas = _hud.GetComponent<Canvas>();
            var camera = Camera.main;
            int originalMask = camera.cullingMask;
            var target = new RenderTexture(1280, 720, 24);
            var pixels = new Texture2D(760, 300, TextureFormat.RGB24, false);
            var active = RenderTexture.active;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "ElementAbacus", "ChargeFrames"));
            Directory.CreateDirectory(directory);
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = 1 << canvas.gameObject.layer;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return null;
                for (int frame = 0; frame < 26; frame++)
                {
                    Canvas.ForceUpdateCanvases();
                    _hud.GetComponent<BattleUiLayout>().Refresh();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 760, 300), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(directory, $"{frame:00}.png"), pixels.EncodeToPNG());
                    if (frame == 0) _hud.Session.Engine.AddBottleCharge(Element.Water, 10);
                    yield return new WaitForSecondsRealtime(.08f);
                }
                Assert.AreEqual(1, Abacus.CompletedRewards);
                Assert.AreEqual(0, Abacus.DisplayedCount(Element.Water));
            }
            finally
            {
                camera.targetTexture = null;
                camera.cullingMask = originalMask;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RenderTexture.active = active;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }

        [UnityTest]
        public IEnumerator 算盘_保存两种尺寸的界面预览()
        {
            StartSession(10);
            _hud.Session.Engine.AddBottleCharge(Element.Water, 5);
            _hud.Session.Engine.AddBottleCharge(Element.Fire, 3);
            _hud.Session.Engine.AddBottleCharge(Element.Wood, 2);
            yield return WaitForAbacus();
            var canvas = _hud.GetComponent<Canvas>();
            var camera = Camera.main;
            int originalMask = camera.cullingMask;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "ElementAbacus"));
            Directory.CreateDirectory(directory);
            foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
            {
                var target = new RenderTexture(size.x, size.y, 24);
                var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                var active = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.cullingMask = 1 << canvas.gameObject.layer;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1;
                    yield return null;
                    Canvas.ForceUpdateCanvases();
                    _hud.GetComponent<BattleUiLayout>().Refresh();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(directory, $"gameplay-{size.x}x{size.y}.png"), pixels.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    camera.cullingMask = originalMask;
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    RenderTexture.active = active;
                    target.Release();
                    Object.Destroy(target);
                    Object.Destroy(pixels);
                }
            }
        }
    }
}
