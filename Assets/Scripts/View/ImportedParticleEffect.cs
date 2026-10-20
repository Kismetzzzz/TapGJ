using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>只实例化素材的粒子节点；透明离屏画面可用于界面和已校准的棋盘。</summary>
    public sealed class ImportedParticleEffect : MonoBehaviour
    {
        const int StageLayer = 31;
        const float SelectionPadding = 1.3f;
        static readonly HashSet<int> Slots = new HashSet<int>();
        readonly List<Renderer> _additive = new List<Renderer>();
        Material _material;
        Material _selectionMaterial;
        Texture2D _selectionMask;
        Mesh _rangeMesh;
        GameObject _stage;
        Camera _camera, _opacityCamera;
        RenderTexture _output, _opacity;
        ParticleSystem[] _particles;
        int _slot = -1;
        bool _loop;
        float _elapsed;
        public bool IsRange => _loop;
        public Texture OutputTexture => _output;
        public Transform ParticleRoot { get; private set; }

        public static ImportedParticleEffect PlaySelection(GameObject source, Vector2 size)
        {
            if (!Application.isPlaying || source == null || !source.activeInHierarchy) return null;
            var profile = Resources.Load<ImportedEffectProfile>("ImportedEffects");
            // kapai_01 是演示卡面；kapai_02 才是叠加在真实卡面上的选中流光。
            var template = profile != null && profile.selection != null
                ? profile.selection.transform.Find("bofang/kapai_02") : null;
            if (template == null) return null;
            var original = template.GetComponent<ParticleSystem>();
            if (original == null) return null;
            var effect = Create(source.transform, size * SelectionPadding, template, false,
                original.main.startSizeY.constantMax * SelectionPadding * .5f, true);
            if (effect != null)
            {
                float width = original.main.startSizeY.constantMax * size.x / Mathf.Max(.001f, size.y);
                var scale = effect.ParticleRoot.localScale;
                scale.x *= width / Mathf.Max(.001f, original.main.startSizeX.constantMax);
                effect.ParticleRoot.localScale = scale;
                effect.RenderEffect();
            }
            return effect;
        }

        public static ImportedParticleEffect PlayComposite(Transform parent, Vector3 position,
            CompositeKind kind, float size, bool range)
        {
            if (!Application.isPlaying || parent == null || !parent.gameObject.activeInHierarchy) return null;
            var profile = Resources.Load<ImportedEffectProfile>("ImportedEffects");
            var prefab = profile != null ? (range ? profile.range : profile.burst) : null;
            var group = ImportedEffectProfile.GroupFor(kind);
            var template = prefab != null && group != null ? prefab.transform.Find(group) : null;
            if (template == null) return null;
            var effect = Create(parent, Vector2.one * size, template, range, range ? 2 : 1, false);
            if (effect != null)
            {
                effect.transform.localPosition = position + new Vector3(0, 0, -.08f);
                effect.GetComponent<MeshRenderer>().sortingOrder = range ? 1 : 4;
            }
            return effect;
        }

        static ImportedParticleEffect Create(Transform parent, Vector2 size, Transform template,
            bool loop, float halfHeight, bool selection)
        {
            var rendering = Resources.Load<CardDissolveProfile>("CardDissolve");
            if (rendering == null || rendering.compositeMaterial == null || size.x <= 0 || size.y <= 0) return null;
            GameObject output;
            if (parent is RectTransform)
            {
                output = new GameObject("ImportedEffect", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                var target = (RectTransform)output.transform;
                target.SetParent(parent, false);
                target.anchorMin = target.anchorMax = target.pivot = Vector2.one * .5f;
                target.anchoredPosition = Vector2.zero;
                target.sizeDelta = size;
                output.GetComponent<RawImage>().raycastTarget = false;
            }
            else
            {
                output = new GameObject("ImportedEffect", typeof(MeshFilter), typeof(MeshRenderer));
                output.transform.SetParent(parent, false);
                output.GetComponent<MeshFilter>().sharedMesh = Quad;
                output.transform.localScale = new Vector3(size.x, size.y, 1);
                output.GetComponent<MeshRenderer>().sortingOrder = 6;
            }
            output.layer = parent.gameObject.layer;
            var effect = output.AddComponent<ImportedParticleEffect>();
            try
            {
                effect.Configure(template, rendering, size, loop, halfHeight, selection);
                return effect;
            }
            catch (System.Exception error)
            {
                output.SetActive(false);
                BoardView.Kill(output);
                Debug.LogWarning("特效资源播放失败：" + error.Message, parent);
                return null;
            }
        }

        static Mesh _quad;
        static Mesh Quad
        {
            get
            {
                if (_quad != null) return _quad;
                _quad = new Mesh { name = "EffectQuad" };
                _quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0),
                    new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
                _quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                _quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                _quad.RecalculateBounds();
                return _quad;
            }
        }

        void Configure(Transform template, CardDissolveProfile rendering, Vector2 size,
            bool loop, float halfHeight, bool selection)
        {
            _loop = loop;
            _slot = 0;
            while (Slots.Contains(_slot)) _slot++;
            Slots.Add(_slot);
            _stage = new GameObject("ImportedEffectStage");
            _stage.transform.position = new Vector3(1000 + _slot * 100, 2000, 1000);
            ParticleRoot = Instantiate(template, _stage.transform, false);
            ParticleRoot.localPosition = Vector3.zero;
            ParticleRoot.gameObject.SetActive(true);
            foreach (var child in ParticleRoot.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = StageLayer;
            if (selection) ConfigureSelectionBorder(size);
            _particles = ParticleRoot.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var particle in _particles)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.useUnscaledTime = true;
                // 爆点组的父粒子是作者的循环控制器，关闭循环才能自然结束。
                if (!loop) main.loop = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            }
            foreach (var renderer in ParticleRoot.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null) continue;
                if (renderer.enabled && material.HasProperty("_Dst") && material.GetFloat("_Dst") == (float)BlendMode.One)
                    _additive.Add(renderer);
            }
            int height = loop ? 384 : 256;
            int width = Mathf.Clamp(Mathf.CeilToInt(height * size.x / size.y), 16, 1024);
            _output = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf) { name = "ImportedEffectColor" };
            _opacity = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "ImportedEffectOpacity" };
            _output.Create();
            _opacity.Create();
            _camera = MakeCamera(_output, halfHeight, selection);
            _camera.allowHDR = true;
            _opacityCamera = MakeCamera(_opacity, halfHeight, selection);
            if (loop) FitRangeProjection();
            if (rendering.postProcessProfile != null && rendering.postProcessResources != null)
            {
                var layer = _camera.gameObject.AddComponent<PostProcessLayer>();
                layer.Init(rendering.postProcessResources);
                layer.volumeLayer = 1 << StageLayer;
                layer.volumeTrigger = _camera.transform;
                layer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                var region = _camera.gameObject.AddComponent<BoxCollider>();
                region.isTrigger = true;
                region.size = Vector3.one * 2;
                var volume = _camera.gameObject.AddComponent<PostProcessVolume>();
                volume.isGlobal = false;
                volume.sharedProfile = rendering.postProcessProfile;
            }
            var composite = new Material(rendering.compositeMaterial);
            _material = composite;
            composite.mainTexture = _output;
            composite.SetTexture("_CardOpacity", _opacity);
            composite.SetFloat("_EffectOpacity", selection ? .14f : loop ? .35f : 1);
            var image = GetComponent<RawImage>();
            if (image != null)
            {
                composite.SetFloat("_ZTest", (float)CompareFunction.Always);
                image.material = composite;
                image.texture = _output;
            }
            else GetComponent<MeshRenderer>().sharedMaterial = composite;
            foreach (var particle in _particles)
            {
                particle.Simulate(loop ? .1f : .0001f, false, true, false);
                particle.Play(false);
            }
            RenderEffect();
        }

        void ConfigureSelectionBorder(Vector2 size)
        {
            // 原包选中材质的主贴图缺失；补透明边框，避免回退白贴图盖住整张卡牌。
            const int width = 128;
            int height = Mathf.Clamp(Mathf.RoundToInt(width * size.y / size.x), 16, 512);
            _selectionMask = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { name = "SelectionBorderMask", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float edge = Mathf.Min(Mathf.Min(x + .5f, width - x - .5f), Mathf.Min(y + .5f, height - y - .5f));
                    float mask = 1 - Mathf.SmoothStep(0, 1, (edge - .5f) / 2);
                    pixels[y * width + x] = new Color(mask, mask, mask, mask);
                }
            _selectionMask.SetPixels(pixels);
            _selectionMask.Apply(false, true);
            var renderer = ParticleRoot.GetComponent<ParticleSystemRenderer>();
            _selectionMaterial = new Material(renderer.sharedMaterial);
            _selectionMaterial.SetTexture("_MainTex", _selectionMask);
            _selectionMaterial.SetTextureScale("_MainTex", Vector2.one);
            _selectionMaterial.SetTextureOffset("_MainTex", Vector2.zero);
            _selectionMaterial.SetFloat("_MainTex_Uspeed", 0);
            _selectionMaterial.SetFloat("_MainTex_Vspeed", 0);
            // 选中边光只增加颜色，不使用消散卡面的遮挡量。
            _selectionMaterial.SetFloat("_Dst", (float)BlendMode.One);
            renderer.sharedMaterial = _selectionMaterial;
        }

        Camera MakeCamera(RenderTexture target, float halfHeight, bool selection)
        {
            var camera = new GameObject("EffectCamera", typeof(Camera)).GetComponent<Camera>();
            camera.gameObject.layer = StageLayer;
            camera.transform.SetParent(_stage.transform, false);
            // 范围模型只有侧壁；保留俯视角才能看见边缘，再由棋盘映射贴合格线。
            var rotation = selection ? Quaternion.Euler(90, 180, 0) : Quaternion.Euler(60, 20, 0);
            camera.transform.localPosition = rotation * Vector3.back * 40;
            camera.transform.localRotation = rotation;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = halfHeight;
            camera.aspect = target.width / (float)target.height;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 80;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.cullingMask = 1 << StageLayer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            camera.targetTexture = target;
            return camera;
        }

        void FitRangeProjection()
        {
            float span = 0;
            foreach (var particle in _particles)
            {
                var renderer = particle.GetComponent<ParticleSystemRenderer>();
                if (renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null)
                    span = Mathf.Max(span, renderer.mesh.bounds.size.x * particle.main.startSize.constantMax * particle.transform.lossyScale.x);
            }
            if (span <= 0) throw new System.InvalidOperationException("范围模型没有有效尺寸。");
            var floor = new Plane(Vector3.up, _stage.transform.position);
            var vertices = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var uv = Quad.uv[i];
                var ray = _camera.ViewportPointToRay(new Vector3(uv.x, uv.y, 0));
                if (!floor.Raycast(ray, out var distance)) throw new System.InvalidOperationException("范围相机无法投影到棋盘。");
                var point = ray.GetPoint(distance) - _stage.transform.position;
                vertices[i] = new Vector3(point.x / span, point.z / span, 0);
            }
            // 把观察角度投回地面，保留立体光壁，同时让底边落在规则的格线范围上。
            _rangeMesh = new Mesh { name = "RangeProjection", vertices = vertices, uv = Quad.uv, triangles = Quad.triangles };
            _rangeMesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _rangeMesh;
        }

        void LateUpdate()
        {
            _elapsed += Time.unscaledDeltaTime;
            RenderEffect();
            if (!_loop && (_elapsed > 8 || _elapsed > .1f && !Alive())) BoardView.Kill(gameObject);
        }

        bool Alive()
        {
            foreach (var particle in _particles)
                if (particle.GetComponent<Renderer>().enabled && particle.IsAlive(false)) return true;
            return false;
        }

        void RenderEffect()
        {
            _camera.Render();
            foreach (var renderer in _additive) renderer.enabled = false;
            try { _opacityCamera.Render(); }
            finally { foreach (var renderer in _additive) renderer.enabled = true; }
        }

        void OnDisable() => BoardView.Kill(gameObject);

        void OnDestroy()
        {
            if (_slot >= 0) Slots.Remove(_slot);
            if (_stage != null) { _stage.SetActive(false); BoardView.Kill(_stage); }
            BoardView.Kill(_material);
            BoardView.Kill(_selectionMaterial);
            BoardView.Kill(_selectionMask);
            BoardView.Kill(_rangeMesh);
            if (_output != null) { _output.Release(); BoardView.Kill(_output); }
            if (_opacity != null) { _opacity.Release(); BoardView.Kill(_opacity); }
        }
    }
}
