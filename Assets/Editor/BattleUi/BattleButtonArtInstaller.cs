using System;
using System.IO;
using System.Linq;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapGJ.EditorTools
{
    /// <summary>补齐现有局内按钮插画，保留布局、文字数据与事件接线。</summary>
    public static class BattleButtonArtInstaller
    {
        const string ArtPath = "Assets/UI/Art/Battle/Buttons/";
        const string PrefabPath = "Assets/UI/Prefabs/BattleGameplay.prefab";

        [MenuItem("TapGJ/更新局内按钮插画", false, 7)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先退出运行模式，再更新按钮插画。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再更新按钮插画。");

            var source = SceneManager.GetSceneByPath(BattleUiSceneBuilder.ScenePath);
            bool opened = !source.isLoaded;
            if (opened) source = EditorSceneManager.OpenScene(BattleUiSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                Apply(source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattleUiController>(true)).Single());
                EditorSceneManager.SaveScene(source);
            }
            finally { if (opened) EditorSceneManager.CloseScene(source, true); }

            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var composition = prefab.transform.Find("Composition");
                var scale = composition.localScale;
                Apply(prefab.GetComponent<BattleUiController>());
                composition.localScale = scale;
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            Debug.Log("[TapGJ] 已更新预览与玩法界面的按钮插画。");
        }

        public static void Apply(BattleUiController ui)
        {
            var data = new SerializedObject(ui);
            data.FindProperty("endDiscardPhaseSprite").objectReferenceValue = Sprite("EndDiscardPhase");
            data.ApplyModifiedPropertiesWithoutUndo();
            var composition = ui.transform.Find("Composition");
            ApplyActionButton(composition.Find("Controls/NextRoundButton").GetComponent<Button>(), "EndPlayerPhase");
            ApplyActionButton(composition.Find("Controls/DiscardButton").GetComponent<Button>(), "Discard");
            ApplyButton(composition.Find("Controls/SettingsButton").GetComponent<Button>(), "Settings");
            ApplyButton(composition.Find("SettingsPanel/CloseButton").GetComponent<Button>(), "Continue");
            foreach (string panel in new[] { "SettingsPanel", "ResultPanel/Dialog" })
            {
                var root = composition.Find(panel);
                if (root == null) continue;
                foreach (var entry in new[] { ("RestartButton", "Restart"), ("MenuButton", "ReturnMenu"), ("NextLevelButton", "NextLevel") })
                {
                    var child = root.Find(entry.Item1);
                    if (child != null) ApplyButton(child.GetComponent<Button>(), entry.Item2);
                }
            }
        }

        static void ApplyActionButton(Button button, string name)
        {
            ApplyButton(button, name);
            var root = (RectTransform)button.transform;
            root.sizeDelta = new Vector2(300, 82);
            root.anchoredPosition = new Vector2(1596, root.anchoredPosition.y);
            var artwork = (Image)button.targetGraphic;
            artwork.preserveAspect = true;
            if (artwork.GetComponent<ActionButtonArtwork>() == null)
                artwork.gameObject.AddComponent<ActionButtonArtwork>();
            var rect = artwork.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(300, 72);
            var ring = (RectTransform)button.transform.Find("FocusRing");
            ring.anchorMin = Vector2.zero;
            ring.anchorMax = Vector2.one;
            ring.offsetMin = -Vector2.one * 3;
            ring.offsetMax = Vector2.one * 3;
            var label = button.GetComponentInChildren<Text>(true).rectTransform;
            label.anchorMin = Vector2.zero;
            label.anchorMax = Vector2.one;
            label.offsetMin = Vector2.one * 4;
            label.offsetMax = -Vector2.one * 4;
            if (button.transform.Find("HitArea") == null)
            {
                var hit = new GameObject("HitArea", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                hit.transform.SetParent(button.transform, false);
                hit.gameObject.layer = button.gameObject.layer;
                hit.color = Color.clear;
                hit.raycastTarget = true;
                hit.rectTransform.anchorMin = Vector2.zero;
                hit.rectTransform.anchorMax = Vector2.one;
                hit.rectTransform.offsetMin = hit.rectTransform.offsetMax = Vector2.zero;
                hit.transform.SetAsFirstSibling();
            }
        }

        static Sprite Sprite(string name)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
            if (sprite == null) throw new FileNotFoundException("局内按钮插画缺失", ArtPath + name + ".png");
            return sprite;
        }

        static void ApplyButton(Button button, string name)
        {
            var child = button.transform.Find("Artwork");
            var artwork = child != null ? child.GetComponent<Image>() :
                new GameObject("Artwork", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            artwork.transform.SetParent(button.transform, false);
            artwork.gameObject.layer = button.gameObject.layer;
            var rect = artwork.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            artwork.sprite = Sprite(name);
            artwork.type = Image.Type.Simple;
            artwork.preserveAspect = artwork.raycastTarget = true;
            artwork.color = Color.white;
            artwork.transform.SetAsFirstSibling();
            button.GetComponent<Image>().enabled = false;
            button.targetGraphic = artwork;
            button.GetComponentInChildren<Text>(true).enabled = false;
            if (button.TryGetComponent<Outline>(out var outline)) outline.enabled = false;
            foreach (Transform item in button.transform)
                if (item.name.StartsWith("Gear", StringComparison.Ordinal)) item.gameObject.SetActive(false);

            var ring = button.transform.Find("FocusRing");
            if (ring == null)
            {
                var image = new GameObject("FocusRing", typeof(RectTransform), typeof(Image), typeof(CanvasGroup)).GetComponent<Image>();
                image.transform.SetParent(button.transform, false);
                image.gameObject.layer = button.gameObject.layer;
                image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiFocus.png");
                image.type = Image.Type.Sliced;
                image.color = new Color(.98f, .84f, .48f);
                image.raycastTarget = false;
                image.rectTransform.anchorMin = Vector2.zero;
                image.rectTransform.anchorMax = Vector2.one;
                image.rectTransform.offsetMin = -Vector2.one * 3;
                image.rectTransform.offsetMax = Vector2.one * 3;
                ring = image.transform;
            }
            var group = ring.GetComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = group.interactable = false;
            var feedback = button.GetComponent<MenuButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<MenuButtonFeedback>();
            var data = new SerializedObject(feedback);
            data.FindProperty("focusRing").objectReferenceValue = group;
            data.FindProperty("artwork").objectReferenceValue = artwork;
            data.ApplyModifiedPropertiesWithoutUndo();
            var colors = button.colors;
            colors.normalColor = colors.highlightedColor = colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.86f, .89f, .86f);
            colors.disabledColor = new Color(.83f, .83f, .83f, .7f);
            button.colors = colors;
        }
    }
}
