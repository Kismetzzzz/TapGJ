using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>通过独立描边显示鼠标、触摸和键盘状态，保留按钮的美术着色。</summary>
    [DisallowMultipleComponent]
    public sealed class MenuButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] CanvasGroup focusRing;
        Button _button;
        bool _hovered, _selected, _pressed;

        void Awake() => _button = GetComponent<Button>();

        void OnEnable()
        {
            _hovered = _pressed = false;
            _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            Refresh();
        }

        void OnDisable()
        {
            _hovered = _selected = _pressed = false;
            if (focusRing != null) focusRing.alpha = 0;
        }

        void LateUpdate() => Refresh();

        void Refresh()
        {
            if (focusRing == null) return;
            bool interactable = _button != null && _button.IsInteractable();
            focusRing.alpha = interactable ? (_pressed ? .45f : _selected ? 1f : _hovered ? .65f : 0f) : 0f;
        }

        public void OnPointerEnter(PointerEventData eventData) { _hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData eventData) { _hovered = _pressed = false; Refresh(); }
        public void OnPointerDown(PointerEventData eventData) { _pressed = true; Refresh(); }
        public void OnPointerUp(PointerEventData eventData) { _pressed = false; Refresh(); }
        public void OnSelect(BaseEventData eventData) { _selected = true; Refresh(); }
        public void OnDeselect(BaseEventData eventData) { _selected = _pressed = false; Refresh(); }
    }
}
