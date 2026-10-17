using System;
using System.Linq;
using TapGJ.Core;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapGJ.EditorTools
{
    /// <summary>只替换五行快捷选牌区域，保留棋盘、手牌与其他界面的现有配置。</summary>
    public static class ElementAbacusInstaller
    {
        const string Art = "Assets/UI/Art/ElementAbacus/";
        const string PrefabPath = "Assets/UI/Prefabs/BattleGameplay.prefab";
        static readonly Color Ink = new Color(.21f, .27f, .31f);

        [MenuItem("TapGJ/更新五行算盘", false, 7)]
        public static void Install()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再更新五行算盘。");

            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // 编辑预制体时画布尺寸可能暂时为零，保留保存的整体缩放。
                var composition = prefab.transform.Find("Composition");
                var scale = composition.localScale;
                Replace(prefab.GetComponent<BattleUiController>());
                composition.localScale = scale;
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            var scene = EditorSceneManager.GetSceneByPath(BattleUiSceneBuilder.ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(BattleUiSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                var ui = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleUiController>(true)).Single();
                Replace(ui);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
            Debug.Log("[TapGJ] 五行算盘已更新：预览场景与玩法预制体。");
        }

        static void Replace(BattleUiController ui)
        {
            var controls = ui.transform.Find("Composition/Controls");
            var old = controls.Find("ElementRail");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var abacus = Create(controls, out var buttons, out var selections);
            abacus.Setup(ui);
            var data = new SerializedObject(ui);
            BindArray(data, "elementButtons", buttons);
            BindArray(data, "elementSelections", selections);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        public static ElementAbacusView Create(Transform controls, out Button[] buttons, out CanvasGroup[] selections)
        {
            var rail = Rect(controls, "ElementRail", 0, 0, 384, 412);
            rail.anchorMin = rail.anchorMax = rail.pivot = Vector2.zero;
            rail.anchoredPosition = new Vector2(24, 24);
            rail.localScale = new Vector3(.7f, .7f, 1);
            Label(rail, "Heading", "五行算盘", 0, 0, 384, 36, 27);
            var hint = Label(rail, "Hint", "集齐 5 点，生成对应元素牌", 0, 36, 384, 28, 20);
            var backing = Picture(rail, "Backing", null, 2, 80, 380, 317);
            backing.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiSurface.png");
            backing.type = Image.Type.Sliced;
            backing.color = new Color(.98f, .93f, .81f, .96f);
            var frame = Picture(rail, "Frame", "Frame", 0, 78, 384, 321);
            buttons = new Button[5];
            selections = new CanvasGroup[5];
            var lowerBeads = new RectTransform[25];
            var upperBeads = new RectTransform[10];
            var icons = new Image[5];
            var flashes = new CanvasGroup[5];
            var counters = new Text[5];
            var focusSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiRing.png");
            float[] centers = { 74.8f, 133.6f, 192f, 250.4f, 309.2f };
            for (int i = 0; i < buttons.Length; i++)
            {
                var element = (Element)i;
                string name = element.ToString();
                var column = Rect(frame.transform, "Element_" + name, centers[i] - 28, 8, 56, 300);
                var hit = column.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                var button = column.gameObject.AddComponent<Button>();
                button.targetGraphic = hit;
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
                buttons[i] = button;

                for (int bead = 0; bead < 5; bead++)
                    lowerBeads[i * 5 + bead] = Bead(column, "LowerBead" + bead, name, 180 + bead * 26.5f);
                for (int bead = 0; bead < 2; bead++)
                {
                    upperBeads[i * 2 + bead] = Bead(column, "UpperBead" + bead, name, 53 - bead * 32);
                    upperBeads[i * 2 + bead].GetComponent<Image>().color = new Color(1, 1, 1, .25f);
                }
                var icon = Picture(column, "Icon", "Icon" + name, 6, 92, 44, 34);
                icon.preserveAspect = true;
                icons[i] = icon;
                counters[i] = Label(column, "Progress", "0/5", 0, 126, 56, 18, 16);
                var flash = Picture(column, "ChargeFlash", null, -4, 82, 64, 64);
                flash.sprite = focusSprite;
                flash.color = new Color(1f, .95f, .36f);
                flashes[i] = Group(flash.gameObject);

                var selected = Picture(column, "SelectedRing", null, 0, 86, 56, 56);
                selected.sprite = focusSprite;
                selected.color = new Color(1f, .89f, .52f);
                selections[i] = Group(selected.gameObject);
                var focus = Picture(column, "FocusRing", null, 0, 86, 56, 56);
                focus.sprite = focusSprite;
                focus.color = new Color(.98f, .84f, .48f);
                var feedback = column.gameObject.AddComponent<MenuButtonFeedback>();
                var feedbackData = new SerializedObject(feedback);
                feedbackData.FindProperty("focusRing").objectReferenceValue = Group(focus.gameObject);
                feedbackData.ApplyModifiedPropertiesWithoutUndo();
            }
            var reward = Picture(rail, "RewardCard", null, 0, 0, 90, 145);
            reward.rectTransform.pivot = new Vector2(.5f, .5f);
            reward.preserveAspect = true;
            var rewardGroup = Group(reward.gameObject);
            var view = rail.gameObject.AddComponent<ElementAbacusView>();
            var viewData = new SerializedObject(view);
            BindArray(viewData, "lowerBeads", lowerBeads);
            BindArray(viewData, "upperBeads", upperBeads);
            BindArray(viewData, "icons", icons);
            BindArray(viewData, "flashes", flashes);
            BindArray(viewData, "counters", counters);
            viewData.FindProperty("hint").objectReferenceValue = hint;
            viewData.FindProperty("rewardCard").objectReferenceValue = reward;
            viewData.FindProperty("rewardGroup").objectReferenceValue = rewardGroup;
            viewData.FindProperty("cardArt").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BattleCardArt>("Assets/UI/Data/BattleCardArt.asset");
            viewData.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        static RectTransform Bead(Transform column, string label, string element, float y)
        {
            var bead = Picture(column, label, "Bead" + element, 6, y - 13, 44, 26).rectTransform;
            bead.pivot = new Vector2(.5f, .5f);
            bead.anchoredPosition = new Vector2(28, -y);
            return bead;
        }

        static CanvasGroup Group(GameObject target)
        {
            var group = target.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.blocksRaycasts = group.interactable = false;
            return group;
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Image Picture(Transform parent, string name, string sprite, float x, float y, float width, float height)
        {
            var image = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Image>();
            if (sprite != null) image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + sprite + ".png");
            image.raycastTarget = false;
            return image;
        }

        static Text Label(Transform parent, string name, string value, float x, float y, float width, float height, int size)
        {
            var label = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Text>();
            label.text = value;
            label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Art/Battle/BattleLabels.ttf");
            label.fontSize = size;
            label.color = Ink;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            UiFontThemeInstaller.BindText(label, name == "Heading" ? UiFontRole.Title : UiFontRole.Body);
            return label;
        }

        static void BindArray<T>(SerializedObject data, string field, T[] values) where T : Object
        {
            var property = data.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
