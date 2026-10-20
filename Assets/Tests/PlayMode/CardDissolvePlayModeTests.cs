using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class CardDissolvePlayModeTests
    {
        GameObject _root;

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            Time.timeScale = 1;
            if (_root != null) Object.Destroy(_root);
            foreach (var effect in Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None))
                Object.Destroy(effect.gameObject);
            yield return null;
        }

        static GameEngine Engine()
        {
            var engine = new GameEngine(new Rules
            {
                BoardWidth = 6, BoardHeight = 6, CreepsPerElement = 0,
                InitialHandSize = 0, MaxMoveSteps = 0,
            }, 7);
            engine.Start();
            engine.ClearBoard();
            engine.ClearHandForTest();
            engine.AddCardForTest(Element.Fire);
            engine.AddCardForTest(Element.Water);
            engine.FreezeHandLimitForTest();
            engine.EndPreparationNow();
            return engine;
        }

        [UnityTest]
        public IEnumerator 出牌失败保留手牌_成功只消散一次_重排不挪动副本_暂停仍回收()
        {
            var engine = Engine();
            _root = new GameObject("DissolveTestRoot");
            var handRoot = new GameObject("Hand");
            handRoot.transform.SetParent(_root.transform, false);
            var hand = handRoot.AddComponent<HandView>();
            hand.RowWidth = 1;
            hand.Setup(engine);
            int cardId = hand.Cards[0].CardId;
            var originalPosition = hand.Cards[0].transform.position;
            engine.PlaceCreep(Element.Fire, new Pos(1, 1));
            Assert.IsNull(engine.TryPlayCard(cardId, new Pos(1, 1)));
            hand.Sync();
            Assert.AreEqual(2, hand.CardObjectCount);
            Assert.AreEqual(0, Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None).Length);

            Assert.IsNotNull(engine.TryPlayCard(cardId, new Pos(2, 2)), engine.FailReason);
            hand.Sync();
            hand.Sync();
            Assert.AreEqual(1, hand.CardObjectCount);
            var effects = Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None);
            Assert.AreEqual(1, effects.Length, "重复同步不能重复播消散");
            Assert.Less(Vector3.Distance(originalPosition, effects[0].transform.position), .001f);
            Assert.IsTrue(effects[0].GetComponentsInChildren<Collider2D>().All(c => !c.enabled));
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration + .2f);
            Assert.AreEqual(0, Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None).Length);
        }

        [UnityTest]
        public IEnumerator 普通牌与火牌_卡面持续到共享消散中途_结束一起回收()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var source = Object.FindFirstObjectByType<BattleUiController>().Cards[0].transform.Find("Artwork").gameObject;
            var normal = CardDissolveEffect.Play(source, Color.white, false);
            var fire = CardDissolveEffect.Play(source, Color.white, true);
            Assert.IsNotNull(normal);
            Assert.IsNotNull(fire);
            Time.timeScale = 0;
            yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration * .5f);
            foreach (var effect in new[] { normal, fire })
            {
                Assert.IsTrue(effect != null, "普通牌和火牌不能早于棋盘过渡结束回收");
                var particles = new ParticleSystem.Particle[1];
                Assert.AreEqual(1, effect.CardParticles.GetParticles(particles),
                    "共享时间线中途必须仍有卡面，不能只剩尾部火星或空对象");
                Assert.AreEqual(1 - effect.Progress,
                    particles[0].remainingLifetime / particles[0].startLifetime, .05f,
                    "卡面实际播放进度应与棋盘共用的时间线一致");
            }
            yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration * .5f + .2f);
            Assert.IsTrue(normal == null);
            Assert.IsTrue(fire == null);
        }

        [UnityTest]
        public IEnumerator 弃牌同样消散_关闭动画时即时移除()
        {
            var engine = Engine();
            _root = new GameObject("Hand");
            var hand = _root.AddComponent<HandView>();
            hand.Setup(engine);
            engine.EndPlayerPhase();
            Assert.IsTrue(engine.TryDiscardCard(engine.Hand[0].Id), engine.FailReason);
            hand.Sync();
            Assert.AreEqual(1, Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None).Length);
            yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration + .2f);
            engine.ClearHandForTest();
            hand.Sync(false);
            Assert.AreEqual(0, hand.CardObjectCount);
            Assert.AreEqual(0, Object.FindObjectsByType<CardDissolveEffect>(FindObjectsSortMode.None).Length);
        }

        [UnityTest]
        public IEnumerator 战斗界面_弃牌和成功出牌共用消散_重复请求不再消耗()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<BattleUiController>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var card = canvas.GetComponentsInChildren<BattleUiCard>().First(c => c.CardFace.sprite != null);
            var allCards = canvas.GetComponentsInChildren<BattleUiCard>();
            int index = System.Array.IndexOf(allCards, card);
            var face = card.CardFace.sprite;
            controller.SelectCard(index);
            var selectedRect = (RectTransform)card.transform.Find("Artwork");
            var selectedPosition = selectedRect.TransformPoint(selectedRect.rect.center);
            controller.ConsumeCard(index);
            controller.ConsumeCard(index);
            Assert.IsFalse(card.gameObject.activeSelf);
            Assert.AreEqual(-1, controller.SelectedCardIndex);
            var effect = Object.FindFirstObjectByType<CardDissolveEffect>();
            Assert.IsNotNull(effect);
            Assert.Less(Vector3.Distance(selectedPosition, effect.transform.position), .001f, "选中抬起的位置应保留到消散开始");
            var output = effect.GetComponent<RawImage>();
            Assert.IsFalse(output.raycastTarget);
            Assert.IsNotNull(output.texture);
            var material = effect.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Assert.AreEqual("VFX/Pandavfx_v2.3", material.shader.name, "应使用原包 shader");
            Assert.AreEqual(face.texture, material.GetTexture("_MainTex"), "原卡面粒子的主贴图应替换为当前卡图");
            yield return new WaitForSecondsRealtime(.3f);
            Assert.Greater(effect.Progress, 0);
            yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration);
            Assert.IsNull(Object.FindFirstObjectByType<CardDissolveEffect>());

            controller.SelectCard(index == 0 ? 1 : 0);
            var discard = canvas.GetComponentsInChildren<Button>().First(b => b.name == "DiscardButton");
            int requests = 0;
            controller.DiscardRequested.AddListener(_ => requests++);
            discard.onClick.Invoke();
            discard.onClick.Invoke();
            Assert.AreEqual(1, requests);
            Assert.IsNotNull(Object.FindFirstObjectByType<CardDissolveEffect>());
        }

        [UnityTest]
        public IEnumerator 界面提前关闭_回收离屏粒子和渲染纹理()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var controller = Object.FindFirstObjectByType<BattleUiController>();
            controller.ConsumeCard(0);
            var effect = Object.FindFirstObjectByType<CardDissolveEffect>();
            var stage = effect.CardParticles.transform.parent.gameObject;
            var target = effect.OutputTexture;
            Object.FindFirstObjectByType<Canvas>().gameObject.SetActive(false);
            yield return null;
            yield return null;
            Assert.IsTrue(effect == null);
            Assert.IsTrue(stage == null, "界面关闭后不应残留独立粒子舞台");
            Assert.IsTrue(target == null, "界面关闭后应释放渲染纹理");
        }

        [UnityTest]
        public IEnumerator 原包粒子曲线和材质保持一致_同时换图不修改共享素材()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var profile = Resources.Load<CardDissolveProfile>("CardDissolve");
            var originalCard = profile.prefab.transform.Find("bofang/kapai_01").GetComponent<ParticleSystem>();
            var originalSparks = profile.prefab.transform.Find("bofang/lizi").GetComponent<ParticleSystem>();
            var originalMaterial = originalCard.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            var originalTexture = originalMaterial.GetTexture("_MainTex");
            var originalCurve = originalCard.customData.GetVector(ParticleSystemCustomData.Custom2, 2);
            var cards = Object.FindFirstObjectByType<Canvas>().GetComponentsInChildren<BattleUiCard>();
            var first = CardDissolveEffect.Play(cards[0].transform.Find("Artwork").gameObject, Color.red);
            var second = CardDissolveEffect.Play(cards[1].transform.Find("Artwork").gameObject, Color.blue);
            Assert.AreEqual(originalMaterial.shader, first.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader);
            Assert.AreEqual(originalCard.main.startLifetime.constantMax, first.CardParticles.main.startLifetime.constantMax);
            Assert.AreEqual(originalCard.emission.burstCount, first.CardParticles.emission.burstCount);
            Assert.AreEqual(originalSparks.emission.rateOverTime.constantMax, first.SparkParticles.emission.rateOverTime.constantMax);
            CollectionAssert.AreEqual(originalCurve.curve.keys, first.CardParticles.customData.GetVector(ParticleSystemCustomData.Custom2, 2).curve.keys);
            Assert.AreEqual(originalMaterial.GetColor("_DIssloveColor"), first.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetColor("_DIssloveColor"));
            Assert.AreEqual(originalTexture, originalMaterial.GetTexture("_MainTex"), "换图不能写回原包共享材质");
            Assert.AreNotEqual(first.OutputTexture, second.OutputTexture);
            Assert.AreNotEqual(first.CardParticles.transform.parent.position, second.CardParticles.transform.parent.position);
            Assert.AreEqual(RenderTextureFormat.ARGBHalf, ((RenderTexture)first.OutputTexture).format, "原包高亮颜色必须保留到泛光阶段");
            var firstVolume = first.CardParticles.transform.parent.GetComponentInChildren<PostProcessVolume>();
            var secondVolume = second.CardParticles.transform.parent.GetComponentInChildren<PostProcessVolume>();
            Assert.AreSame(profile.postProcessProfile, firstVolume.sharedProfile, "复用包内后处理配置");
            Assert.IsFalse(firstVolume.isGlobal, "泛光只能影响自己的离屏相机");
            Assert.Greater(Vector3.Distance(firstVolume.transform.position, secondVolume.transform.position), 50);
            Assert.AreEqual(cards[0].CardFace.sprite.texture, first.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetTexture("_MainTex"));
            Assert.AreEqual(cards[1].CardFace.sprite.texture, second.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetTexture("_MainTex"));
        }

        [UnityTest]
        public IEnumerator 替换主贴图_保留上下左右方向_溶解从下往上()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            var colors = new Color[32 * 32];
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    colors[y * 32 + x] = y >= 16 ? (x < 16 ? Color.red : Color.green) : Color.blue;
            texture.SetPixels(colors);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), Vector2.one * .5f);
            _root = new GameObject("CardFace", typeof(RectTransform), typeof(Image));
            _root.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
            ((RectTransform)_root.transform).sizeDelta = new Vector2(100, 100);
            _root.GetComponent<Image>().sprite = sprite;
            var effect = CardDissolveEffect.Play(_root, Color.white);
            var output = (RenderTexture)effect.OutputTexture;
            var previous = RenderTexture.active;
            var pixels = new Texture2D(output.width, output.height, TextureFormat.RGBA32, false);
            var gradient = new Texture2D(1, 32, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = output;
                pixels.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0);
                pixels.Apply();
                var top = pixels.GetPixel(output.width * 3 / 10, output.height * 7 / 10);
                var bottom = pixels.GetPixel(output.width * 3 / 10, output.height * 3 / 10);
                var right = pixels.GetPixel(output.width * 7 / 10, output.height * 7 / 10);
                Assert.Greater(top.r, top.b + .4f, "贴图上半部分应仍位于特效上方");
                Assert.Greater(bottom.b, bottom.r + .4f, "贴图下半部分应仍位于特效下方");
                Assert.Greater(right.g, right.r + .4f, "贴图右半部分不能被水平镜像");

                // 使用固定纵向梯度隔离原包噪声和火星，直接检查溶解方向。
                gradient.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < gradient.height; y++)
                    gradient.SetPixel(0, y, Color.white * (y / (float)(gradient.height - 1)));
                gradient.Apply();
                var material = effect.CardParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                material.SetTexture("_DissloveTex", gradient);
                material.SetVector("_DissloveTex_ST", new Vector4(1, 1, 0, 0));
                material.SetFloat("_IfDissolveOffsetC", 0);
                material.SetFloat("_CustomdataDis", 0);
                material.SetFloat("_DIssloveFactor", .5f);
                typeof(CardDissolveEffect).GetMethod("RenderEffect", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(effect, null);
                var opacityCamera = effect.CardParticles.transform.parent.GetComponentsInChildren<Camera>().Single(c => !c.allowHDR);
                RenderTexture.active = opacityCamera.targetTexture;
                pixels.ReadPixels(new Rect(0, 0, output.width, output.height), 0, 0);
                pixels.Apply();
                Assert.Less(pixels.GetPixel(output.width / 2, output.height * 3 / 10).a, .05f,
                    "溶解到中途时下半部应先消失");
                Assert.Greater(pixels.GetPixel(output.width / 2, output.height * 7 / 10).a, .8f,
                    "溶解到中途时上半部应仍可见");
            }
            finally
            {
                RenderTexture.active = previous;
                Object.Destroy(pixels);
                Object.Destroy(sprite);
                Object.Destroy(texture);
                Object.Destroy(gradient);
            }
        }

        [UnityTest]
        public IEnumerator 场景卡牌_消散起始保持原色_结束完全透明()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            Object.FindFirstObjectByType<Canvas>().gameObject.SetActive(false);
            _root = new GameObject("SpriteCard", typeof(SpriteRenderer));
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), Vector2.one * .5f, Texture2D.whiteTexture.width);
            var renderer = _root.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = ElementDefs.Tint(Element.Fire);
            renderer.sortingOrder = 1;
            var camera = Camera.main;
            camera.cullingMask = ~0;
            camera.orthographicSize = 1;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 128, 24);
            var previousTarget = RenderTexture.active;
            var pixels = new Texture2D(128, 128, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            try
            {
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                pixels.Apply();
                var before = pixels.GetPixel(64, 64);
                Assert.Greater(before.r, .2f);
                var effect = CardDissolveEffect.Play(_root, ElementDefs.Tint(Element.Fire));
                _root.SetActive(false);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                pixels.Apply();
                var after = pixels.GetPixel(64, 64);
                Assert.Less(Mathf.Abs(before.r - after.r) + Mathf.Abs(before.g - after.g) + Mathf.Abs(before.b - after.b), .08f,
                    "SpriteRenderer 的元素色不能在消散开始时变白或被重复相乘");
                yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration + .2f);
                Assert.IsTrue(effect == null);
                camera.Render();
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
                pixels.Apply();
                Assert.Less(pixels.GetPixel(64, 64).maxColorComponent, .02f);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
                Object.Destroy(sprite);
            }
        }

        [UnityTest]
        public IEnumerator 战斗卡面_真实渲染溶解过程并保存预览()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var camera = Camera.main;
            var target = new RenderTexture(1920, 1080, 24);
            var previousTarget = RenderTexture.active;
            var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            try
            {
                camera.cullingMask = ~0;
                camera.targetTexture = target;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                yield return null;
                yield return null;
                yield return null;
                Canvas.ForceUpdateCanvases();
                Object.FindFirstObjectByType<BattleUiLayout>().Refresh();
                var controller = Object.FindFirstObjectByType<BattleUiController>();
                controller.SetTimerRunning(false);
                var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "CardDissolve"));
                Directory.CreateDirectory(outputDirectory);
                int[] before = null;
                for (int frame = 0; frame < 4; frame++)
                {
                    if (frame == 1) controller.ConsumeCard(2);
                    if (frame > 0) yield return new WaitForSecondsRealtime(.3f);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(outputDirectory, "preview-" + frame + ".png"), pixels.EncodeToPNG());
                    var sampled = pixels.GetPixels32();
                    if (frame == 0) before = sampled.Select(c => (int)c.r + c.g + c.b).ToArray();
                    if (frame == 3)
                        Assert.Greater(sampled.Where((c, i) => Mathf.Abs(c.r + c.g + c.b - before[i]) > 30).Count(), 2000,
                            "卡牌溶解应改变实际渲染像素，不能只有属性在变化");
                }
                yield return new WaitForSecondsRealtime(CardDissolveEffect.Duration);
                Assert.IsNull(Object.FindFirstObjectByType<CardDissolveEffect>());
            }
            finally
            {
                camera.targetTexture = null;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                RenderTexture.active = previousTarget;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }
    }
}
