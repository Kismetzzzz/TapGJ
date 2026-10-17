using UnityEngine;

namespace TapGJ.View
{
    /// <summary>将背景和全部界面一起等比放入安全区域，保持棋盘位置一致。</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BattleUiLayout : MonoBehaviour
    {
        public static readonly Vector2 ReferenceSize = new Vector2(1920, 1080);
        [SerializeField] RectTransform canvasRect;
        [SerializeField] RectTransform composition;
        Vector2 _canvasSize;
        Rect _safeArea;

        void OnEnable() => Refresh();
        void LateUpdate()
        {
            if (canvasRect == null || composition == null) return;
            if (_canvasSize != canvasRect.rect.size || _safeArea != GetSafeArea()) Refresh();
        }

        public static float FitScale(Vector2 availableSize) => Mathf.Max(0,
            Mathf.Min(availableSize.x / ReferenceSize.x, availableSize.y / ReferenceSize.y));

        Rect GetSafeArea() => Application.isPlaying ? Screen.safeArea : new Rect(0, 0, Screen.width, Screen.height);

        public void Refresh()
        {
            if (canvasRect == null || composition == null || Screen.width <= 0 || Screen.height <= 0) return;
            _canvasSize = canvasRect.rect.size;
            _safeArea = GetSafeArea();
            var ratio = new Vector2(_canvasSize.x / Screen.width, _canvasSize.y / Screen.height);
            composition.sizeDelta = ReferenceSize;
            composition.localScale = Vector3.one * FitScale(Vector2.Scale(_safeArea.size, ratio));
            composition.anchoredPosition = Vector2.Scale(
                _safeArea.center - new Vector2(Screen.width, Screen.height) * .5f, ratio);
        }
    }
}
