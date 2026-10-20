using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>只延展题字与端部之间的底纹，让按钮等大而不拉伸题字和圆角。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Image))]
    public sealed class ActionButtonArtwork : BaseMeshEffect
    {
        // ponytail: 切分按现有三张插画的题字与端部留白标定；替换素材时需重新核对。
        static readonly float[] SourceCuts = { 0, .18f, .20f, .80f, .82f, 1 };
        readonly float[] _positions = new float[6];
        readonly UIVertex[] _quad = new UIVertex[4];

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || mesh.currentVertCount == 0) return;
            var image = (Image)graphic;
            var sprite = image.sprite;
            if (sprite == null) return;
            var rect = image.GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float aspect = sprite.rect.width / sprite.rect.height;
            float height = Mathf.Min(rect.height, rect.width / (aspect * .96f));
            float nativeWidth = height * aspect;
            _positions[0] = rect.xMin;
            _positions[1] = rect.xMin + nativeWidth * .18f;
            _positions[2] = rect.center.x - nativeWidth * .30f;
            _positions[3] = rect.center.x + nativeWidth * .30f;
            _positions[4] = rect.xMax - nativeWidth * .18f;
            _positions[5] = rect.xMax;
            var vertex = UIVertex.simpleVert;
            mesh.PopulateUIVertex(ref vertex, 0);
            var tint = vertex.color;
            var uv = DataUtility.GetOuterUV(sprite);
            mesh.Clear();
            for (int section = 0; section < 5; section++)
            {
                for (int i = 0; i < 4; i++)
                {
                    bool right = i >= 2;
                    bool top = i == 1 || i == 2;
                    _quad[i] = UIVertex.simpleVert;
                    _quad[i].color = tint;
                    _quad[i].position = new Vector3(_positions[section + (right ? 1 : 0)],
                        rect.center.y + (top ? height : -height) * .5f);
                    _quad[i].uv0 = new Vector2(Mathf.Lerp(uv.x, uv.z, SourceCuts[section + (right ? 1 : 0)]),
                        top ? uv.w : uv.y);
                }
                mesh.AddUIVertexQuad(_quad);
            }
        }
    }
}
