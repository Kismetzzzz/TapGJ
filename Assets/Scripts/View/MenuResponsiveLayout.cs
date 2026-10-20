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
        [SerializeField] bool referenceArtworkLayout;
        [SerializeField] RectTransform utilityButtons;
        [SerializeField] bool scrollLevelArtwork;
        [SerializeField] bool scrollChapterArtwork;
        float _width, _height;

        void LateUpdate()
        {
            float width = canvasRect.rect.width;
            float height = canvasRect.rect.height;
            if (width <= 0 || height <= 0 || Mathf.Abs(width - _width) < .5f && Mathf.Abs(height - _height) < .5f) return;
            _width = width;
            _height = height;
            if (scrollChapterArtwork)
                chaptersPage.localScale = Vector3.one * Mathf.Min(width / 1280f, height / 720f);
            else
                LayoutPage(chaptersPage, false, width);
            LayoutPage(levelsPage, true, width, scrollLevelArtwork);
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
            if (referenceArtworkLayout)
            {
                // ponytail: 参考画面采用完整等比构图；其他比例留出画边，避免题字和按钮被裁掉。
                float scale = Mathf.Min(width / 1280f, height / 720f);
                entryPage.localScale = utilityButtons.localScale = Vector3.one * scale;
                canvasRect.Find("SettingsPanel").localScale = Vector3.one * scale;
                canvasRect.Find("NoticePanel").localScale = Vector3.one * scale;
            }
            foreach (string name in new[] { "SettingsPanel", "NoticePanel", "LeaderboardPanel" })
            {
                if (referenceArtworkLayout && (name == "SettingsPanel" || name == "NoticePanel")) continue;
                var panel = (RectTransform)canvasRect.Find(name);
                panel.localScale = Vector3.one * Mathf.Min(1f, (width - 64) / panel.sizeDelta.x);
            }
        }

        static void LayoutPage(RectTransform page, bool levels, float canvasWidth, bool scrollArtwork = false)
        {
            float width = Mathf.Min(1160, canvasWidth - 64);
            page.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float left = -width * .5f, right = width * .5f;
            if (levels && scrollArtwork)
            {
                LayoutScrollPage(page, width);
                return;
            }
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

        static void LayoutScrollPage(RectTransform page, float width)
        {
            // ponytail: 卷轴只作为装饰，卡片数量和可滚动宽度继续由配置决定。
            float artWidth = width - 40;
            float artHeight = artWidth * 430f / 925f;
            var art = (RectTransform)page.Find("ScrollArtwork");
            art.sizeDelta = new Vector2(artWidth, artHeight);
            SetRect(page.Find("Title"), -width * .5f + 40, width - 230, true);
            SetY(page.Find("Title"), 310);
            SetRect(page.Find("Subtitle"), -width * .5f + 40, width - 170, true);
            SetY(page.Find("Subtitle"), 277);
            SetX(page.Find("BackButton"), width * .5f - 72);
            SetY(page.Find("BackButton"), 310);
            var scroll = page.Find("ScrollView").GetComponent<ScrollRect>();
            SetX(scroll.transform, artWidth * .085f);
            SetWidth(scroll.transform, artWidth * .715f);
            SetWidth(scroll.transform.Find("Track"), artWidth * .715f);
            scroll.content.GetComponent<LayoutElement>().minWidth = artWidth * .715f;
            LayoutRebuilder.MarkLayoutForRebuild(scroll.content);
            SetWidth(page.Find("StatusLabel"), width - 80);
            SetY(page.Find("StatusLabel"), -artHeight * .5f - 28);
        }

        static void SetY(Transform transform, float y)
        {
            var rect = (RectTransform)transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, y);
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
