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
        bool _artInitialized;

        public Element Element => element;
        public Button Button => button;
        public bool Selected { get; private set; }
        public BattleCardArt CardArt => cardArt;
        public Image CardFace => cardFace;

        void OnEnable() => RefreshArt();
        void OnValidate() => _artInitialized = false;
        void LateUpdate()
        {
            var face = cardArt != null ? cardArt.GetFace(element) : null;
            if (!_artInitialized || face != _lastFace || element != _lastElement) RefreshArt();
        }

        public void RefreshArt()
        {
            if (cardFace == null || fallbackArtwork == null || fallbackFrame == null
                || fallbackLabel == null || button == null) return;
            _lastFace = cardArt != null ? cardArt.GetFace(element) : null;
            _lastElement = element;
            _artInitialized = true;
            cardFace.sprite = _lastFace;
            cardFace.gameObject.SetActive(_lastFace != null);
            fallbackArtwork.SetActive(_lastFace == null);
            fallbackLabel.text = (int)element >= 0 && (int)element < ElementDefs.Count ? ElementDefs.Name(element) : "?";
            button.targetGraphic = _lastFace != null ? cardFace : fallbackFrame;
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            artwork.anchoredPosition = new Vector2(0, selected ? selectedLift : 0);
            selectedRing.alpha = selected ? 1 : 0;
        }

        /// <summary>玩法确认消耗成功后调用；视觉副本独立播完，原槽位立即停止交互。</summary>
        public void PlayDissolve()
        {
            if (!gameObject.activeInHierarchy) return;
            selectedRing.alpha = 0;
            CardDissolveEffect.Play(artwork.gameObject, ElementDefs.Tint(element));
            SetSelected(false);
            gameObject.SetActive(false);
        }
    }
}
