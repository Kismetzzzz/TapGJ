using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>实例化导入预制体的原粒子与材质，将离屏画面合成到卡牌原位置。</summary>
    public sealed class CardDissolveEffect : MonoBehaviour
    {
        public const float Duration = 1.5f;
        const float Padding = 1.3f;
        const int StageLayer = 31;
        static readonly HashSet<int> ActiveStages = new HashSet<int>();
        readonly List<Material> _materials = new List<Material>();
        GameObject _stage;
        Camera _camera, _opacityCamera;
        RenderTexture _output, _opacity, _snapshot;
        ParticleSystem _card, _sparks;
        float _elapsed;
        int _stageSlot = -1;
        public float Progress => Mathf.Clamp01(_elapsed / Duration);
        public ParticleSystem CardParticles => _card;
        public ParticleSystem SparkParticles => _sparks;
        public Texture OutputTexture => _output;

        public static CardDissolveEffect Play(GameObject source, Color tint)
        {
            if (!Application.isPlaying || source == null || !source.activeInHierarchy) return null;
            var profile = Resources.Load<CardDissolveProfile>("CardDissolve");
            var cardTemplate = profile != null && profile.prefab != null
                ? profile.prefab.transform.Find("bofang/kapai_01") : null;
            var sparkTemplate = profile != null && profile.prefab != null
                ? profile.prefab.transform.Find("bofang/lizi") : null;
            if (cardTemplate == null || sparkTemplate == null || profile.compositeMaterial == null)
            {
                Debug.LogWarning("原包卡牌消散资源未配置，保留即时移除。", source);
                return null;
            }

            var sourceRect = source.transform as RectTransform;
            GameObject output;
            Vector2 size;
            if (sourceRect != null)
            {
                size = sourceRect.rect.size;
                output = new GameObject("CardDissolve", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                var rect = (RectTransform)output.transform;
                rect.SetParent(source.transform.parent, false);
                rect.localScale = source.transform.localScale;
                rect.SetParent(source.transform.parent != null ? source.transform.parent.parent : null, true);
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = size * Padding;
                rect.SetPositionAndRotation(sourceRect.TransformPoint(sourceRect.rect.center), source.transform.rotation);
                output.GetComponent<RawImage>().raycastTarget = false;
            }
            else
            {
                var bounds = VisibleBounds(source);
                size = new Vector2(bounds.size.x, bounds.size.y);
                output = GameObject.CreatePrimitive(PrimitiveType.Quad);
                output.name = "CardDissolve";
                var collider = output.GetComponent<Collider>();
                collider.enabled = false;
                Destroy(collider);
                output.transform.SetParent(source.transform.parent != null ? source.transform.parent.parent : null, true);
                output.transform.SetPositionAndRotation(bounds.center, Quaternion.identity);
                output.transform.localScale = new Vector3(size.x * Padding, size.y * Padding, 1);
                var body = source.GetComponentInChildren<SpriteRenderer>();
                if (body != null)
                {
                    var renderer = output.GetComponent<MeshRenderer>();
                    renderer.sortingLayerID = body.sortingLayerID;
                    renderer.sortingOrder = body.sortingOrder;
                }
            }
            var effect = output.AddComponent<CardDissolveEffect>();
            try { effect.Configure(source, profile, cardTemplate.gameObject, sparkTemplate.gameObject, size, tint); }
            catch (System.Exception error)
            {
                effect.enabled = false;
                Destroy(output);
                Debug.LogWarning("原包特效播放失败，卡牌仍按正常逻辑移除：" + error.Message, source);
                return null;
            }
            return effect;
        }

        void Configure(GameObject source, CardDissolveProfile profile, GameObject cardTemplate,
            GameObject sparkTemplate, Vector2 size, Color tint)
        {
            _stage = new GameObject("CardDissolveStage");
            // 离屏粒子远离玩法相机；每张牌独立占位，避免同时播放时互相进入画面。
            _stageSlot = 0;
            while (ActiveStages.Contains(_stageSlot)) _stageSlot++;
            ActiveStages.Add(_stageSlot);
            _stage.transform.position = new Vector3(1000 + _stageSlot * 100, 1000, 1000);
            _card = Instantiate(cardTemplate, _stage.transform, false).GetComponent<ParticleSystem>();
            _sparks = Instantiate(sparkTemplate, _stage.transform, false).GetComponent<ParticleSystem>();
            _card.gameObject.layer = _sparks.gameObject.layer = StageLayer;
            _card.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            _sparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

            Image cardFace = null;
            foreach (var image in source.GetComponentsInChildren<Image>())
                if (image.name == "CardFace" && image.sprite != null) { cardFace = image; break; }
            Texture texture;
            Vector4 uv = new Vector4(0, 0, 1, 1);
            Color faceColor = Color.white;
            if (cardFace != null)
            {
                texture = cardFace.sprite.texture;
                uv = DataUtility.GetOuterUV(cardFace.sprite);
                faceColor = cardFace.color;
            }
            else texture = CaptureCard(source, size);

            var cardRenderer = _card.GetComponent<ParticleSystemRenderer>();
            var cardMaterial = CopyMaterial(cardRenderer.sharedMaterial);
            cardMaterial.SetTexture("_MainTex", texture);
            // 原粒子卡面与 Canvas 的上下方向相反，替换纹理时一起转换 UV。
            cardMaterial.SetVector("_MainTex_ST", new Vector4(uv.z - uv.x, uv.y - uv.w, uv.x, uv.w));
            cardMaterial.SetColor("_MainColor", faceColor);
            cardRenderer.sharedMaterial = cardMaterial;
            var sparkRenderer = _sparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.sharedMaterial = CopyMaterial(sparkRenderer.sharedMaterial);
            if (profile.tintByElement)
            {
                var edge = cardMaterial.GetColor("_DIssloveColor");
                float intensity = Mathf.Max(edge.r, Mathf.Max(edge.g, edge.b));
                cardMaterial.SetColor("_DIssloveColor", new Color(tint.r * intensity, tint.g * intensity, tint.b * intensity, edge.a));
                var colors = _sparks.colorOverLifetime;
                var original = colors.color.gradient;
                var keys = original.colorKeys;
                for (int i = 0; i < keys.Length; i++) keys[i].color = tint * keys[i].color.maxColorComponent;
                var gradient = new Gradient();
                gradient.SetKeys(keys, original.alphaKeys);
                colors.color = gradient;
            }

            // 只适配卡面宽高；粒子数量、寿命、轨迹和 CustomData 溶解曲线保持原包配置。
            float originalWidth = _card.main.startSizeX.constantMax;
            float originalHeight = _card.main.startSizeY.constantMax;
            float width = originalHeight * size.x / Mathf.Max(size.y, .001f);
            float widthScale = width / originalWidth;
            var scale = _card.transform.localScale;
            scale.x *= widthScale;
            _card.transform.localScale = scale;
            scale = _sparks.transform.localScale;
            scale.x *= widthScale;
            _sparks.transform.localScale = scale;
            int height = Mathf.Clamp(Mathf.CeilToInt(size.y * Padding), 256, 768);
            int outputWidth = Mathf.Max(16, Mathf.CeilToInt(height * size.x / Mathf.Max(size.y, .001f)));
            _output = new RenderTexture(outputWidth, height, 24, RenderTextureFormat.ARGBHalf)
            { name = "CardDissolveOutput", antiAliasing = 1 };
            _output.Create();
            _opacity = new RenderTexture(outputWidth, height, 24, RenderTextureFormat.ARGB32)
            { name = "CardDissolveOpacity", antiAliasing = 1 };
            _opacity.Create();
            _camera = MakeCamera(_stage.transform, new Vector3(0, 20, 0), Quaternion.Euler(90, 0, 0),
                originalHeight * Padding * .5f, _output);
            _camera.allowHDR = true;
            _opacityCamera = MakeCamera(_stage.transform, _camera.transform.localPosition, _camera.transform.localRotation,
                _camera.orthographicSize, _opacity);
            if (profile.postProcessProfile != null && profile.postProcessResources != null)
            {
                _camera.gameObject.layer = StageLayer;
                var layer = _camera.gameObject.AddComponent<PostProcessLayer>();
                layer.Init(profile.postProcessResources);
                layer.volumeLayer = 1 << StageLayer;
                layer.volumeTrigger = _camera.transform;
                layer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                // 相机位于自己的局部体积内；同时消散的卡牌与玩法相机不会互相套后处理。
                var region = _camera.gameObject.AddComponent<BoxCollider>();
                region.isTrigger = true;
                region.size = Vector3.one * 2;
                var volume = _camera.gameObject.AddComponent<PostProcessVolume>();
                volume.isGlobal = false;
                volume.sharedProfile = profile.postProcessProfile;
            }
            var composite = CopyMaterial(profile.compositeMaterial);
            composite.mainTexture = _output;
            composite.SetTexture("_CardOpacity", _opacity);
            var rawImage = GetComponent<RawImage>();
            if (rawImage != null)
            {
                composite.SetFloat("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                rawImage.material = composite;
                rawImage.texture = _output;
            }
            else GetComponent<MeshRenderer>().sharedMaterial = composite;
            var main = _card.main;
            main.useUnscaledTime = true;
            main = _sparks.main;
            main.useUnscaledTime = true;
            _card.Simulate(.0001f, false, true, false);
            _sparks.Simulate(.0001f, false, true, false);
            _card.Play(false);
            _sparks.Play(false);
            RenderEffect();
        }

        Texture CaptureCard(GameObject source, Vector2 size)
        {
            var captureRoot = new GameObject("CardSnapshot");
            captureRoot.transform.position = _stage.transform.position + Vector3.up * 100;
            var copy = Instantiate(source, captureRoot.transform, false);
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is Graphic)) script.enabled = false;
            foreach (var child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = StageLayer;
            var rect = copy.transform as RectTransform;
            Bounds bounds;
            if (rect != null)
            {
                var canvas = captureRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                copy.transform.localScale = Vector3.one;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = size;
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                Canvas.ForceUpdateCanvases();
                bounds = new Bounds(captureRoot.transform.position, new Vector3(size.x, size.y, 0));
            }
            else
            {
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = source.transform.lossyScale;
                bounds = VisibleBounds(copy);
            }
            int height = 512;
            int width = Mathf.Max(16, Mathf.CeilToInt(height * bounds.size.x / Mathf.Max(bounds.size.y, .001f)));
            _snapshot = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _snapshot.Create();
            var camera = MakeCamera(captureRoot.transform, bounds.center - captureRoot.transform.position + Vector3.back * 10,
                Quaternion.identity, bounds.extents.y, _snapshot);
            camera.Render();
            Destroy(captureRoot);
            return _snapshot;
        }

        static Bounds VisibleBounds(GameObject source)
        {
            Bounds bounds = default;
            bool found = false;
            foreach (var renderer in source.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || (renderer is SpriteRenderer sprite && sprite.color.a <= .001f)) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found ? bounds : new Bounds(source.transform.position, Vector3.one);
        }

        static Camera MakeCamera(Transform parent, Vector3 position, Quaternion rotation, float size, RenderTexture target)
        {
            var camera = new GameObject("CardEffectCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(parent, false);
            camera.transform.localPosition = position;
            camera.transform.localRotation = rotation;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(.001f, size);
            camera.aspect = target.width / (float)target.height;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 50;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << StageLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.targetTexture = target;
            return camera;
        }

        Material CopyMaterial(Material template)
        {
            var material = new Material(template);
            _materials.Add(material);
            return material;
        }

        void LateUpdate()
        {
            _elapsed += Time.unscaledDeltaTime;
            RenderEffect();
            if (_elapsed > .1f && !_card.IsAlive(false) && !_sparks.IsAlive(false)) Destroy(gameObject);
        }

        void RenderEffect()
        {
            _camera.targetTexture = _output;
            _camera.Render();
            // 火星使用原包的加法混合，只贡献颜色；界面遮挡量由卡面单独决定。
            var sparks = _sparks.GetComponent<ParticleSystemRenderer>();
            bool wasEnabled = sparks.enabled;
            sparks.enabled = false;
            try
            {
                _opacityCamera.Render();
            }
            finally
            {
                sparks.enabled = wasEnabled;
                _camera.targetTexture = _output;
            }
        }

        void OnDestroy()
        {
            if (_stageSlot >= 0) ActiveStages.Remove(_stageSlot);
            if (_stage != null) Destroy(_stage);
            foreach (var material in _materials) if (material != null) Destroy(material);
            if (_output != null) { _output.Release(); Destroy(_output); }
            if (_opacity != null) { _opacity.Release(); Destroy(_opacity); }
            if (_snapshot != null) { _snapshot.Release(); Destroy(_snapshot); }
        }

        void OnDisable()
        {
            if (Application.isPlaying && _stage != null) Destroy(gameObject);
        }
    }
}

