using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>保持文字大小，窄屏减少可见卡片数量并保留横向滚动。</summary>
    [DisallowMultipleComponent]
    public sealed class MenuResponsiveLayout : MonoBehaviour
    {
        [SerializeField] RectTransform canvasRect;
        [SerializeField] RectTransform chaptersPage;
        [SerializeField] RectTransform levelsPage;
        [SerializeField] RectTransform mainMenu;
        [SerializeField] RectTransform entryPage;
        float _width;

        void LateUpdate()
        {
            float width = canvasRect.rect.width;
            if (width <= 0 || Mathf.Abs(width - _width) < .5f) return;
            _width = width;
            LayoutPage(chaptersPage, false, width);
            LayoutPage(levelsPage, true, width);
            float left = -Mathf.Min(310, width * .5f - 220);
            foreach (string name in new[] { "Brand", "Title", "Logo", "Subtitle", "StartButton", "LeaderboardButton", "QuitButton" })
                SetX(mainMenu.Find(name), left);
            string[] elements = { "Metal", "Wood", "Water", "Fire", "Earth" };
            for (int i = 0; i < elements.Length; i++) SetX(mainMenu.Find("Element_" + elements[i]), left - 80 + i * 40);
            float right = Mathf.Min(270, width * .225f);
            SetX(mainMenu.Find("ElementSeal"), right);
            SetX(mainMenu.Find("ArtIllustration"), right);
            SetX(mainMenu.Find("SealCaption"), right);
            SetX(mainMenu.Find("SealHint"), right);
            mainMenu.Find("ElementSeal").localScale = Vector3.one * Mathf.Clamp(width / 1160f, .7f, 1f);
            var welcome = (RectTransform)entryPage.Find("WelcomeBar");
            welcome.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(800, width - 100));
            foreach (string name in new[] { "SettingsPanel", "NoticePanel", "LeaderboardPanel" })
            {
                var panel = (RectTransform)canvasRect.Find(name);
                panel.localScale = Vector3.one * Mathf.Min(1f, (width - 64) / panel.sizeDelta.x);
            }
        }

        static void LayoutPage(RectTransform page, bool levels, float canvasWidth)
        {
            float width = Mathf.Min(1160, canvasWidth - 64);
            page.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float left = -width * .5f, right = width * .5f;
            SetRect(page.Find("Title"), left + 40, width - 230, true);
            SetRect(page.Find("Subtitle"), left + 40, width - 170, true);
            SetX(page.Find("BackButton"), right - 72);
            SetWidth(page.Find("HeaderRule"), width - 80);
            SetWidth(page.Find("FooterRule"), width - 80);
            var scroll = page.Find("ScrollView").GetComponent<ScrollRect>();
            float scrollWidth = width - (levels ? 124 : 80);
            SetWidth(scroll.transform, scrollWidth);
            SetWidth(scroll.transform.Find("Track"), scrollWidth);
            scroll.content.GetComponent<LayoutElement>().minWidth = scrollWidth;
            LayoutRebuilder.MarkLayoutForRebuild(scroll.content);
            if (levels)
            {
                SetX(page.Find("ChapterMarker"), left + 39);
                SetWidth(page.Find("StatusLabel"), width - 80);
            }
            else
            {
                SetRect(page.Find("Availability"), left + 40, width - 450, true);
                SetX(page.Find("PrototypeButton"), right - 215);
            }
        }

        static void SetX(Transform transform, float x)
        {
            var rect = (RectTransform)transform;
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
        }

        static void SetWidth(Transform transform, float width) =>
            ((RectTransform)transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

        static void SetRect(Transform transform, float x, float width, bool leftEdge)
        {
            SetWidth(transform, width);
            SetX(transform, x + (leftEdge ? width * .5f : 0));
        }
    }
}
