using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>独立描边与可选插画反馈；保留按钮的美术着色和点击区域。</summary>
    [DisallowMultipleComponent]
    public sealed class MenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        [SerializeField] CanvasGroup focusRing;
        [SerializeField] Image artwork;
        [SerializeField, Range(.9f, 1f)] float pressedScale = .97f;
        [SerializeField, Min(0f)] float transitionSeconds = .1f;
        Button _button;
        bool _hovered, _selected, _pressed;
        bool _visualReady;
        Vector3 _artworkScale, _ringScale;
        Color _artworkTint;
        Material _originalMaterial, _feedbackMaterial;
        float _pressAmount, _highlightAmount, _submitUntil;

        void Awake() => _button = GetComponent<Button>();

        void OnEnable()
        {
            _hovered = _pressed = false;
            _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            if (artwork != null && artwork.gameObject.activeInHierarchy)
            {
                _artworkScale = artwork.rectTransform.localScale;
                if (_feedbackMaterial == null)
                {
                    _originalMaterial = artwork.material;
                    _feedbackMaterial = new Material(_originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
                    _artworkTint = _feedbackMaterial.GetColor("_Color");
                    artwork.material = _feedbackMaterial;
                }
                if (focusRing != null) _ringScale = focusRing.transform.localScale;
                _visualReady = true;
            }
            Refresh();
        }

        void OnDisable()
        {
            _hovered = _selected = _pressed = false;
            _submitUntil = 0;
            if (focusRing != null) focusRing.alpha = 0;
            ResetArtwork();
        }

        void OnDestroy()
        {
            if (artwork != null && _feedbackMaterial != null) artwork.material = _originalMaterial;
            if (_feedbackMaterial != null) Destroy(_feedbackMaterial);
        }

        void LateUpdate()
        {
            Refresh();
            if (!_visualReady) return;
            if (!Interactable || !artwork.gameObject.activeInHierarchy)
            {
                _pressed = false;
                _submitUntil = 0;
                ResetArtwork();
                return;
            }
            float step = transitionSeconds > 0 ? Time.unscaledDeltaTime / transitionSeconds : 1f;
            _pressAmount = Mathf.MoveTowards(_pressAmount, Pressed ? 1f : 0f, step);
            _highlightAmount = Mathf.MoveTowards(_highlightAmount, _hovered || _selected ? 1f : 0f, step);
            float scale = Mathf.Lerp(1f, pressedScale, _pressAmount);
            artwork.rectTransform.localScale = _artworkScale * scale;
            if (focusRing != null) focusRing.transform.localScale = _ringScale * scale;
            var tint = _artworkTint;
            float brightness = Mathf.Lerp(1f, 1.045f, _highlightAmount);
            tint.r *= brightness; tint.g *= brightness; tint.b *= brightness;
            SetArtworkTint(tint);
        }

        bool Interactable => isActiveAndEnabled && _button != null && _button.IsInteractable();
        bool Pressed => _pressed || Time.unscaledTime < _submitUntil;

        void ResetArtwork()
        {
            _pressAmount = _highlightAmount = 0;
            if (!_visualReady || artwork == null) return;
            artwork.rectTransform.localScale = _artworkScale;
            SetArtworkTint(_artworkTint);
            if (focusRing != null) focusRing.transform.localScale = _ringScale;
        }

        void SetArtworkTint(Color tint)
        {
            // 材质保留大于 1 的提亮值，避免渲染器的顶点颜色截断。
            _feedbackMaterial.SetColor("_Color", tint);
            artwork.materialForRendering.SetColor("_Color", tint);
        }

        void Refresh()
        {
            if (focusRing == null) return;
            focusRing.alpha = Interactable ? (Pressed ? .45f : _selected ? 1f : _hovered ? .65f : 0f) : 0f;
        }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = _pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Interactable) return;
            _pressed = true;
            Refresh();
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = false;
            Refresh();
        }
        public void OnSelect(BaseEventData eventData) { _selected = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { _selected = _pressed = false; _submitUntil = 0; Refresh(); }
        public void OnSubmit(BaseEventData eventData)
        {
            if (!Interactable || !_visualReady) return;
            _submitUntil = Time.unscaledTime + transitionSeconds;
            Refresh();
        }
    }
}
