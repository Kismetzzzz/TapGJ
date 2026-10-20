using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.EditorTools
{
    /// <summary>主菜单场景若还停在编译前的内存副本，补上模式页并接上引用。</summary>
    [InitializeOnLoad]
    public static class ModeSelectPageInstaller
    {
        const string ArtPath = "Assets/UI/Art/ModeSelect/";

        static ModeSelectPageInstaller() => EditorApplication.delayCall += Ensure;

        [MenuItem("TapGJ/补齐模式选择页", false, 8)]
        public static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var controller = Object.FindFirstObjectByType<MainMenuController>();
            if (controller == null) return;
            var navigation = Object.FindFirstObjectByType<Canvas>().transform.Find("Navigation");
            if (navigation == null) return;
            var data = new SerializedObject(controller);
            var page = navigation.Find("ModePage") as RectTransform;
            if (data.FindProperty("modePage").objectReferenceValue != null && page != null && HasFeedback(page)) return;
            if (page == null) page = CreatePage(navigation);
            data.FindProperty("modePage").objectReferenceValue = page.gameObject;
            data.FindProperty("campaignButton").objectReferenceValue = page.Find("CampaignButton").GetComponent<Button>();
            data.FindProperty("endlessButton").objectReferenceValue = page.Find("EndlessButton").GetComponent<Button>();
            data.FindProperty("rankingButton").objectReferenceValue = page.Find("RankingButton").GetComponent<Button>();
            data.FindProperty("exitButton").objectReferenceValue = page.Find("ExitButton").GetComponent<Button>();
            data.ApplyModifiedPropertiesWithoutUndo();

            var layout = navigation.GetComponentInParent<MenuResponsiveLayout>();
            if (layout != null)
            {
                var layoutData = new SerializedObject(layout);
                layoutData.FindProperty("modePage").objectReferenceValue = page;
                layoutData.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (string name in new[] { "CampaignButton", "EndlessButton", "RankingButton", "ExitButton" })
            {
                var button = page.Find(name);
                if (button == null || button.GetComponent<MenuButtonFeedback>() != null) continue;
                var feedback = button.gameObject.AddComponent<MenuButtonFeedback>();
                var feedbackData = new SerializedObject(feedback);
                feedbackData.FindProperty("artwork").objectReferenceValue = button.GetComponent<Image>();
                feedbackData.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        static bool HasFeedback(Transform page)
        {
            foreach (string name in new[] { "CampaignButton", "EndlessButton", "RankingButton", "ExitButton" })
            {
                var button = page.Find(name);
                if (button == null || button.GetComponent<MenuButtonFeedback>() == null) return false;
            }
            return true;
        }

        static RectTransform CreatePage(Transform navigation)
        {
            var page = new GameObject("ModePage", typeof(RectTransform)).GetComponent<RectTransform>();
            page.gameObject.layer = 5;
            page.SetParent(navigation, false);
            page.anchorMin = page.anchorMax = page.pivot = new Vector2(.5f, .5f);
            page.sizeDelta = new Vector2(1280, 720);
            page.SetSiblingIndex(navigation.Find("EntryPage").GetSiblingIndex() + 1);

            var art = new GameObject("Artwork", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            art.layer = 5;
            art.transform.SetParent(page, false);
            var artRect = (RectTransform)art.transform;
            artRect.anchorMin = Vector2.zero;
            artRect.anchorMax = Vector2.one;
            artRect.offsetMin = artRect.offsetMax = Vector2.zero;
            var artImage = art.GetComponent<Image>();
            artImage.sprite = Sprite("Background");
            artImage.color = Color.white;
            artImage.raycastTarget = false;

            var campaign = Button(page, "CampaignButton", "Campaign", new Vector2(-313.125f, -10.017f), new Vector2(337.5f, 87.652f));
            var endless = Button(page, "EndlessButton", "Endless", new Vector2(-313.75f, -92.035f), new Vector2(336.25f, 86.4f));
            var ranking = Button(page, "RankingButton", "Ranking", new Vector2(-313.75f, -169.67f), new Vector2(336.25f, 88.904f));
            var exit = Button(page, "ExitButton", "Exit", new Vector2(-313.75f, -246.052f), new Vector2(336.25f, 88.904f));
            Link(campaign, exit, endless);
            Link(endless, campaign, ranking);
            Link(ranking, endless, exit);
            Link(exit, ranking, campaign);
            page.gameObject.SetActive(false);
            return page;
        }

        static Button Button(RectTransform parent, string name, string sprite, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = Sprite(sprite);
            image.preserveAspect = true;
            image.color = Color.white;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            var feedback = go.AddComponent<MenuButtonFeedback>();
            var feedbackData = new SerializedObject(feedback);
            feedbackData.FindProperty("artwork").objectReferenceValue = image;
            feedbackData.ApplyModifiedPropertiesWithoutUndo();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = colors.selectedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(.86f, .86f, .86f, 1f);
            colors.fadeDuration = .1f;
            button.colors = colors;
            return button;
        }

        static void Link(Button button, Button up, Button down)
        {
            button.navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down
            };
        }

        static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
    }
}
