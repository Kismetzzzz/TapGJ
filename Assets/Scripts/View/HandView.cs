using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 手牌区：每张手牌是一个真实 GameObject（色块底板 + 元素字），可以点击。
    ///
    /// ⚠ 位置由**场景**决定：整排以**自己的 transform 为中心**沿局部 X 从左到右排，
    /// 不再读屏幕尺寸、不再用 Camera.main。把 HandAnchor 摆到哪儿，手牌就出现在哪儿。
    ///
    /// 点击只负责「选中」或「弃掉」，具体干什么由 GameSession 决定（它订阅 ClickRouter）。
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        [Header("卡牌尺寸（世界单位）")]
        [Tooltip("单张卡牌的宽度")]
        public float CardWidth = 1.15f;

        [Tooltip("单张卡牌的高度")]
        public float CardHeight = 1.62f;

        [Tooltip("卡牌之间的间隙")]
        public float CardGap = 0.16f;

        [Tooltip("整排最多占多宽；手牌太多超出时就整排等比缩小（不换行）")]
        public float RowWidth = 8.0f;

        [Header("外观")]
        [Tooltip("卡牌预制体。留空 = 用代码生成的朴素外观（测试与快速迭代用）；拖了就用美术的预制体")]
        public CardView CardPrefab;
        [Tooltip("使用界面手牌时关闭，避免重复生成世界空间手牌。")]
        public bool ShowCards = true;

        [Header("编辑器预览")]
        [Tooltip("没进 Play 时，Scene 视图里画几张占位牌（只是预览，不进运行时）")]
        public int PreviewCardCount = 5;

        public GameEngine Engine;

        /// <summary>参数是牌的 id，交给 GameSession 去 engine.FindCard。</summary>
        public event System.Action<int> CardClicked;

        readonly List<CardView> _cards = new List<CardView>();
        Font _font;

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

            Sync();
        }

        /// <summary>
        /// 按当前手牌重排：以**自己的 transform 为整排中心**，沿局部 X 从左到右摆。
        /// 总宽超过 RowWidth 时整排等比缩小（不做换行），保证不会压到旁边的东西。
        /// </summary>
        public void Relayout()
        {
            if (Engine == null) return;

            int count = Mathf.Max(1, _cards.Count);
            float step = CardWidth + CardGap;
            float totalWidth = count * CardWidth + (count - 1) * CardGap;

            float scale = 1f;
            if (RowWidth > 0f && totalWidth > RowWidth)
                scale = Mathf.Max(0.05f, RowWidth / totalWidth);

            transform.localScale = Vector3.one * scale;

            float startX = -totalWidth * 0.5f + CardWidth * 0.5f;
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].transform.localPosition = new Vector3(startX + i * step, 0f, 0f);
                _cards[i].SetBaseSize(CardWidth, CardHeight);
            }
        }

        /// <summary>把手牌显示对象与逻辑层对齐（多退少补），顺序也跟逻辑层一致。</summary>
        public float Sync(bool animateRemoval = true)
        {
            if (Engine == null || !ShowCards) return 0;
            float dissolveDuration = 0;

            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                if (Engine.FindCard(_cards[i].CardId) == null)
                {
                    if (Application.isPlaying && animateRemoval
                        && CardDissolveEffect.Play(_cards[i].gameObject, ElementDefs.Tint(_cards[i].Element)) != null)
                        dissolveDuration = CardDissolveEffect.Duration;
                    _cards[i].gameObject.SetActive(false);
                    BoardView.Kill(_cards[i].gameObject);
                    _cards.RemoveAt(i);
                }
            }

            foreach (var card in Engine.Hand)
            {
                if (FindView(card.Id) != null) continue;

                CardView view;
                if (CardPrefab != null)
                {
                    var go = Instantiate(CardPrefab.gameObject, transform, false);
                    go.name = $"Card_{card.Id}_{CardNameFor(card)}";
                    view = go.GetComponent<CardView>();
                    if (view == null) view = go.AddComponent<CardView>();
                }
                else
                {
                    var go = new GameObject($"Card_{card.Id}_{CardNameFor(card)}");
                    go.transform.SetParent(transform, false);
                    view = go.AddComponent<CardView>();
                }

                view.Build(card, _font, OnCardClicked);
                _cards.Add(view);
            }

            _cards.Sort((a, b) => HandIndexOf(a.CardId).CompareTo(HandIndexOf(b.CardId)));
            Relayout();
            return dissolveDuration;
        }

        /// <summary>对象名里用的牌名：元素牌是五行字，秘术牌是「融合」/「分解」。</summary>
        static string CardNameFor(Card card) => card.IsElementCard ? ElementDefs.Name(card.Element) : card.Glyph;

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

        /// <summary>
        /// Scene 视图里把手牌整排的占地和每张牌的位置画出来（选中 HandAnchor 可见）。
        /// 手牌是动态数据（运行时才知道有几张、什么元素），所以不能在场景里放静态对象，
        /// 这里用 Gizmo 让你「没进 Play」也能看到牌会落在哪、整排有多宽。
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (CardWidth <= 0f || CardHeight <= 0f) return;

            int n = Engine != null && Engine.Hand.Count > 0 ? Engine.Hand.Count : Mathf.Max(1, PreviewCardCount);
            float step = CardWidth + CardGap;
            float total = n * CardWidth + (n - 1) * CardGap;
            float scale = (RowWidth > 0f && total > RowWidth) ? Mathf.Max(0.05f, RowWidth / total) : 1f;

            var center = transform.position;
            float cardW = CardWidth * scale;
            float cardH = CardHeight * scale;

            // 整排占地（中空矩形）
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.45f);
            Gizmos.DrawWireCube(center, new Vector3(total * scale, cardH, 0f));

            // 每张牌的位置；Play 里用真实手牌的元素色，编辑里用统一的中性色
            float x0 = -total * 0.5f + CardWidth * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var pos = center + transform.right * ((x0 + i * step) * scale);
                pos.z = center.z;

                Gizmos.color = Engine != null && i < Engine.Hand.Count
                    ? Engine.Hand[i].Tint
                    : new Color(1f, 1f, 1f, 0.22f);
                Gizmos.DrawWireCube(pos, new Vector3(cardW, cardH, 0f));
            }
        }
    }

    /// <summary>
    /// 一张手牌的显示对象：色块底板 + 牌名 + 选中描边。
    ///
    /// 外观可以走两条路，二选一：
    ///   · 序列化引用连好（body / ring / text）→ 用预制体里美术做好的子物体
    ///   · 引用留空 → Build 时用代码生成朴素外观（测试与快速迭代用）
    /// 尺寸始终由 HandView 的 CardWidth/CardHeight 驱动（SetBaseSize），预制体里的外观会被等比缩放。
    ///
    /// 元素牌和秘术牌共用这一个显示对象：区别只在配色和字（元素牌是五行字，融合牌「融合」、分解牌「分解」），
    /// 秘术牌的字缩到 0.62 倍，一眼能看出「这不是元素牌」。
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public int CardId { get; private set; }
        public Element Element { get; private set; }
        /// <summary>这张显示对象画的是哪种牌（元素牌 / 融合牌 / 分解牌）。</summary>
        public CardKind Kind { get; private set; }

        /// <summary>画的是不是秘术牌（融合 / 分解）。</summary>
        public bool IsSpecialCard => Kind != CardKind.ElementCard;

        [Header("外观（留空则 Build 时用代码生成）")]
        [SerializeField] SpriteRenderer body;
        [SerializeField] SpriteRenderer ring;
        [SerializeField] TextMesh text;

        float _w = 1.15f, _h = 1.62f;

        /// <summary>元素字的世界高度 ≈ (fontSize/10) × glyphScale = 110/10 × 0.10 = 1.1 个「手牌单位」。</summary>
        const int TextFontSize = 110;
        const float TextGlyphScale = 0.10f;

        /// <summary>秘术牌是两个字，字形要缩小一点才装得进牌面。</summary>
        const float SpecialGlyphScale = 0.062f;

        /// <summary>
        /// 按一张逻辑层的牌刷新外观。
        /// ⚠ 原来这个方法的签名是 (cardId, element, font, onClick)；改成传 Card 本身，
        /// 因为秘术牌没有牌面元素，读 card.Element 只会拿到占位值。
        /// </summary>
        public void Build(Card card, Font font, System.Action<int> onClick)
        {
            CardId = card.Id;
            Kind = card.Kind;
            Element = card.Element;
            _onClick = onClick;

            var tint = card.Tint;
            var glyph = card.Glyph;
            float glyphScale = card.IsElementCard ? TextGlyphScale : SpecialGlyphScale;

            if (body == null)
                body = BoardView.MakeSolid(transform, "body", tint, 1);
            else
                body.color = tint;

            if (ring == null)
                ring = BoardView.MakeSolid(transform, "ring", new Color(1f, 1f, 1f, 0f), 2);
            else
                ring.color = new Color(1f, 1f, 1f, 0f);

            if (text == null)
            {
                text = BoardView.MakeText(transform, "text", glyph, font,
                    new Color(0.06f, 0.06f, 0.08f), TextFontSize, glyphScale, 3);
            }
            else
            {
                text.text = glyph;
                if (font != null)
                {
                    text.font = font;
                    var mr = text.GetComponent<MeshRenderer>();
                    if (mr != null) mr.material = font.material;
                }
                text.color = new Color(0.06f, 0.06f, 0.08f);
                text.fontSize = TextFontSize;
                text.transform.localScale = Vector3.one * glyphScale;
            }

            var col = GetComponent<BoxCollider2D>();
            if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
            col.size = new Vector2(_w, _h);
        }

        /// <summary>只改选中态时用（外观已经建好了）。</summary>
        public void RefreshVisual()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col != null) col.size = new Vector2(_w, _h);
        }

        System.Action<int> _onClick;

        /// <summary>供 ClickRouter 的点击分发调用。</summary>
        public void Click() => _onClick?.Invoke(CardId);

        public void SetBaseSize(float w, float h)
        {
            _w = w;
            _h = h;

            if (body != null) body.transform.localScale = new Vector3(_w, _h, 1f);
            if (ring != null) ring.transform.localScale = new Vector3(_w * 1.12f, _h * 1.08f, 1f);
            if (text != null) text.transform.localPosition = new Vector3(0f, 0f, -0.05f);

            var col = GetComponent<BoxCollider2D>();
            if (col != null) col.size = new Vector2(w, h);
        }

        public void SetSelected(bool selected)
        {
            if (ring == null) return;
            ring.color = selected ? new Color(1f, 0.95f, 0.55f, 1f) : new Color(1f, 1f, 1f, 0f);
        }
    }
}
