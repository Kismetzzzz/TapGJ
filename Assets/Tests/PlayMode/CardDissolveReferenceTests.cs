using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TapGJ.Tests
{
    public sealed class CardDissolveReferenceTests
    {
        [System.Serializable]
        sealed class FrameResult
        {
            public float time, meanError, activeMeanError, changedFraction;
            public float bloomDifference, referenceHdrPeak;
            public int cardParticles, sparkParticles;
        }
        [System.Serializable]
        sealed class CaptureReport
        {
            public string note;
            public bool importedProfileResolved, importedPostMaterialResolved, originalCameraHdr;
            public FrameResult[] frames;
        }

        [UnityTest]
        public IEnumerator 原包与接入版_固定时间画面对照()
        {
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/BattleUI.unity");
            yield return null;
            var profile = Resources.Load<CardDissolveProfile>("CardDissolve");
            var originalCard = profile.prefab.transform.Find("bofang/kapai_01").GetComponent<ParticleSystem>();
            var originalTexture = (Texture2D)originalCard.GetComponent<ParticleSystemRenderer>().sharedMaterial.GetTexture("_MainTex");
            var sprite = Sprite.Create(originalTexture, new Rect(0, 0, originalTexture.width, originalTexture.height), Vector2.one * .5f);
            var source = new GameObject("CardFace", typeof(RectTransform), typeof(Image));
            source.transform.SetParent(Object.FindFirstObjectByType<Canvas>().transform, false);
            ((RectTransform)source.transform).sizeDelta = new Vector2(600, 970);
            source.GetComponent<Image>().sprite = sprite;
            var effect = CardDissolveEffect.Play(source, Color.white);
            var candidateTarget = (RenderTexture)effect.OutputTexture;
            var referenceTarget = new RenderTexture(candidateTarget.width, candidateTarget.height, 24, RenderTextureFormat.ARGBHalf);
            var reference = Object.Instantiate(profile.prefab);
            reference.GetComponent<PlayableDirector>().Stop();
            reference.GetComponent<PlayableDirector>().enabled = false;
            reference.transform.position = effect.CardParticles.transform.parent.position;
            foreach (var child in reference.GetComponentsInChildren<Transform>()) child.gameObject.layer = 29;
            var referenceCard = reference.transform.Find("bofang/kapai_01").GetComponent<ParticleSystem>();
            var referenceSparks = reference.transform.Find("bofang/lizi").GetComponent<ParticleSystem>();
            var referenceCamera = reference.transform.Find("Camera01").GetComponent<Camera>();
            referenceCamera.enabled = false;
            referenceCamera.cullingMask = 1 << 29;
            referenceCamera.targetTexture = referenceTarget;
            var ownedMaterials = new List<Material>();
            bool importedProfileResolved = false, importedPostMaterialResolved = false;
            foreach (var script in referenceCamera.GetComponents<MonoBehaviour>())
            {
                if (script == null) continue;
                var field = script.GetType().GetField("PostProcessMat");
                var originalPostMaterial = field != null ? field.GetValue(script) as Material : null;
                if (originalPostMaterial != null)
                {
                    importedPostMaterialResolved = true;
                    var material = new Material(originalPostMaterial);
                    field.SetValue(script, material);
                    ownedMaterials.Add(material);
                }
                if (script.GetType().Name == "PostProcessLayer")
                {
                    script.GetType().GetField("volumeLayer").SetValue(script, (LayerMask)(1 << 29));
                    script.GetType().GetField("volumeTrigger").SetValue(script, referenceCamera.transform);
                }
                if (script.GetType().Name == "PostProcessVolume")
                {
                    importedProfileResolved = script.GetType().GetField("sharedProfile").GetValue(script) as Object != null;
                    script.enabled = false;
                    script.enabled = true;
                }
            }
            var cameraPosition = referenceCamera.transform.localPosition;
            var cameraRotation = referenceCamera.transform.localRotation;
            var cameraBackground = referenceCamera.backgroundColor;
            var cameraSize = referenceCamera.orthographicSize;
            var cardRenderer = referenceCard.GetComponent<ParticleSystemRenderer>();
            var cardMaterial = new Material(cardRenderer.sharedMaterial);
            cardRenderer.sharedMaterial = cardMaterial;
            ownedMaterials.Add(cardMaterial);
            var originalUv = cardMaterial.GetVector("_MainTex_ST");
            var pixels = new Texture2D(referenceTarget.width, referenceTarget.height, TextureFormat.RGBAFloat, false, true);
            var previousTarget = RenderTexture.active;
            var outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "CardDissolveComparison"));
            Directory.CreateDirectory(outputDirectory);
            var results = new List<FrameResult>();
            float[] times = { 0, .3f, .6f, 1f, 1.25f, 1.5f };
            var renderCandidate = typeof(CardDissolveEffect).GetMethod("RenderEffect", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                foreach (float time in times)
                {
                    SetTime(referenceCard, time, 9251);
                    SetTime(referenceSparks, time, 9251);
                    SetTime(effect.CardParticles, time, 9251);
                    SetTime(effect.SparkParticles, time, 9251);
                    referenceCamera.orthographic = false;
                    referenceCamera.transform.localPosition = cameraPosition;
                    referenceCamera.transform.localRotation = cameraRotation;
                    referenceCamera.backgroundColor = cameraBackground;
                    referenceCamera.orthographicSize = cameraSize;
                    cardMaterial.SetVector("_MainTex_ST", originalUv);
                    referenceCamera.Render();
                    ReadAndSave(referenceTarget, pixels, Path.Combine(outputDirectory, "author-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));

                    // 保留原包后处理；仅统一相机取景、背景与卡图朝向，隔离显示适配的影响。
                    referenceCamera.orthographic = true;
                    referenceCamera.orthographicSize = 9.7f * 1.3f * .5f;
                    referenceCamera.transform.localPosition = new Vector3(0, 20, 0);
                    referenceCamera.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    referenceCamera.backgroundColor = Color.clear;
                    cardMaterial.SetVector("_MainTex_ST", new Vector4(1, -1, 0, 1));
                    referenceCamera.Render();
                    var referencePixels = ReadAndSave(referenceTarget, pixels, Path.Combine(outputDirectory, "reference-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                    renderCandidate.Invoke(effect, null);
                    var candidatePixels = ReadAndSave(candidateTarget, pixels, Path.Combine(outputDirectory, "candidate-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
#if UNITY_EDITOR
                    var volume = referenceCamera.GetComponents<MonoBehaviour>().First(s => s != null && s.GetType().Name == "PostProcessVolume");
                    var profileField = volume.GetType().GetField("sharedProfile");
                    profileField.SetValue(volume, UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/yuansupai/texiao/Camera Profile 1.asset"));
                    volume.enabled = false;
                    volume.enabled = true;
                    referenceCamera.Render();
                    var bloomPixels = ReadAndSave(referenceTarget, pixels, Path.Combine(outputDirectory, "repaired-bloom-" + time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png"));
                    profileField.SetValue(volume, null);
                    volume.enabled = false;
                    volume.enabled = true;
#else
                    var bloomPixels = referencePixels;
#endif
                    float total = 0, activeTotal = 0;
                    float bloomDifference = 0, hdrPeak = 0;
                    int active = 0, changed = 0;
                    for (int i = 0; i < referencePixels.Length; i++)
                    {
                        var a = bloomPixels[i];
                        var b = candidatePixels[i];
                        float error = (Mathf.Abs(Mathf.Clamp01(a.r) - Mathf.Clamp01(b.r)) + Mathf.Abs(Mathf.Clamp01(a.g) - Mathf.Clamp01(b.g)) + Mathf.Abs(Mathf.Clamp01(a.b) - Mathf.Clamp01(b.b))) / 3;
                        total += error;
                        hdrPeak = Mathf.Max(hdrPeak, a.maxColorComponent);
                        bloomDifference += (Mathf.Abs(Mathf.Clamp01(a.r) - Mathf.Clamp01(referencePixels[i].r)) + Mathf.Abs(Mathf.Clamp01(a.g) - Mathf.Clamp01(referencePixels[i].g)) + Mathf.Abs(Mathf.Clamp01(a.b) - Mathf.Clamp01(referencePixels[i].b))) / 3;
                        if (Mathf.Max(a.r, Mathf.Max(a.g, a.b)) > .02f || Mathf.Max(b.r, Mathf.Max(b.g, b.b)) > .02f)
                        {
                            active++;
                            activeTotal += error;
                            if (error > .05f) changed++;
                        }
                    }
                    results.Add(new FrameResult { time = time, meanError = total / referencePixels.Length,
                        activeMeanError = active > 0 ? activeTotal / active : 0,
                        changedFraction = active > 0 ? changed / (float)active : 0,
                        bloomDifference = bloomDifference / referencePixels.Length, referenceHdrPeak = hdrPeak,
                        cardParticles = referenceCard.particleCount, sparkParticles = referenceSparks.particleCount });
                    Assert.AreEqual(referenceCard.particleCount, effect.CardParticles.particleCount);
                    Assert.AreEqual(referenceSparks.particleCount, effect.SparkParticles.particleCount);
                }
                File.WriteAllText(Path.Combine(outputDirectory, "comparison.json"), JsonUtility.ToJson(new CaptureReport
                { note = "author 为当前导入状态的原包相机；reference 只统一取景、背景和卡图朝向；candidate 为接入版；repaired-bloom 仅在测试实例接回包内 Camera Profile 1。误差比较 repaired-bloom 与 candidate；bloomDifference 是有无泛光的差异。相同种子与时间；颜色统一转换成 sRGB 后比较，原文件未修改。", importedProfileResolved = importedProfileResolved, importedPostMaterialResolved = importedPostMaterialResolved, originalCameraHdr = referenceCamera.allowHDR, frames = results.ToArray() }, true));
                Assert.AreEqual(times.Length, results.Count);
                foreach (var result in results)
                    Assert.Less(result.activeMeanError, .01f, "相同粒子、时间与取景时，接入版应与原包补齐泛光后的画面一致：" + result.time);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                referenceCamera.targetTexture = null;
                referenceTarget.Release();
                Object.Destroy(referenceTarget);
                Object.Destroy(reference);
                Object.Destroy(effect.gameObject);
                Object.Destroy(source);
                Object.Destroy(sprite);
                Object.Destroy(pixels);
                foreach (var material in ownedMaterials) Object.Destroy(material);
            }
        }

        static void SetTime(ParticleSystem particles, float time, uint seed)
        {
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = seed;
            particles.Simulate(Mathf.Max(.0001f, time), false, true, time > 0);
        }

        static Color[] ReadAndSave(RenderTexture target, Texture2D pixels, string path)
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            var png = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var colors = pixels.GetPixels();
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && !target.sRGB)
                for (int i = 0; i < colors.Length; i++) colors[i] = colors[i].gamma;
            png.SetPixels(colors);
            png.Apply();
            File.WriteAllBytes(path, png.EncodeToPNG());
            Object.Destroy(png);
            return colors;
        }
    }
}
