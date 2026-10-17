using TapGJ.Core;
using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>从共享配置读取整张卡面，保留手牌的选中抬起和描边。</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BattleUiCard : MonoBehaviour
    {
        [SerializeField] Element element;
        [SerializeField] CardKind kind;
        [SerializeField] Button button;
        [SerializeField] RectTransform artwork;
        [SerializeField] CanvasGroup selectedRing;
        [SerializeField] float selectedLift = 28f;
        [Header("卡面（所有手牌共用一个配置）")]
        [SerializeField] BattleCardArt cardArt;
        [SerializeField] Image cardFace;
        [SerializeField] GameObject fallbackArtwork;
        [SerializeField] Image fallbackFrame;
        [SerializeField] Text fallbackLabel;
        Sprite _lastFace;
        Element _lastElement;
        CardKind _lastKind;
        bool _artInitialized;
        Vector2 _poseFrom, _poseTo;
        float _angleFrom, _angleTo, _scaleFrom, _scaleTo, _poseTime;
        int _handOrder;
        bool _poseInitialized, _moving;

        public Element Element => element;
        public int CardId { get; private set; } = -1;
        public CardKind Kind => kind;
        public string Label => kind == CardKind.FusionCard ? "融合牌" :
            kind == CardKind.DecomposeCard ? "分解牌" : ElementDefs.Name(element) + "牌";
        public Button Button => button;
        public bool Selected { get; private set; }
        public BattleCardArt CardArt => cardArt;
        public Image CardFace => cardFace;
        public Vector2 ArtworkSize => artwork.rect.size;
        public float SelectionLift => selectedLift;

        void OnEnable() => RefreshArt();
        void OnValidate() => _artInitialized = false;
        void LateUpdate()
        {
            var face = kind == CardKind.ElementCard && cardArt != null ? cardArt.GetFace(element) : null;
            if (!_artInitialized || face != _lastFace || element != _lastElement || kind != _lastKind) RefreshArt();
            if (_moving)
            {
                _poseTime += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(_poseTime / .2f));
                ApplyPose(Vector2.Lerp(_poseFrom, _poseTo, t), Mathf.LerpAngle(_angleFrom, _angleTo, t),
                    Mathf.Lerp(_scaleFrom, _scaleTo, t));
                if (_poseTime >= .2f) _moving = false;
            }
        }

        public void Bind(Card card)
        {
            CardId = card.Id;
            kind = card.Kind;
            element = card.Element;
            _artInitialized = false;
            RefreshArt();
        }

        public void ClearBinding()
        {
            CardId = -1;
            _poseInitialized = _moving = false;
        }

        public void SetHandPose(Vector2 position, float angle, float scale, int order, bool immediate, float delay = 0)
        {
            var rect = (RectTransform)transform;
            _handOrder = order;
            rect.SetSiblingIndex(order);
            rect.sizeDelta = ArtworkSize;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(.5f, .5f);
            _poseFrom = rect.anchoredPosition;
            _angleFrom = rect.localEulerAngles.z;
            _scaleFrom = rect.localScale.x;
            _poseTo = position;
            _angleTo = angle;
            _scaleTo = scale;
            _poseTime = -delay;
            _moving = !immediate && _poseInitialized;
            _poseInitialized = true;
            if (!_moving) ApplyPose(position, angle, scale);
            if (Selected) transform.SetAsLastSibling();
        }

        void ApplyPose(Vector2 position, float angle, float scale)
        {
            ((RectTransform)transform).anchoredPosition = position;
            transform.localRotation = Quaternion.Euler(0, 0, angle);
            transform.localScale = Vector3.one * scale;
        }

        public void RefreshArt()
        {
            if (cardFace == null || fallbackArtwork == null || fallbackFrame == null
                || fallbackLabel == null || button == null) return;
            _lastFace = kind == CardKind.ElementCard && cardArt != null ? cardArt.GetFace(element) : null;
            _lastElement = element;
            _lastKind = kind;
            _artInitialized = true;
            cardFace.sprite = _lastFace;
            cardFace.gameObject.SetActive(_lastFace != null);
            fallbackArtwork.SetActive(_lastFace == null);
            fallbackLabel.text = kind == CardKind.FusionCard ? "融合" : kind == CardKind.DecomposeCard ? "分解" :
                (int)element >= 0 && (int)element < ElementDefs.Count ? ElementDefs.Name(element) : "?";
            fallbackFrame.color = kind == CardKind.FusionCard ? new Color(.72f, .62f, .86f) :
                kind == CardKind.DecomposeCard ? new Color(.78f, .69f, .59f) : new Color(.78f, .73f, .62f);
            var hint = fallbackArtwork.transform.Find("ArtHint");
            if (hint != null) hint.GetComponent<Text>().text = kind == CardKind.FusionCard ? "选择两个元素\n合成复合元素" :
                kind == CardKind.DecomposeCard ? "选择复合元素\n拆回元素牌" : "卡面待补充";
            button.targetGraphic = _lastFace != null ? cardFace : fallbackFrame;
        }

        public void SetSelected(bool selected)
        {
            if (Selected == selected) return;
            Selected = selected;
            artwork.anchoredPosition = new Vector2(0, selected ? selectedLift : 0);
            selectedRing.alpha = selected ? 1 : 0;
            if (selected) transform.SetAsLastSibling();
            else transform.SetSiblingIndex(_handOrder);
        }

        /// <summary>玩法确认消耗成功后调用；视觉副本独立播完，原槽位立即停止交互。</summary>
        public CardDissolveEffect PlayDissolve()
        {
            if (!gameObject.activeInHierarchy) return null;
            selectedRing.alpha = 0;
            var effect = CardDissolveEffect.Play(artwork.gameObject, ElementDefs.Tint(element));
            SetSelected(false);
            gameObject.SetActive(false);
            return effect;
        }
    }
}
