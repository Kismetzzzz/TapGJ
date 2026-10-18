using System;
using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.View
{
    /// <summary>不可见逻辑棋盘对齐背景格线；元素、高亮和点击共用同一份校准。</summary>
    public sealed class BattleBoardViewport : RawImage, IPointerClickHandler
    {
        [SerializeField] Camera boardCamera;
        [SerializeField, Range(0, .4f)] float topInset = .085f;
        [Serializable]
        public struct CalibrationRow
        {
            [Range(0, 1)] public float height;
            public float[] columns;

            public CalibrationRow(float height, float[] columns)
            {
                this.height = height;
                this.columns = columns;
            }
        }

        [Header("背景格线校准（由下到上，坐标范围 0–1）")]
        [Tooltip("每条横向格线的高度，以及这条线上从左到右的纵向格线位置。留空时使用原梯形布局。")]
        [SerializeField] CalibrationRow[] calibrationRows = Array.Empty<CalibrationRow>();
        [Tooltip("菱形棋盘四角，依次为逻辑左下、右下、右上、左上；使用容器内的 0–1 坐标。")]
        [SerializeField] Vector2[] calibrationCorners = Array.Empty<Vector2>();
        GameSession _session;
        UiFontTheme _fontTheme;
        RenderTexture _target;
        readonly Dictionary<CreepView, BattleBoardPieceView> _pieces = new Dictionary<CreepView, BattleBoardPieceView>();
        readonly HashSet<CreepView> _livePieces = new HashSet<CreepView>();
        readonly List<CreepView> _stalePieces = new List<CreepView>();
        readonly Vector2[] _pieceCorners = new Vector2[4];

        public void SetCalibration(CalibrationRow[] rows)
        {
            if (!ValidCalibration(rows)) throw new ArgumentException("格线必须在 0–1 内有序排列，且每行列数相同。", nameof(rows));
            calibrationRows = rows;
            calibrationCorners = Array.Empty<Vector2>();
            SetVerticesDirty();
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool ValidCorners(Vector2[] corners)
        {
            if (corners == null || corners.Length != 4) return false;
            for (int i = 0; i < 4; i++)
            {
                var p = corners[i];
                if (!Finite(p.x) || !Finite(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1
                    || Cross(corners[(i + 1) % 4] - p, corners[(i + 2) % 4] - corners[(i + 1) % 4]) <= .000001f)
                    return false;
            }
            return true;
        }

        public void SetCornerCalibration(Vector2[] corners)
        {
            if (!ValidCorners(corners)) throw new ArgumentException("四角必须在 0–1 内按逆时针围成凸四边形。", nameof(corners));
            calibrationCorners = (Vector2[])corners.Clone();
            calibrationRows = Array.Empty<CalibrationRow>();
            SetVerticesDirty();
        }

        void CornerProjection(out Vector2 horizontal, out Vector2 vertical, out Vector2 perspective)
        {
            var a = calibrationCorners[1] - calibrationCorners[2];
            var b = calibrationCorners[3] - calibrationCorners[2];
            var bend = calibrationCorners[0] - calibrationCorners[1] + calibrationCorners[2] - calibrationCorners[3];
            float determinant = Cross(a, b);
            perspective = new Vector2(Cross(bend, b), Cross(a, bend)) / determinant;
            horizontal = calibrationCorners[1] * (1 + perspective.x) - calibrationCorners[0];
            vertical = calibrationCorners[3] * (1 + perspective.y) - calibrationCorners[0];
        }

        Vector2 MapCorners(Vector2 grid)
        {
            CornerProjection(out var horizontal, out var vertical, out var perspective);
            return (calibrationCorners[0] + horizontal * grid.x + vertical * grid.y)
                / (1 + Vector2.Dot(perspective, grid));
        }

        static bool ValidCalibration(CalibrationRow[] rows)
        {
            if (rows == null || rows.Length < 2 || rows[0].columns == null || rows[0].columns.Length < 2) return false;
            int count = rows[0].columns.Length;
            for (int y = 0; y < rows.Length; y++)
            {
                var row = rows[y];
                if (!Finite(row.height) || row.height < 0 || row.height > 1
                    || y > 0 && row.height <= rows[y - 1].height
                    || row.columns == null || row.columns.Length != count) return false;
                for (int x = 0; x < count; x++)
                    if (!Finite(row.columns[x]) || row.columns[x] < 0 || row.columns[x] > 1
                        || x > 0 && row.columns[x] <= row.columns[x - 1]) return false;
            }
            return true;
        }

        public void Setup(GameSession session)
        {
            _session = session;
            _fontTheme = GetComponentInParent<BattleUiController>()?.GetComponentInChildren<UiFontBinding>(true)?.Theme;
            var board = session.Board;
            float width = board.BoardWidthWorld * board.transform.lossyScale.x;
            float height = board.BoardHeightWorld * board.transform.lossyScale.y;
            int textureWidth = Mathf.Clamp(Mathf.RoundToInt(1024 * width / height), 64, 4096);
            if (_target == null || _target.width != textureWidth)
            {
                ReleaseTarget();
                _target = new RenderTexture(textureWidth, 1024, 24, RenderTextureFormat.ARGB32)
                { name = "BattleBoard", antiAliasing = 2 };
                _target.Create();
            }
            texture = _target;
            boardCamera.targetTexture = _target;
            boardCamera.aspect = width / height;
            boardCamera.orthographicSize = height * .5f;
            boardCamera.transform.position = board.BoardCenter + new Vector3(0, 0, -10);
            SetVerticesDirty();
        }

        void LateUpdate()
        {
            if (_session == null || _session.Engine == null) return;
            var board = _session.Board;
            _livePieces.Clear();
            foreach (var source in board.CreepViews)
            {
                if (source == null) continue;
                _livePieces.Add(source);
                if (!_pieces.TryGetValue(source, out var piece))
                {
                    piece = BattleBoardPieceView.Create(rectTransform, source, _fontTheme);
                    _pieces.Add(source, piece);
                }
                var position = board.transform.InverseTransformPoint(source.transform.position);
                var uv = new Vector2(position.x / board.BoardWidthWorld + .5f, position.y / board.BoardHeightWorld + .5f);
                var center = BoardUvToLocal(uv);
                var halfWidth = new Vector2(board.CellSize / board.BoardWidthWorld * .5f, 0);
                var halfHeight = new Vector2(0, board.CellSize / board.BoardHeightWorld * .5f);
                _pieceCorners[0] = BoardUvToLocal(uv - halfWidth - halfHeight);
                _pieceCorners[1] = BoardUvToLocal(uv - halfWidth + halfHeight);
                _pieceCorners[2] = BoardUvToLocal(uv + halfWidth + halfHeight);
                _pieceCorners[3] = BoardUvToLocal(uv + halfWidth - halfHeight);
                piece.Refresh(center, _pieceCorners, source.transform.localScale.x / (board.CellSize * CreepView.BodyScale));
            }
            _stalePieces.Clear();
            foreach (var pair in _pieces)
                if (!_livePieces.Contains(pair.Key)) _stalePieces.Add(pair.Key);
            foreach (var source in _stalePieces)
            {
                // 立即隐藏，避免销毁延迟让已消失或重开的棋子多留一帧。
                _pieces[source].gameObject.SetActive(false);
                BoardView.Kill(_pieces[source].gameObject);
                _pieces.Remove(source);
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            bool corners = ValidCorners(calibrationCorners);
            bool calibrated = corners || ValidCalibration(calibrationRows);
            int columns = corners ? (_session?.Engine?.Board.Width ?? 8) * 4 : calibrated ? (calibrationRows[0].columns.Length - 1) * 4 : 16;
            int rows = corners ? (_session?.Engine?.Board.Height ?? 8) * 4 : calibrated ? (calibrationRows.Length - 1) * 4 : 16;
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    var uv = new Vector2(x / (float)columns, y / (float)rows);
                    if (calibrated)
                    {
                        uv = GridToBoardUv(uv);
                        uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
                    }
                    mesh.AddVert(UvToLocal(uv, rect, calibrated), color,
                        new Vector2(uvRect.x + uv.x * uvRect.width, uvRect.y + uv.y * uvRect.height));
                    if (x == columns || y == rows) continue;
                    int i = y * (columns + 1) + x;
                    mesh.AddTriangle(i, i + columns + 1, i + 1);
                    mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
                }
        }

        /// <summary>棋盘画面中的坐标映射到背景位置，包含格子间隙的半格边距。</summary>
        public Vector2 BoardUvToLocal(Vector2 uv) => UvToLocal(uv, GetPixelAdjustedRect(), ValidCalibration(calibrationRows));

        Vector2 UvToLocal(Vector2 uv, Rect rect, bool calibrated)
        {
            Vector2 mapped;
            if (ValidCorners(calibrationCorners))
            {
                mapped = MapCorners(BoardToGridUv(uv));
            }
            else if (calibrated)
            {
                var grid = BoardToGridUv(uv);
                float row = Mathf.Clamp01(grid.y) * (calibrationRows.Length - 1);
                int y = Mathf.Min(Mathf.FloorToInt(row), calibrationRows.Length - 2);
                float column = Mathf.Clamp01(grid.x) * (calibrationRows[0].columns.Length - 1);
                int x = Mathf.Min(Mathf.FloorToInt(column), calibrationRows[0].columns.Length - 2);
                var lower = calibrationRows[y];
                var upper = calibrationRows[y + 1];
                mapped = new Vector2(Mathf.Lerp(Mathf.Lerp(lower.columns[x], lower.columns[x + 1], column - x),
                        Mathf.Lerp(upper.columns[x], upper.columns[x + 1], column - x), row - y),
                    Mathf.Lerp(lower.height, upper.height, row - y));
            }
            else
            {
                float inset = topInset * uv.y;
                mapped = new Vector2(inset + uv.x * (1 - 2 * inset), uv.y);
            }
            return rect.min + Vector2.Scale(mapped, rect.size);
        }

        Vector2 BoardToGridUv(Vector2 uv)
        {
            if (_session == null || _session.Engine == null) return uv;
            var board = _session.Board;
            float step = board.CellSize + board.CellGap;
            // 格线围绕格子中心排列；外侧也预留半个间隙，避免两端格子偏离背景中心。
            return new Vector2((uv.x * board.BoardWidthWorld + board.CellGap * .5f) / (_session.Engine.Board.Width * step),
                (uv.y * board.BoardHeightWorld + board.CellGap * .5f) / (_session.Engine.Board.Height * step));
        }

        Vector2 GridToBoardUv(Vector2 grid)
        {
            if (_session == null || _session.Engine == null) return grid;
            var board = _session.Board;
            float step = board.CellSize + board.CellGap;
            return new Vector2((grid.x * _session.Engine.Board.Width * step - board.CellGap * .5f) / board.BoardWidthWorld,
                (grid.y * _session.Engine.Board.Height * step - board.CellGap * .5f) / board.BoardHeightWorld);
        }

        public bool TryBoardUvAtLocalPoint(Vector2 local, out Vector2 uv)
        {
            uv = default;
            Rect rect = GetPixelAdjustedRect();
            if (!Finite(local.x) || !Finite(local.y) || rect.width <= 0 || rect.height <= 0) return false;
            var point = new Vector2((local.x - rect.xMin) / rect.width, (local.y - rect.yMin) / rect.height);
            if (ValidCorners(calibrationCorners))
            {
                // 四角透视映射的反算，与实际渲染共用同一组顶点。
                CornerProjection(out var horizontal, out var vertical, out var perspective);
                horizontal -= point * perspective.x;
                vertical -= point * perspective.y;
                float determinant = Cross(horizontal, vertical);
                if (Mathf.Abs(determinant) < .000001f) return false;
                var delta = point - calibrationCorners[0];
                var grid = new Vector2(Cross(delta, vertical), Cross(horizontal, delta)) / determinant;
                const float tolerance = .00001f;
                if (!Finite(grid.x) || !Finite(grid.y) || grid.x < -tolerance || grid.x > 1 + tolerance
                    || grid.y < -tolerance || grid.y > 1 + tolerance || (MapCorners(grid) - point).sqrMagnitude > 1e-10f)
                    return false;
                uv = GridToBoardUv(new Vector2(Mathf.Clamp01(grid.x), Mathf.Clamp01(grid.y)));
                return true;
            }
            if (!ValidCalibration(calibrationRows))
            {
                float inset = topInset * point.y;
                uv = new Vector2((point.x - inset) / (1 - 2 * inset), point.y);
                return Finite(uv.x) && uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
            }
            const float edgeTolerance = .000001f;
            for (int y = 0; y < calibrationRows.Length - 1; y++)
            {
                var lower = calibrationRows[y];
                var upper = calibrationRows[y + 1];
                if (point.y < lower.height - edgeTolerance || point.y > upper.height + edgeTolerance) continue;
                float t = Mathf.Clamp01((point.y - lower.height) / (upper.height - lower.height));
                for (int x = 0; x < lower.columns.Length - 1; x++)
                {
                    float left = Mathf.Lerp(lower.columns[x], upper.columns[x], t);
                    float right = Mathf.Lerp(lower.columns[x + 1], upper.columns[x + 1], t);
                    if (point.x < left - edgeTolerance || point.x > right + edgeTolerance) continue;
                    uv = GridToBoardUv(new Vector2((x + Mathf.Clamp01((point.x - left) / (right - left))) / (lower.columns.Length - 1),
                        (y + t) / (calibrationRows.Length - 1)));
                    return true;
                }
                return false;
            }
            return false;
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || _session == null || _session.Paused
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,
                    data.position, data.pressEventCamera, out var local)) return;
            if (TryCellAtLocalPoint(local, out var cell)) _session.PlaySelectedCardAt(cell);
        }

        public bool TryCellAtLocalPoint(Vector2 local, out Pos cell)
        {
            cell = Pos.None;
            if (_session == null || _session.Engine == null) return false;
            if (!TryBoardUvAtLocalPoint(local, out var uv)) return false;
            var board = _session.Board;
            return TryCellAtUv(uv, _session.Engine.Board.Width, _session.Engine.Board.Height,
                board.CellSize, board.CellGap, out cell);
        }

        public static bool TryCellAtUv(Vector2 uv, int columns, int rows, float size, float gap, out Pos cell)
        {
            cell = Pos.None;
            if (!Finite(uv.x) || !Finite(uv.y) || uv.x < 0 || uv.y < 0 || uv.x >= 1 || uv.y >= 1
                || columns <= 0 || rows <= 0 || !Finite(size) || !Finite(gap) || size <= 0 || gap < 0) return false;
            float x = uv.x * (columns * size + (columns - 1) * gap);
            float y = uv.y * (rows * size + (rows - 1) * gap);
            int column = Mathf.FloorToInt(x / (size + gap));
            int row = Mathf.FloorToInt(y / (size + gap));
            if (column >= columns || row >= rows || x - column * (size + gap) > size || y - row * (size + gap) > size)
                return false;
            cell = new Pos(column, row);
            return true;
        }

        void ReleaseTarget()
        {
            if (boardCamera != null && boardCamera.targetTexture == _target) boardCamera.targetTexture = null;
            texture = null;
            if (_target == null) return;
            _target.Release();
            BoardView.Kill(_target);
            _target = null;
        }

        protected override void OnDestroy()
        {
            ReleaseTarget();
            base.OnDestroy();
        }
    }
}
