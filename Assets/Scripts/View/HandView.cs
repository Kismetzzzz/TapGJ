using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 手牌区：屏幕下方那一块（对应设计稿的「手牌区」）。
    /// 每张手牌是一个真实 GameObject（色块底板 + 元素字），可以点击，
    /// 并随屏幕尺寸重新排布，所以不会被右侧 HUD 挤到角落里。
    ///
    /// 点击只负责「选中」，选中之后干什么由 GameRunner 决定：
    /// 出牌阶段 → 再点棋盘目标格；弃牌阶段 → 直接弃掉。
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        public GameEngine Engine;

        /// <summary>参数是牌的 id，交给 GameRunner 去 engine.FindCard。</summary>
        public event System.Action<int> CardClicked;

        readonly List<CardView> _cards = new List<CardView>();
        Font _font;
        float _appliedScale = -1f;

        public int CardObjectCount => _cards.Count;

        /// <summary>当前显示的手牌对象（顺序与逻辑层手牌一致），测试与调试用。</summary>
        public IReadOnlyList<CardView> Cards => _cards;

        public void Setup(GameEngine engine)
        {
            Engine = engine;
            _font = BoardView.LoadFont();

            for (int i = transform.childCount - 1; i >= 0; i--)
                BoardView.Kill(transform.GetChild(i).gameObject);
            _cards.Clear();
            _appliedScale = -1f;

            Sync();
        }

        /// <summary>按屏幕像素矩形摆放整个手牌区。camera 传 null 时用 Camera.main。</summary>
        public void LayoutPixels(float px, float py, float pw, float ph, Camera camera = null)
        {
            if (Engine == null) return;

            var cam = camera != null ? camera : Camera.main;
            if (cam == null) return;

            const float gapWorld = 0.16f;
            const float cardWidth = 1.15f;
            const float cardHeight = 1.62f;

            int count = Mathf.Max(1, _cards.Count);
            float totalWidth = count * cardWidth + (count - 1) * gapWorld;

            // 这个区域在世界坐标里有多宽？装不下就把整排卡片等比缩小
            var leftWorld = cam.ScreenToWorldPoint(new Vector3(px, 0f, -cam.transform.position.z));
            var rightWorld = cam.ScreenToWorldPoint(new Vector3(px + pw, 0f, -cam.transform.position.z));
            float availableWidth = Mathf.Abs(rightWorld.x - leftWorld.x);
            float scale = availableWidth > 0f ? Mathf.Min(1f, availableWidth * 0.92f / totalWidth) : 1f;

            transform.localScale = Vector3.one * scale;

            var worldCenter = cam.ScreenToWorldPoint(
                new Vector3(px + pw * 0.5f, Screen.height - (py + ph * 0.5f), -cam.transform.position.z));
            worldCenter.z = 0f;
            transform.position = worldCenter;

            if (_appliedScale <= 0f || !Mathf.Approximately(_appliedScale, scale))
            {
                _appliedScale = scale;
                foreach (var card in _cards) card.ApplyScale(scale);
            }

            float startX = -totalWidth * 0.5f + cardWidth * 0.5f;
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].transform.localPosition = new Vector3(startX + i * (cardWidth + gapWorld), 0f, 0f);
                _cards[i].SetBaseSize(cardWidth, cardHeight);
            }
        }

        /// <summary>把手牌显示对象与逻辑层对齐（多退少补），顺序也跟逻辑层一致。</summary>
        public void Sync()
        {
            if (Engine == null) return;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                if (Engine.FindCard(_cards[i].CardId) == null)
                {
                    BoardView.Kill(_cards[i].gameObject);
                    _cards.RemoveAt(i);
                }
            }

            foreach (var card in Engine.Hand)
            {
                if (FindView(card.Id) != null) continue;

                var go = new GameObject($"Card_{card.Id}_{ElementDefs.Name(card.Element)}");
                go.transform.SetParent(transform, false);
                var view = go.AddComponent<CardView>();
                view.Build(card.Id, card.Element, _font, OnCardClicked);
                view.ApplyScale(_appliedScale > 0f ? _appliedScale : 1f);
                _cards.Add(view);
            }

            _cards.Sort((a, b) => HandIndexOf(a.CardId).CompareTo(HandIndexOf(b.CardId)));
        }

        int HandIndexOf(int cardId)
        {
            for (int i = 0; i < Engine.Hand.Count; i++)
                if (Engine.Hand[i].Id == cardId) return i;
            return int.MaxValue;
        }

        public CardView FindView(int cardId)
        {
            foreach (var c in _cards)
                if (c.CardId == cardId) return c;
            return null;
        }

        void OnCardClicked(int cardId) => CardClicked?.Invoke(cardId);

        /// <summary>选中态高亮：和 HUD 面板共用同一个 SelectedCardId。</summary>
        public void RefreshSelection(int selectedCardId)
        {
            foreach (var card in _cards)
                card.SetSelected(card.CardId == selectedCardId && selectedCardId > 0);
        }
    }

    /// <summary>一张手牌的显示对象：色块底板 + 元素名 + 选中描边。</summary>
    public sealed class CardView : MonoBehaviour
    {
        public int CardId { get; private set; }
        public Element Element { get; private set; }

        SpriteRenderer _body;
        SpriteRenderer _ring;
        TextMesh _text;
        float _w = 1.15f, _h = 1.62f;

        /// <summary>元素字的世界高度 ≈ (fontSize/10) × glyphScale = 110/10 × 0.10 = 1.1 个「手牌单位」。</summary>
        const int TextFontSize = 110;
        const float TextGlyphScale = 0.10f;

        public void Build(int cardId, Element element, Font font, System.Action<int> onClick)
        {
            CardId = cardId;
            Element = element;

            _body = BoardView.MakeSolid(transform, "body", ElementDefs.Tint(element), 1);
            _ring = BoardView.MakeSolid(transform, "ring", new Color(1f, 1f, 1f, 0f), 2);
            _text = BoardView.MakeText(transform, "text", ElementDefs.Name(element), font,
                new Color(0.06f, 0.06f, 0.08f), TextFontSize, TextGlyphScale, 3);

            var col = gameObject.AddComponent<BoxCollider2D>();
            col.size = new Vector2(_w, _h);

            // 点击由 GameRunner 统一做射线检测后转发过来，避免和棋盘抢输入
            _onClick = onClick;
        }

        System.Action<int> _onClick;

        /// <summary>供 GameRunner 的点击分发调用。</summary>
        public void Click() => _onClick?.Invoke(CardId);

        public void SetBaseSize(float w, float h)
        {
            _w = w;
            _h = h;

            if (_body != null) _body.transform.localScale = new Vector3(_w, _h, 1f);
            if (_ring != null) _ring.transform.localScale = new Vector3(_w * 1.12f, _h * 1.08f, 1f);
            if (_text != null) _text.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            var col = GetComponent<BoxCollider2D>();
            if (col != null) col.size = new Vector2(w, h);
        }

        public void ApplyScale(float scale)
        {
            SetBaseSize(_w, _h);
        }

        public void SetSelected(bool selected)
        {
            if (_ring == null) return;
            _ring.color = selected ? new Color(1f, 0.95f, 0.55f, 1f) : new Color(1f, 1f, 1f, 0f);
        }
    }
}
