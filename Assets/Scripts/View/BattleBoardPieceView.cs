using UnityEngine;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>动物棋子保持直立比例，元素色块贴合棋盘透视；动画跟随原有表现对象。</summary>
    public sealed class BattleBoardPieceView : MaskableGraphic
    {
        CreepView _source;
        RectTransform _rect;
        Text _label;
        Image _artwork;
        readonly Vector2[] _corners = new Vector2[4];
        readonly UIVertex[] _quad = new UIVertex[4];

        public static BattleBoardPieceView Create(RectTransform parent, CreepView source, UiFontTheme fontTheme)
        {
            var root = new GameObject("DiamondPiece_" + source.Uid, typeof(RectTransform), typeof(CanvasRenderer));
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            var piece = root.AddComponent<BattleBoardPieceView>();
            piece.raycastTarget = false;
            piece._source = source;
            piece._rect = (RectTransform)root.transform;
            piece._rect.anchorMin = piece._rect.anchorMax = parent.pivot;
            var artwork = new GameObject("Artwork", typeof(RectTransform), typeof(Image));
            artwork.layer = root.layer;
            artwork.transform.SetParent(root.transform, false);
            piece._artwork = artwork.GetComponent<Image>();
            piece._artwork.preserveAspect = true;
            piece._artwork.raycastTarget = false;
            artwork.SetActive(false);
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.layer = root.layer;
            label.transform.SetParent(root.transform, false);
            piece._label = label.GetComponent<Text>();
            piece._label.font = source.Label.font;
            piece._label.alignment = TextAnchor.MiddleCenter;
            piece._label.raycastTarget = false;
            piece._label.supportRichText = false;
            piece._label.resizeTextForBestFit = true;
            piece._label.resizeTextMinSize = 8;
            label.AddComponent<UiFontBinding>().Configure(fontTheme, UiFontRole.Body);
            return piece;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_source == null || _source.HasArtwork) return;
            if (_source.Ring != null && _source.Ring.enabled) AddQuad(mesh, _source.Ring.color, 1.08f);
            AddQuad(mesh, color, 1);
        }

        void AddQuad(VertexHelper mesh, Color tint, float scale)
        {
            for (int i = 0; i < 4; i++)
            {
                _quad[i] = UIVertex.simpleVert;
                _quad[i].position = _corners[i] * scale;
                _quad[i].color = tint;
            }
            mesh.AddUIVertexQuad(_quad);
        }

        public void Refresh(Vector2 position, Vector2[] corners, float animationScale)
        {
            _rect.anchoredPosition = position;
            _rect.localScale = Vector3.one * animationScale;
            var min = corners[0];
            var max = corners[0];
            for (int i = 0; i < 4; i++)
            {
                _corners[i] = corners[i] - position;
                min = Vector2.Min(min, corners[i]);
                max = Vector2.Max(max, corners[i]);
            }
            var size = max - min;
            _rect.sizeDelta = size;
            color = _source.Body.color;
            _artwork.gameObject.SetActive(_source.HasArtwork);
            _artwork.sprite = _source.HasArtwork ? _source.Body.sprite : null;
            _artwork.color = color;
            // 图片使用独立的正向平面，避免随地面四角变成扁平菱形。
            float side = Mathf.Min(Vector2.Distance(corners[0], corners[1]),
                Vector2.Distance(corners[0], corners[3])) * .95f;
            // 复合元素先取格内最大直立正方形，弹出时收回，避免动画再额外放大。
            if (_source.IsComposite)
            {
                side = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    var edge = _corners[(i + 1) % 4] - _corners[i];
                    float distance = Mathf.Abs(edge.x * _corners[i].y - edge.y * _corners[i].x);
                    side = Mathf.Min(side, 2 * distance / Mathf.Max(.001f, Mathf.Abs(edge.x) + Mathf.Abs(edge.y)));
                }
                side *= .98f / Mathf.Max(1, animationScale);
            }
            // 在贴合格线的尺寸上再大约两成，便于辨认；不按格子长边铺满。
            side *= 1.2f;
            _artwork.rectTransform.sizeDelta = Vector2.one * side;
            _label.gameObject.SetActive(!_source.HasArtwork);
            _label.rectTransform.sizeDelta = new Vector2(size.x * .55f, size.y * .75f);
            _label.text = _source.Label.text;
            _label.fontStyle = _source.Label.fontStyle;
            _label.color = _source.Label.color;
            _label.resizeTextMaxSize = Mathf.Max(8, Mathf.RoundToInt(size.y * .65f));
            SetOriginalRendering(false);
            SetVerticesDirty();
        }

        void SetOriginalRendering(bool visible)
        {
            if (_source == null) return;
            _source.Body.forceRenderingOff = !visible;
            _source.Label.GetComponent<Renderer>().forceRenderingOff = !visible;
            if (_source.Ring != null) _source.Ring.forceRenderingOff = !visible;
        }

        protected override void OnDisable()
        {
            SetOriginalRendering(true);
            base.OnDisable();
        }
    }
}
