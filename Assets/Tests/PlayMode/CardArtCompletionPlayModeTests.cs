using System.Collections;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.Flow;
using TapGJ.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class CardArtCompletionPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
        }

        [UnityTest]
        public IEnumerator 同一槽位切换牌种_缺图恢复后自动刷新_保留选中效果()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var slot = ui.Cards[0];
            var engine = new GameEngine(new Rules { UseCompositeElements = true }, 31);
            var fusion = engine.DrawSpecialCard(CardKind.FusionCard);
            var decompose = engine.DrawSpecialCard(CardKind.DecomposeCard);
            var element = engine.DrawElementCard(Element.Water);
            var fallback = slot.transform.Find("Artwork/FallbackArtwork").gameObject;
            var art = slot.CardArt;
            var savedFusion = art.fusion;
            try
            {
                foreach (var card in new[] { fusion, decompose, element, fusion })
                {
                    slot.Bind(card);
                    Assert.AreSame(art.GetFace(card.Kind, card.Element), slot.CardFace.sprite);
                    Assert.IsTrue(slot.CardFace.gameObject.activeSelf);
                    Assert.IsFalse(fallback.activeSelf);
                    Assert.AreSame(slot.CardFace, slot.Button.targetGraphic);
                    Assert.AreEqual(card.Id, slot.CardId);
                }
                slot.SetSelected(true);
                Assert.IsTrue(slot.Selected);
                Assert.AreEqual(slot.SelectionLift, ((RectTransform)slot.transform.Find("Artwork")).anchoredPosition.y);
                art.fusion = null;
                yield return null;
                yield return null;
                Assert.IsTrue(fallback.activeSelf, "缺少秘术牌素材时保留可识别的文字占位");
                Assert.IsFalse(slot.CardFace.gameObject.activeSelf);
                Assert.AreEqual("融合", fallback.transform.Find("ElementLabel").GetComponent<Text>().text);
                Assert.AreSame(fallback.transform.Find("Frame").GetComponent<Image>(), slot.Button.targetGraphic);
                art.fusion = savedFusion;
                yield return null;
                yield return null;
                Assert.IsFalse(fallback.activeSelf);
                Assert.AreSame(savedFusion, slot.CardFace.sprite);
                Assert.IsTrue(slot.Selected, "刷新卡面不能丢失选中状态");
            }
            finally { art.fusion = savedFusion; }
        }

        [UnityTest]
        public IEnumerator 真实手牌与回合推进_插画正确切换_等待时不能点击()
        {
            Time.timeScale = 1;
            GameLaunchContext.Clear();
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var engine = new GameEngine(new Rules
            {
                UseCompositeElements = true, SpecialCardPool = SpecialCardMask.All,
                CompositeProducesOnCreation = false,
                CreepsPerElement = 0, MaxMoveSteps = 0, InitialHandSize = 3, HandLimit = 3,
                Goal = GoalSpec.Survive(500), RoundLimit = 500
            }, 31);
            engine.Start();
            engine.EndPreparationNow();
            engine.ClearHandForTest();
            engine.DrawSpecialCard(CardKind.FusionCard);
            engine.DrawSpecialCard(CardKind.DecomposeCard);
            engine.DrawElementCard(Element.Water);
            engine.PlaceCreep(Element.Metal, new Pos(1, 1));
            engine.PlaceCreep(Element.Water, new Pos(2, 1));
            session.Begin(engine, 31);
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var cards = ui.Cards.Where(card => card.gameObject.activeSelf).ToArray();
            CollectionAssert.AreEqual(new[] { "Fusion", "Decompose", "Water" },
                cards.Select(card => card.CardFace.sprite.name));
            Assert.IsTrue(cards[0].Button.IsInteractable(),
                $"phase={engine.Phase}, busy={session.Busy}, paused={session.Paused}, over={engine.IsOver}, settings={ui.SettingsOpen}");
            Assert.AreEqual(-1, session.SelectedCardId, "测试开局应没有选中的手牌");
            Assert.AreEqual(-1, ui.SelectedCardIndex, "测试开局的界面应没有选中的手牌");
            Assert.AreSame(session, ui.GetComponent<BattleGameplayHud>().Session);
            Assert.AreSame(engine, session.Engine, "测试引擎应仍属于当前对局");
            Assert.IsNotNull(session.Engine.FindCard(cards[0].CardId));
            ui.SelectCard(0);
            Assert.AreEqual(cards[0].CardId, session.SelectedCardId);
            Assert.IsTrue(cards[0].Selected);
            Assert.AreEqual(0, cards[0].transform.Find("Artwork/SelectedRing").GetComponent<CanvasGroup>().alpha);
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "special-cards", "CardArtCompletion/previews");
            var next = ui.transform.Find("Composition/Controls/NextRoundButton").GetComponent<Button>();
            Assert.AreEqual("EndPlayerPhase", ((Image)next.targetGraphic).sprite.name);
            next.onClick.Invoke();
            yield return WaitForPhase(session, GamePhase.Discard);
            Assert.AreEqual("EndDiscardPhase", ((Image)next.targetGraphic).sprite.name);
            session.AnimationsEnabled = true;
            next.onClick.Invoke();
            session.Paused = true;
            yield return null;
            yield return null;
            var image = (Image)next.targetGraphic;
            Assert.AreEqual("Advancing", image.sprite.name);
            Assert.IsTrue(image.gameObject.activeInHierarchy && image.enabled);
            Assert.IsFalse(next.IsInteractable());
            Assert.IsFalse(next.GetComponent<Image>().enabled);
            Assert.IsFalse(next.GetComponentInChildren<Text>(true).enabled, "图片题字不能重复叠加");
            Assert.AreEqual(new Vector2(300, 82), ((RectTransform)next.transform).sizeDelta);
            ui.gameObject.SetActive(false);
            ui.gameObject.SetActive(true);
            Assert.AreEqual("Advancing", ((Image)next.targetGraphic).sprite.name);
            yield return BattleActionButtonArtPlayModeTests.Capture(ui, "advancing", "CardArtCompletion/previews");
            var mesh = image.canvasRenderer.GetMesh();
            Assert.AreEqual(300, mesh.bounds.size.x, .1f);
            Assert.AreEqual(72, mesh.bounds.size.y, .1f);
            session.Paused = false;
            session.AnimationsEnabled = false;
            yield return WaitForPhase(session, GamePhase.PlayerAction);
            Assert.AreEqual("EndPlayerPhase", ((Image)next.targetGraphic).sprite.name);
            Assert.IsTrue(next.IsInteractable());
        }

        static IEnumerator WaitForPhase(GameSession session, GamePhase phase)
        {
            yield return null;
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Engine.Phase != phase || session.Busy || session.Engine.PendingStepCount > 0)
                   && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(phase, session.Engine.Phase);
            Assert.IsFalse(session.Busy);
            Assert.AreEqual(0, session.Engine.PendingStepCount);
        }
    }
}
