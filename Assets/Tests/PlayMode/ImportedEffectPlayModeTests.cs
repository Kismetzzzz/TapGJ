using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class ImportedEffectPlayModeTests
    {
        GameObject _root;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = 1;
            if (_root != null) Object.Destroy(_root);
            foreach (var effect in Object.FindObjectsByType<ImportedParticleEffect>(FindObjectsSortMode.None))
                Object.Destroy(effect.gameObject);
            yield return null;
            yield return null;
        }

        [TestCase(CompositeKind.Ember, "bofang_muhuo")]
        [TestCase(CompositeKind.Lava, "bofang_huotu")]
        [TestCase(CompositeKind.Ore, "bofang_tujin")]
        [TestCase(CompositeKind.ColdSteel, "bofang_jinshui")]
        [TestCase(CompositeKind.SpiritSpring, "bofang_shuimu")]
        public void 五种复合元素绑定对应原包粒子(CompositeKind kind, string group)
        {
            var profile = Resources.Load<ImportedEffectProfile>("ImportedEffects");
            Assert.IsNotNull(profile);
            Assert.AreEqual(group, ImportedEffectProfile.GroupFor(kind));
            foreach (var prefab in new[] { profile.burst, profile.range })
            {
                var template = prefab.transform.Find(group);
                Assert.IsNotNull(template);
                Assert.Greater(template.GetComponentsInChildren<ParticleSystem>(true).Length, 1);
                Assert.AreEqual(0, template.GetComponentsInChildren<MonoBehaviour>(true).Length,
                    "运行节点中不能夹带演示脚本或录制轨道");
            }
            Assert.IsNotNull(profile.selection.transform.Find("bofang/kapai_02"));
            var dissolve = Resources.Load<CardDissolveProfile>("CardDissolve");
            Assert.IsNotNull(dissolve.normalPrefab);
            Assert.AreNotEqual(dissolve.prefab, dissolve.normalPrefab);
        }

        [UnityTest]
        public IEnumerator 选中只播一次_取消和关闭时回收且不阻挡点击()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            var card = Object.FindFirstObjectByType<BattleUiController>().Cards[0];
            card.SetSelected(true);
            var effect = card.GetComponentInChildren<ImportedParticleEffect>();
            Assert.IsNotNull(effect);
            Assert.IsFalse(effect.IsRange);
            Assert.IsFalse(effect.GetComponent<RawImage>().raycastTarget);
            var sourceRect = (RectTransform)card.transform.Find("Artwork");
            Assert.AreEqual(0, sourceRect.Find("SelectedRing").GetComponent<CanvasGroup>().alpha,
                "已有素材流光时不能再叠加旧矩形选中框");
            Assert.AreEqual(card.SelectionLift, sourceRect.anchoredPosition.y);
            var effectRect = (RectTransform)effect.transform;
            Assert.Less(Vector3.Distance(sourceRect.TransformPoint(sourceRect.rect.center),
                effectRect.TransformPoint(effectRect.rect.center)), .001f, "左上角轴心的卡面与边光也必须同心");
            Assert.AreEqual(1, effect.ParticleRoot.GetComponentsInChildren<ParticleSystem>().Length);
            Assert.AreEqual(0, effect.GetComponentsInChildren<Camera>().Length);
            card.SetSelected(true);
            Assert.AreEqual(1, card.GetComponentsInChildren<ImportedParticleEffect>().Length);
            var stage = effect.ParticleRoot.parent.gameObject;
            var texture = effect.OutputTexture;
            var material = effect.ParticleRoot.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            var mask = material.GetTexture("_MainTex");
            Assert.IsNotNull(mask, "选中边框必须绑定有效遮罩");
            yield return new WaitForSecondsRealtime(.3f);
            AssertRendered(effect, "selection");
            card.SetSelected(false);
            Assert.AreEqual(0, sourceRect.anchoredPosition.y);
            Assert.IsFalse(effect.gameObject.activeSelf);
            yield return null;
            yield return null;
            Assert.IsTrue(stage == null);
            Assert.IsTrue(texture == null);
            Assert.IsTrue(material == null);
            Assert.IsTrue(mask == null);
            card.SetSelected(true);
            card.gameObject.SetActive(false);
            yield return null;
            yield return null;
            Assert.IsFalse(card.Selected);
            Assert.IsNull(card.GetComponentInChildren<ImportedParticleEffect>(true));
        }

        [UnityTest]
        public IEnumerator 选中光效_中心透明且不在卡面留下矩形色带()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Busy || session.Engine.Phase != GamePhase.PlayerAction) && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(GamePhase.PlayerAction, session.Engine.Phase);
            session.StopAllCoroutines();
            yield return null;
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            var card = ui.Cards[0];
            Assert.IsTrue(session.SelectHandCard(card.CardId));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.IsTrue(card.Button.IsInteractable(), "画面对照应处于正常可操作状态");
            var effect = card.GetComponentInChildren<ImportedParticleEffect>();
            var image = effect.GetComponent<RawImage>();
            var canvas = ui.GetComponent<Canvas>();
            var camera = Camera.main;
            var previousTarget = camera.targetTexture;
            int previousMask = camera.cullingMask;
            var previousActive = RenderTexture.active;
            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                camera.cullingMask = 1 << canvas.gameObject.layer;
                foreach (var size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720) })
                {
                    var target = new RenderTexture(size.x, size.y, 24);
                    var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                    var output = (RenderTexture)effect.OutputTexture;
                    var glow = new Texture2D(output.width, output.height, TextureFormat.RGBAFloat, false);
                    try
                    {
                        camera.targetTexture = target;
                        Canvas.ForceUpdateCanvases();
                        ui.GetComponent<BattleUiLayout>().Refresh();
                        foreach (float time in new[] { .05f, .3f, .6f })
                        {
                            foreach (var particle in effect.ParticleRoot.GetComponentsInChildren<ParticleSystem>())
                            {
                                particle.Simulate(time, false, true, false);
                                particle.Pause(false);
                            }
                            typeof(ImportedParticleEffect).GetMethod("RenderEffect", System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Instance).Invoke(effect, null);
                            RenderTexture.active = output;
                            glow.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0);
                            glow.Apply();
                            for (int y = 3; y <= 7; y++)
                                for (int x = 3; x <= 7; x++)
                                    Assert.Less(glow.GetPixel(Mathf.RoundToInt(output.width * x / 10f),
                                        Mathf.RoundToInt(output.height * y / 10f)).maxColorComponent, .03f,
                                        "选中流光内部必须透明，缺失贴图不能变成整块亮色矩形");
                            Assert.Greater(glow.GetPixels().Count(c => c.r + c.g + c.b > .1f), 50,
                                "边框仍须实际发光，不能通过隐藏特效来消除色块");

                            image.enabled = false;
                            camera.Render();
                            RenderTexture.active = target;
                            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                            pixels.Apply();
                            var without = pixels.GetPixels32();
                            image.enabled = true;
                            camera.Render();
                            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                            pixels.Apply();
                            var with = pixels.GetPixels32();
                            Directory.CreateDirectory("Artifacts/BoardEffects");
                            File.WriteAllBytes($"Artifacts/BoardEffects/selection-{size.x}x{size.y}-{time:F2}.png", pixels.EncodeToPNG());
                            var face = card.CardFace.rectTransform;
                            foreach (float u in new[] { .25f, .5f, .75f })
                                foreach (float v in new[] { .25f, .5f, .75f })
                                {
                                    var point = RectTransformUtility.WorldToScreenPoint(camera, face.TransformPoint(
                                        new Vector2(Mathf.Lerp(face.rect.xMin, face.rect.xMax, u),
                                            Mathf.Lerp(face.rect.yMin, face.rect.yMax, v))));
                                    int index = Mathf.RoundToInt(point.y) * size.x + Mathf.RoundToInt(point.x);
                                    int difference = Mathf.Abs(with[index].r - without[index].r) +
                                        Mathf.Abs(with[index].g - without[index].g) + Mathf.Abs(with[index].b - without[index].b);
                                    Assert.LessOrEqual(difference, 6, "流光不能让卡面内部变灰或出现竖条");
                                }
                            Assert.Greater(with.Where((c, i) => Mathf.Abs(c.r - without[i].r) +
                                Mathf.Abs(c.g - without[i].g) + Mathf.Abs(c.b - without[i].b) > 12).Count(), 50,
                                "边框光效必须出现在最终界面");
                        }
                    }
                    finally
                    {
                        camera.targetTexture = previousTarget;
                        RenderTexture.active = previousActive;
                        target.Release();
                        Object.Destroy(target);
                        Object.Destroy(pixels);
                        Object.Destroy(glow);
                    }
                }
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.cullingMask = previousMask;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RenderTexture.active = previousActive;
                if (session.SelectedCardId == card.CardId) session.SelectHandCard(card.CardId);
                card.SetSelected(false);
            }
        }

        static GameEngine Engine()
        {
            var engine = new GameEngine(new Rules
            {
                BoardWidth = 8, BoardHeight = 8, CreepsPerElement = 0, MaxMoveSteps = 0,
                UseCompositeElements = true, AllowMultipleComposites = true,
                CompositeProducesOnCreation = false,
                Goal = new GoalSpec { Kind = GoalKind.SurviveRounds, Rounds = 20 }
            }, 9251);
            engine.Start();
            engine.ClearBoard();
            return engine;
        }

        BoardView Board(GameEngine engine)
        {
            _root = new GameObject("EffectBoard");
            var board = _root.AddComponent<BoardView>();
            board.Setup(engine);
            board.Sync();
            return board;
        }

        [UnityTest]
        public IEnumerator 范围随复合元素存续_同步不重复创建_分解和重开后清理()
        {
            var engine = Engine();
            engine.PlaceComposite(CompositeKind.Lava, new Pos(3, 3));
            var board = Board(engine);
            board.Sync();
            board.Sync();
            var effect = board.GetComponentsInChildren<ImportedParticleEffect>().Single();
            Assert.IsTrue(effect.IsRange);
            Assert.AreEqual("bofang_huotu(Clone)", effect.ParticleRoot.name);
            Assert.Less(Vector3.Distance(board.CellLocalPos(new Pos(3, 3)),
                board.transform.InverseTransformPoint(effect.transform.position) + Vector3.forward * .08f), .001f);
            Assert.AreEqual((2 * engine.Rules.CompositeRange + 1) * (board.CellSize + board.CellGap),
                effect.transform.localScale.x, .001f, "范围使用规则的半径和实际格距");
            var camera = effect.ParticleRoot.parent.GetComponentsInChildren<Camera>().Single(c => c.allowHDR);
            var vertices = effect.GetComponent<MeshFilter>().sharedMesh.vertices;
            // 素材的地面边长为 3 单位；倾斜相机中的四角投回棋盘后应仍是正方形。
            foreach (var corner in new[] { new Vector3(-1.5f, 0, -1.5f), new Vector3(1.5f, 0, -1.5f),
                new Vector3(1.5f, 0, 1.5f), new Vector3(-1.5f, 0, 1.5f) })
            {
                var uv = camera.WorldToViewportPoint(effect.ParticleRoot.parent.position + corner);
                var point = Vector3.LerpUnclamped(Vector3.LerpUnclamped(vertices[0], vertices[3], uv.x),
                    Vector3.LerpUnclamped(vertices[1], vertices[2], uv.x), uv.y);
                Assert.Less(Vector2.Distance(new Vector2(corner.x, corner.z) / 3, point), .001f,
                    "原包的观察角度不能让效果覆盖边界斜穿逻辑格线");
            }
            yield return new WaitForSecondsRealtime(.3f);
            AssertRendered(effect, "range-lava");
            var stage = effect.ParticleRoot.parent.gameObject;
            var texture = effect.OutputTexture;
            var record = engine.DecomposeComposite(engine.CompositeAt(new Pos(3, 3)), null, false);
            yield return board.PlayStep(record, .1f, .3f);
            board.Sync();
            Assert.AreEqual(0, board.GetComponentsInChildren<ImportedParticleEffect>().Count(e => e.IsRange));
            Assert.IsTrue(stage == null);
            Assert.IsTrue(texture == null);
            Assert.AreEqual(1, board.GetComponentsInChildren<ImportedParticleEffect>().Count(e => !e.IsRange),
                "与卡牌同时播放的分解分支也应触发对应爆点");
            board.Setup(Engine());
            yield return null;
            yield return null;
            Assert.AreEqual(0, board.GetComponentsInChildren<ImportedParticleEffect>().Length);
        }

        [UnityTest]
        public IEnumerator 五组爆点和范围真实渲染_保留原材质且独立播放()
        {
            var engine = Engine();
            var board = Board(engine);
            var effects = CompositeDefs.All.Select((kind, i) => ImportedParticleEffect.PlayComposite(
                board.transform, board.CellLocalPos(new Pos(i, 2)), kind, 2, false)).ToArray();
            foreach (var effect in effects)
            {
                Assert.IsNotNull(effect);
                foreach (var particle in effect.ParticleRoot.GetComponentsInChildren<ParticleSystem>())
                {
                    particle.Simulate(.04f, false, true, false);
                    particle.Pause(false);
                }
                typeof(ImportedParticleEffect).GetMethod("RenderEffect", System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance).Invoke(effect, null);
                AssertRendered(effect, "burst-" + effect.ParticleRoot.name);
                var group = Resources.Load<ImportedEffectProfile>("ImportedEffects").burst.transform.Find(
                    effect.ParticleRoot.name.Replace("(Clone)", ""));
                CollectionAssert.AreEqual(group.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial),
                    effect.ParticleRoot.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial),
                    "适配时不改写共享素材材质");
            }
            foreach (var effect in effects) Object.Destroy(effect.gameObject);
            foreach (var kind in CompositeDefs.All)
            {
                var range = ImportedParticleEffect.PlayComposite(board.transform, Vector3.zero, kind, 3, true);
                yield return new WaitForSecondsRealtime(.2f);
                AssertRendered(range, "range-" + kind);
                Object.Destroy(range.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator 普通步骤播放爆点_快进不播爆点_停用时回收范围()
        {
            var engine = Engine();
            engine.PlaceComposite(CompositeKind.Ore, new Pos(2, 2));
            var board = Board(engine);
            var composite = engine.CompositeAt(new Pos(2, 2));
            var step = new StepRecord();
            step.Events.Add(new GameEvent { Type = GameEventType.CompositeSpawn, Uid = composite.Uid, To = composite.Pos });
            yield return board.PlayStep(step, 0);
            Assert.AreEqual(0, board.GetComponentsInChildren<ImportedParticleEffect>().Count(e => !e.IsRange));
            yield return board.PlayStep(step, .1f);
            var burst = board.GetComponentsInChildren<ImportedParticleEffect>().Single(e => !e.IsRange);
            Assert.AreEqual("bofang_tujin(Clone)", burst.ParticleRoot.name);
            var stage = board.GetComponentsInChildren<ImportedParticleEffect>().Single(e => e.IsRange).ParticleRoot.parent.gameObject;
            board.gameObject.SetActive(false);
            yield return null;
            yield return null;
            Assert.IsTrue(stage == null);
            Assert.AreEqual(0, board.GetComponentsInChildren<ImportedParticleEffect>(true).Length);
            board.gameObject.SetActive(true);
            Assert.AreEqual(1, board.GetComponentsInChildren<ImportedParticleEffect>().Count(e => e.IsRange));
        }

        [UnityTest]
        public IEnumerator 原型手牌也接入选中_普通与火牌选择不同消散素材()
        {
            var engine = Engine();
            engine.ClearHandForTest();
            engine.AddCardForTest(Element.Metal);
            _root = new GameObject("EffectHand");
            var hand = _root.AddComponent<HandView>();
            hand.Setup(engine);
            hand.RefreshSelection(engine.Hand[0].Id);
            hand.RefreshSelection(engine.Hand[0].Id);
            Assert.AreEqual(1, hand.GetComponentsInChildren<ImportedParticleEffect>().Length);
            hand.RefreshSelection(-1);
            yield return null;
            yield return null;
            Assert.AreEqual(0, hand.GetComponentsInChildren<ImportedParticleEffect>().Length);
            var profile = Resources.Load<CardDissolveProfile>("CardDissolve");
            var normal = CardDissolveEffect.Play(hand.Cards[0].gameObject, Color.white, false);
            var fire = CardDissolveEffect.Play(hand.Cards[0].gameObject, Color.white, true);
            Assert.AreEqual(profile.normalPrefab.transform.Find("bofang/kapai_01").GetComponent<ParticleSystem>().main.startLifetime.constantMax,
                normal.CardParticles.main.startLifetime.constantMax);
            Assert.AreEqual(profile.prefab.transform.Find("bofang/kapai_01").GetComponent<ParticleSystem>().main.startLifetime.constantMax,
                fire.CardParticles.main.startLifetime.constantMax);
            Object.Destroy(normal.gameObject);
            Object.Destroy(fire.gameObject);
        }

        [UnityTest]
        public IEnumerator 战斗画面保存背景与特效预览()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/Game.unity");
            var session = Object.FindFirstObjectByType<GameSession>();
            session.AnimationsEnabled = false;
            float until = Time.realtimeSinceStartup + 15;
            while ((session.Busy || session.Engine.Phase != GamePhase.PlayerAction) && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.AreEqual(GamePhase.PlayerAction, session.Engine.Phase);
            session.StopAllCoroutines();
            session.Engine.ClearBoard();
            foreach (var kind in CompositeDefs.All)
                session.Engine.PlaceComposite(kind, new Pos((int)kind + 1, (int)kind + 1));
            session.Board.SnapAll();
            var ui = Object.FindFirstObjectByType<BattleUiController>();
            Assert.IsTrue(session.SelectHandCard(ui.Cards[0].CardId));
            yield return new WaitForSecondsRealtime(.25f);
            var canvas = ui.GetComponent<Canvas>();
            var camera = Camera.main;
            var target = new RenderTexture(1920, 1080, 24);
            var previous = RenderTexture.active;
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            var cameraTarget = camera.targetTexture;
            int cameraMask = camera.cullingMask;
            try
            {
                camera.targetTexture = target;
                camera.cullingMask = 1 << canvas.gameObject.layer;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                Canvas.ForceUpdateCanvases();
                ui.GetComponent<BattleUiLayout>().Refresh();
                yield return null;
                Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c => c.name == "BoardRenderCamera").Render();
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("Artifacts/BoardEffects");
                File.WriteAllBytes("Artifacts/BoardEffects/gameplay-1920x1080.png", pixels.EncodeToPNG());
                Assert.AreEqual(5, session.Board.GetComponentsInChildren<ImportedParticleEffect>().Count(e => e.IsRange));
                var viewport = Object.FindFirstObjectByType<BattleBoardViewport>();
                Assert.AreEqual(1, viewport.material.GetFloat("_UseRenderedAlpha"),
                    "棋盘必须保留加法颜色，否则透明粒子会在界面合成时消失");
                var withEffects = pixels.GetPixels32();
                foreach (var effect in session.Board.GetComponentsInChildren<ImportedParticleEffect>())
                    effect.gameObject.SetActive(false);
                Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(c => c.name == "BoardRenderCamera").Render();
                camera.Render();
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                var withoutEffects = pixels.GetPixels32();
                Assert.Greater(withEffects.Where((c, i) => Mathf.Abs(c.r - withoutEffects[i].r) +
                    Mathf.Abs(c.g - withoutEffects[i].g) + Mathf.Abs(c.b - withoutEffects[i].b) > 20).Count(), 500,
                    "范围效果必须出现在最终界面画面中，不能只在离屏纹理里可见");
            }
            finally
            {
                camera.targetTexture = cameraTarget;
                camera.cullingMask = cameraMask;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RenderTexture.active = previous;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }

        static void AssertRendered(ImportedParticleEffect effect, string name)
        {
            Assert.IsNotNull(effect);
            TestContext.WriteLine(name + " / " + string.Join("; ", effect.ParticleRoot.GetComponentsInChildren<ParticleSystem>()
                .Select(p => p.name + " active=" + p.gameObject.activeInHierarchy + " count=" + p.particleCount +
                    " size=" + p.GetComponent<Renderer>().bounds.size +
                    " offset=" + (p.GetComponent<Renderer>().bounds.center - effect.ParticleRoot.parent.position))));
            var target = (RenderTexture)effect.OutputTexture;
            var previous = RenderTexture.active;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                Directory.CreateDirectory("Artifacts/BoardEffects");
                File.WriteAllBytes("Artifacts/BoardEffects/" + name + ".png", pixels.EncodeToPNG());
                Assert.Greater(pixels.GetPixels().Count(c => c.maxColorComponent > .02f && (c.r + c.g + c.b) > .02f), 24,
                    name + " 必须真正渲染粒子，不能只有空节点");
            }
            finally { RenderTexture.active = previous; Object.Destroy(pixels); }
        }


    }
}
