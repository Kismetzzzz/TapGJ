using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 棋盘表现层：把 GameEngine 的状态画成 2D 网格，并把点击换算成格子坐标。
    /// 完全由代码生成（程序化贴图 + 内置字体），所以不需要任何美术资源或预制体。
    ///
    /// ⚠ 对象所有权只有两份，不要混：
    ///   CellView  —— 格子自己的底板 / 高亮 / 坐标角标，**静态**，一辈子跟着格子不动。
    ///   CreepView —— 一只小怪的色块 + 元素字，**全部**挂在 _creepRoot 下，位置用局部坐标。
    ///   以前小怪的表现对象一半在格子里、一半在 _creepRoot 下，两套并存导致
    ///   「格子有色块没字」（孤儿对象没人销毁）和「方块飞出棋盘」（世界坐标不跟棋盘走）。
    ///   现在 Sync() 是唯一入口：以引擎为准双向对账，多退少补。
    ///
    /// ⚠ 这个组件会自己生成 64 个格子：不要手工往它下面塞格子物体，
    /// 运行时 Setup() 会先清空子物体再重建（想改格子外观请改 CellView）。
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        public const float Cell = 1.0f;
        public const float Gap = 0.10f;

        public GameEngine Engine;

        readonly List<CellView> _cells = new List<CellView>();
        readonly Dictionary<int, CellView> _cellByKey = new Dictionary<int, CellView>();

        /// <summary>小怪表现对象的唯一归属：uid → Creep_{uid}（挂在 _creepRoot 下）。</summary>
        readonly Dictionary<int, GameObject> _creeps = new Dictionary<int, GameObject>();

        /// <summary>正在播动画的小怪：这期间 Sync 不许重建 / 挪动 / 销毁它。</summary>
        readonly HashSet<int> _frozen = new HashSet<int>();

        readonly HashSet<int> _live = new HashSet<int>();
        readonly List<int> _stale = new List<int>();

        GameObject _cellRoot;
        GameObject _creepRoot;
        bool _selectedCellDirty;
        Pos _selectedCell = Pos.None;
        Font _font;

        static Font _cachedFont;

        /// <summary>
        /// 内置字体：Unity 2022+ 是 LegacyRuntime.ttf，更早的版本是 Arial.ttf。
        /// 两者都走系统字体回退，所以中文可以正常显示。
        /// </summary>
        public static Font LoadFont()
        {
            if (_cachedFont != null) return _cachedFont;
            _cachedFont = TryLoadFont("LegacyRuntime.ttf") ?? TryLoadFont("Arial.ttf");
            return _cachedFont;
        }

        static Font TryLoadFont(string name)
        {
            try { return Resources.GetBuiltinResource<Font>(name); }
            catch { return null; }
        }

        /// <summary>
        /// 销毁对象：编辑模式下必须用 DestroyImmediate，否则 Unity 会报错。
        /// 表现层因此也能在 EditMode 测试里搭起来并验证。
        /// </summary>
        public static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        public float BoardWidthWorld => Engine.Board.Width * Cell + (Engine.Board.Width - 1) * Gap;
        public float BoardHeightWorld => Engine.Board.Height * Cell + (Engine.Board.Height - 1) * Gap;
        public Vector3 BoardCenter => _cellRoot != null ? _cellRoot.transform.position : Vector3.zero;

        public event System.Action<Pos> CellClicked;

        /// <summary>由 GameRunner 控制：动画播放中 / 过场中时禁止点选。</summary>
        public bool InteractionEnabled = true;

        public void Setup(GameEngine engine)
        {
            Engine = engine;
            _font = LoadFont();

            // 换引擎/重开局时清掉旧的表现对象，避免残留。
            // 这里递归销毁子物体，所以格子里就算真留了孤儿对象也会被一起清掉。
            for (int i = transform.childCount - 1; i >= 0; i--)
                Kill(transform.GetChild(i).gameObject);

            _cells.Clear();
            _cellByKey.Clear();
            _creeps.Clear();
            _frozen.Clear();
            _selectedCell = Pos.None;
            _appliedScale = -1f; // 格子是新建的（坐标角标默认隐藏），下次布局必须重刷一次

            Build();
        }

        /// <summary>
        /// 按「屏幕比例」的矩形摆放棋盘：内部网格与文字都按这个框等比缩放。
        /// 这样棋盘永远落在规划好的区域内，不会因为铺满半个屏幕而把格子糊得很大。
        /// fitScale = 棋盘占这个框的比例（留一点边距，免得贴边）。
        /// </summary>
        public void Layout(Rect screenFraction, float fitScale = 0.92f)
        {
            float vx = screenFraction.x * Screen.width;
            float vy = screenFraction.y * Screen.height;
            float vw = screenFraction.width * Screen.width;
            float vh = screenFraction.height * Screen.height;
            LayoutPixels(vx, vy, vw, vh, fitScale);
        }

        /// <summary>
        /// 按屏幕像素矩形摆放棋盘。
        /// fitToZone = true：把棋盘等比缩放到正好装进这个框（自包含，适合测试或单用）。
        /// fitToZone = false：只把这个框的中心对准棋盘中心、缩放保持 1（棋盘尺寸交给相机控制）。
        /// camera 传 null 时用 Camera.main。
        /// </summary>
        public void LayoutPixels(float px, float py, float pw, float ph, float fitScale = 0.92f,
            Camera camera = null, bool fitToZone = true)
        {
            if (Engine == null) return;

            var cam = camera != null ? camera : Camera.main;
            if (cam == null) return;

            float scale = 1f;
            if (fitToZone)
            {
                float bw = BoardWidthWorld;
                float bh = BoardHeightWorld;
                if (bw <= 0f || bh <= 0f) return;

                // 取「能放进这个框」的最大缩放：同时受宽高限制，取更小的那个
                float margin = Mathf.Clamp(fitScale, 0.3f, 1f);
                scale = Mathf.Min(pw * margin / bw, ph * margin / bh);
                if (scale <= 0f) return;
            }

            transform.localScale = Vector3.one * scale;

            // 框中心 → 世界坐标，再把棋盘中心对上去
            var worldCenter = cam.ScreenToWorldPoint(
                new Vector3(px + pw * 0.5f, Screen.height - (py + ph * 0.5f), -cam.transform.position.z));
            worldCenter.z = 0f;
            transform.position = worldCenter;

            ApplyCellArt(scale);
        }

        float _appliedScale = -1f;

        /// <summary>直接指定棋盘缩放（GameRunner 用相机控制尺寸时调它保持 scale = 1）。</summary>
        public void ApplyLayoutScale(float scale)
        {
            if (scale <= 0f) return;
            transform.localScale = Vector3.one * scale;
            ApplyCellArt(scale);
        }

        /// <summary>
        /// 按当前缩放调整格子的精细度：格子变小的时候，坐标角标要跟着隐藏，
        /// 否则就会出现「文字比格子还大、糊满屏幕」的情况。
        /// 元素字不用管 —— 它挂在小怪下面，缩放天然跟着棋盘走。
        /// </summary>
        void ApplyCellArt(float scale)
        {
            if (_appliedScale > 0f && Mathf.Approximately(_appliedScale, scale)) return;
            _appliedScale = scale;

            bool showCoords = scale > 0.55f; // 棋盘被缩得太小时就别显示坐标了
            foreach (var cell in _cells)
                cell.ApplyScale(showCoords);
        }

        public static Vector3 CellWorldPos(Pos p, int width, int height)
        {
            float w = width * Cell + (width - 1) * Gap;
            float h = height * Cell + (height - 1) * Gap;
            float x = -w * 0.5f + Cell * 0.5f + p.X * (Cell + Gap);
            float y = -h * 0.5f + Cell * 0.5f + p.Y * (Cell + Gap);
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// 格子在棋盘**局部空间**里的位置。
        /// _cellRoot / _creepRoot 的 localPosition 都是零，所以小怪用这个值当 localPosition，
        /// 就和格子严丝合缝地对齐，而且棋盘被挪动/缩放时小怪会自动跟着走。
        /// </summary>
        public Vector3 CellLocalPos(Pos p) => CellWorldPos(p, Engine.Board.Width, Engine.Board.Height);

        void Build()
        {
            _cellRoot = new GameObject("Cells");
            _cellRoot.transform.SetParent(transform, false);
            _creepRoot = new GameObject("Creeps");
            _creepRoot.transform.SetParent(transform, false);

            for (int y = 0; y < Engine.Board.Height; y++)
            {
                for (int x = 0; x < Engine.Board.Width; x++)
                {
                    var pos = new Pos(x, y);
                    var cell = new GameObject($"Cell_{x}_{y}");
                    cell.transform.SetParent(_cellRoot.transform, false);
                    cell.transform.localPosition = CellLocalPos(pos);

                    var view = cell.AddComponent<CellView>();
                    view.Build(pos, _font);
                    _cells.Add(view);
                    _cellByKey[pos.Y * Engine.Board.Width + pos.X] = view;
                }
            }
        }

        void OnCellClicked(Pos pos)
        {
            _selectedCell = pos;
            _selectedCellDirty = true;
            CellClicked?.Invoke(pos);
        }

        public void SelectCell(Pos pos)
        {
            _selectedCell = pos;
            _selectedCellDirty = true;
        }

        // ---------------------------------------------------------------- 状态同步

        /// <summary>
        /// 把小怪表现层和逻辑层对账（唯一入口）：
        ///   一、引擎里有的小怪 —— 确保恰好一个表现对象，并且站在自己的格子上；
        ///   二、引擎里已经没有的小怪 —— 销毁表现对象（以前这里漏了，色块就留在格子上）。
        /// 格子完全由静态元素组成，不参与小怪显示，所以不会再有「有色无字」的残影。
        /// </summary>
        public void Sync()
        {
            if (Engine == null) return;

            // 一、补齐 + 归位
            _live.Clear();
            foreach (var creep in Engine.Creeps)
            {
                _live.Add(creep.Uid);
                if (_frozen.Contains(creep.Uid)) continue; // 动画在放，别插手

                var view = EnsureCreepView(creep.Uid);
                view.Show(creep);
                view.transform.localPosition = CellLocalPos(creep.Pos);
            }

            // 二、清掉引擎里已经没有的
            _stale.Clear();
            foreach (var kv in _creeps)
                if (!_live.Contains(kv.Key) && !_frozen.Contains(kv.Key)) _stale.Add(kv.Key);

            for (int i = 0; i < _stale.Count; i++)
            {
                Kill(_creeps[_stale[i]]);
                _creeps.Remove(_stale[i]);
            }
        }

        /// <summary>一个 uid 对应恰好一个表现对象；已经有了就复用，不会有第二份。</summary>
        CreepView EnsureCreepView(int uid)
        {
            if (_creeps.TryGetValue(uid, out var go) && go != null)
            {
                var existing = go.GetComponent<CreepView>();
                if (existing != null) return existing;
            }

            if (go != null) Kill(go); // 引用还在但组件没了：当脏数据处理

            go = new GameObject($"Creep_{uid}");
            go.transform.SetParent(_creepRoot.transform, false);
            go.transform.localScale = Vector3.one * CreepView.BodyScale;
            var view = go.AddComponent<CreepView>();
            view.Build(uid, _font);
            _creeps[uid] = go;
            return view;
        }

        public GameObject CreepGo(int uid) => _creeps.TryGetValue(uid, out var go) ? go : null;

        public CreepView CreepViewOf(int uid)
        {
            var go = CreepGo(uid);
            return go == null ? null : go.GetComponent<CreepView>();
        }

        /// <summary>格子上的小怪表现对象；该格空着就返回 null。</summary>
        public CreepView CreepViewAt(Pos pos)
        {
            var creep = Engine.Board.At(pos);
            return creep == null ? null : CreepViewOf(creep.Uid);
        }

        /// <summary>场上小怪表现对象的数量（不变量：必须等于 Engine.CreepCount）。</summary>
        public int CreepObjectCount => _creeps.Count;

        public IEnumerable<CreepView> CreepViews
        {
            get
            {
                foreach (var kv in _creeps)
                    if (kv.Value != null) yield return kv.Value.GetComponent<CreepView>();
            }
        }

        static Sprite _solidSprite;
        static Sprite _boxSprite;

        /// <summary>
        /// 1x1 白色 sprite，靠 SpriteRenderer.color 上色，全项目共用一张贴图。
        /// 世界尺寸 = 1 像素 / pixelsPerUnit = 1 个单位。
        /// </summary>
        public static Sprite SpriteOf(Color color)
        {
            if (_solidSprite != null) return _solidSprite;

            _solidSprite = Sprite.Create(ElementDefs.White, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _solidSprite.name = "solid";
            _solidSprite.hideFlags = HideFlags.HideAndDontSave;
            return _solidSprite;
        }

        /// <summary>
        /// 格子底图：深色方格 + 浅色描边。
        /// 贴图直接按格子尺寸生成，pixelsPerUnit = 贴图边长，于是世界尺寸正好是 1×1 个单位，
        /// 也就不需要九宫格缩放（Sprite.Create 带 border 的重载在 2022 上参数顺序容易踩坑）。
        /// </summary>
        public static Sprite BoxSprite()
        {
            if (_boxSprite != null) return _boxSprite;

            const int size = 256;
            const int border = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var fill = new Color(1f, 1f, 1f, 0.10f);
            var edge = new Color(1f, 1f, 1f, 0.34f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isEdge = x < border || y < border || x >= size - border || y >= size - border;
                    tex.SetPixel(x, y, isEdge ? edge : fill);
                }
            }
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            _boxSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _boxSprite.name = "cellbox";
            _boxSprite.hideFlags = HideFlags.HideAndDontSave;
            return _boxSprite;
        }

        /// <summary>在父节点下做一个 1×1 单位的格子底板（高亮层也用它，靠颜色区分）。</summary>
        public static SpriteRenderer MakeBox(Transform parent, string name, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BoxSprite();
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>纯色铺块：进度条的底与填充、色块都走这个。</summary>
        public static SpriteRenderer MakeSolid(Transform parent, string name, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteOf(color);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// 造一个 TextMesh。注意：TextMesh 的实际字高 ≈ fontSize/10 × characterSize × lossyScale，
        /// 所以这里的 scale 是「字形缩放」，不是字号。
        /// </summary>
        public static TextMesh MakeText(Transform parent, string name, string text, Font font, Color color,
            int fontSize, float glyphScale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            go.transform.localScale = Vector3.one * glyphScale;

            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            var mr = tm.GetComponent<MeshRenderer>();
            if (font != null) mr.material = font.material;
            tm.text = text;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = fontSize;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            tm.characterSize = 1f;
            mr.sortingOrder = order;
            return tm;
        }

        // ---------------------------------------------------------------- 动画

        /// <summary>
        /// 播放一个步骤的所有事件，全部播完才返回。
        /// 每个事件涉及的小怪在播放期间会被「冻结」（见 _frozen）：
        /// 引擎里记的是这一步**结算完**的状态，Sync 要是在动画中间插手，
        /// 就会把小怪瞬间拽到终点 / 把刚消失的又画出来。
        /// </summary>
        public IEnumerator<object> PlayStep(StepRecord step, float stepDuration)
        {
            if (step == null) yield break;

            bool pathPlayed = false;
            for (int i = 0; i < step.Events.Count; i++)
            {
                var e = step.Events[i];
                int uid = e.Uid != 0 ? e.Uid : step.Uid;
                bool freeze = uid != 0 && NeedsFreeze(e.Type);
                if (freeze) _frozen.Add(uid);

                switch (e.Type)
                {
                    case GameEventType.Move:
                        // 一步走 N 格会在 record 里留下 N 条 Move 事件，但 Path 是完整路径：
                        // 只播一次，否则小怪会把同一条路来回走 N 遍。
                        if (!pathPlayed)
                        {
                            pathPlayed = true;
                            yield return PlayMove(step, stepDuration);
                        }
                        break;
                    case GameEventType.Bump:
                        yield return PlayBump(e);
                        break;
                    case GameEventType.Transform:
                        // 相生：小怪换了元素。闪一下格子，动画播完 GameRunner 会 Sync 一次，
                        // 颜色和元素字就跟着换过来了（uid 没变，所以是同一个对象换皮）。
                        yield return PlayFlash(e.From, new Color(0.75f, 0.95f, 1f), 0.20f);
                        break;
                    case GameEventType.Vanish:
                        yield return PlayVanish(e, 0.16f);
                        break;
                    case GameEventType.Spawn:
                        yield return PlaySpawn(e, 0.16f);
                        break;
                    case GameEventType.SpawnBlocked:
                        yield return PlayFlash(e.From, new Color(1f, 0.4f, 0.3f), 0.14f);
                        break;
                }

                if (freeze) _frozen.Remove(uid);
            }

            _frozen.Clear(); // 兜底：一个步骤播完，动画期一定结束
        }

        static bool NeedsFreeze(GameEventType type)
        {
            switch (type)
            {
                case GameEventType.Move:
                case GameEventType.Bump:
                case GameEventType.Transform:
                case GameEventType.Vanish:
                case GameEventType.Spawn:
                    return true;
                default:
                    return false;
            }
        }

        CellView CellAt(Pos pos)
        {
            if (pos.X < 0 || pos.Y < 0 || pos.X >= Engine.Board.Width || pos.Y >= Engine.Board.Height) return null;
            return _cellByKey.TryGetValue(pos.Y * Engine.Board.Width + pos.X, out var v) ? v : null;
        }

        IEnumerator<object> PlayMove(StepRecord step, float duration)
        {
            var go = CreepGo(step.Uid);
            if (go == null || step.Path.Count < 2) yield break;

            for (int i = 1; i < step.Path.Count; i++)
            {
                var from = CellLocalPos(step.Path[i - 1]);
                var to = CellLocalPos(step.Path[i]);
                float t = 0f;
                while (t < duration)
                {
                    t += Time.deltaTime;
                    go.transform.localPosition = Vector3.Lerp(from, to, Mathf.Clamp01(t / duration));
                    yield return null;
                }
                go.transform.localPosition = to;
            }
        }

        IEnumerator<object> PlayBump(GameEvent e)
        {
            var go = CreepGo(e.Uid);
            if (go == null) yield break;

            var from = CellLocalPos(e.From);
            var toward = CellLocalPos(e.To);
            float t = 0f;
            const float duration = 0.18f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI) * 0.3f;
                go.transform.localPosition = Vector3.Lerp(from, toward, k);
                yield return null;
            }
            go.transform.localPosition = from;
        }

        /// <summary>
        /// 消失：把整只小怪（色块 + 元素字）一起缩小到 0 再销毁。
        /// 字是它的子物体，所以会自动跟着缩 —— 不会出现「色块没了字还在」。
        /// </summary>
        IEnumerator<object> PlayVanish(GameEvent e, float duration)
        {
            var go = CreepGo(e.Uid);
            var view = CellAt(e.From);
            if (view != null && view.Highlight != null)
                view.Highlight.color = new Color(1f, 1f, 1f, 0.35f);

            float t = 0f;
            var start = go != null ? go.transform.localScale : Vector3.one;
            while (go != null && t < duration)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / duration);
                go.transform.localScale = start * k;
                yield return null;
            }

            if (go != null) Kill(go);
            _creeps.Remove(e.Uid);

            if (view != null && view.Highlight != null) view.Highlight.color = new Color(1f, 1f, 1f, 0f);
        }

        IEnumerator<object> PlaySpawn(GameEvent e, float duration)
        {
            var creep = FindCreep(e.Uid);
            if (creep == null) yield break;

            var view = EnsureCreepView(creep.Uid);
            view.Show(creep);
            view.transform.localPosition = CellLocalPos(e.To);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                view.transform.localScale = Vector3.one * CreepView.BodyScale * k;
                yield return null;
            }
            view.transform.localScale = Vector3.one * CreepView.BodyScale;
        }

        IEnumerator<object> PlayFlash(Pos pos, Color color, float duration)
        {
            var view = CellAt(pos);
            if (view == null || view.Highlight == null) yield break;

            view.Highlight.color = color;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                yield return null;
            }
            view.Highlight.color = new Color(1f, 1f, 1f, 0f);
        }

        Creep FindCreep(int uid)
        {
            foreach (var c in Engine.Creeps)
                if (c.Uid == uid) return c;
            return null;
        }

        /// <summary>把所有小怪瞬间归位（跳过动画、重开局时用）。</summary>
        public void SnapAll()
        {
            _frozen.Clear();
            foreach (var kv in _creeps)
                if (kv.Value != null) Kill(kv.Value);
            _creeps.Clear();

            foreach (var view in _cells)
            {
                if (view.Highlight != null) view.Highlight.color = new Color(1f, 1f, 1f, 0f);
            }

            Sync();
        }

        // ------------------------------------------------------------ 点击与高亮

        /// <summary>
        /// 点击格子的入口。射线检测由 GameRunner 统一做（它要同时区分手牌 / 格子 / HUD），
        /// 所以这里不再自己读 Input，避免一处点击被两边各处理一次。
        /// </summary>
        public void NotifyCellClicked(Pos pos)
        {
            if (Engine == null || !InteractionEnabled) return;
            OnCellClicked(pos);
        }

        /// <summary>玩家选中的牌：能作用于目标格时高亮，帮助测试时看清规则。</summary>
        public void RefreshHighlights(Card selectedCard)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                var view = _cells[i];
                var creep = Engine.Board.At(view.Pos);
                Color color;

                if (view.Pos == _selectedCell) color = new Color(1f, 0.85f, 0.3f, 0.30f);
                else if (selectedCard != null && creep != null && Engine.CanPlayCardOn(selectedCard, creep))
                    color = new Color(0.4f, 1f, 0.5f, 0.22f);
                else color = new Color(1f, 1f, 1f, 0f);

                view.SetHighlight(color);
            }
            _selectedCellDirty = false;
        }

        public bool SelectedCellDirty => _selectedCellDirty;
        public Pos SelectedCell => _selectedCell;
    }

    /// <summary>
    /// 单个格子的显示对象：只有静态的底板 / 高亮层 / 坐标角标。
    /// 它**不**拥有小怪 —— 小怪归 BoardView 的 _creepRoot，这样格子永远不会留下残影。
    /// </summary>
    public sealed class CellView : MonoBehaviour
    {
        public Pos Pos;

        SpriteRenderer _background;
        SpriteRenderer _highlight;
        TextMesh _coord;

        const int CoordFontSize = 30;
        const float CoordGlyphScale = 0.045f;

        public SpriteRenderer Highlight => _highlight;

        public void Build(Pos pos, Font font)
        {
            Pos = pos;

            // 格子底板：1×1 单位，正好一格
            _background = BoardView.MakeBox(transform, "bg", new Color(0.16f, 0.18f, 0.22f), 0);
            var col = gameObject.AddComponent<BoxCollider2D>();
            col.size = Vector2.one * BoardView.Cell;

            // 高亮层：默认全透明，鼠标/选牌时上色
            _highlight = BoardView.MakeBox(transform, "highlight", new Color(1f, 1f, 1f, 0f), 3);
            _highlight.color = new Color(1f, 1f, 1f, 0f);

            // 坐标角标：默认隐藏，只有棋盘放得够大时才显示（见 ApplyScale）
            _coord = BoardView.MakeText(transform, "coord", $"{pos.X},{pos.Y}", font,
                new Color(1f, 1f, 1f, 0.28f), CoordFontSize, CoordGlyphScale, 1);
            _coord.transform.localPosition = new Vector3(0.30f, -0.33f, -0.02f);
            _coord.gameObject.SetActive(false);
        }

        /// <summary>棋盘被缩得太小时就别显示坐标角标了。</summary>
        public void ApplyScale(bool showCoords)
        {
            if (_coord == null) return;
            if (_coord.gameObject.activeSelf != showCoords) _coord.gameObject.SetActive(showCoords);
        }

        public void SetHighlight(Color c)
        {
            if (_highlight != null) _highlight.color = c;
        }
    }

    /// <summary>
    /// 一只小怪的显示对象：色块底板 + 元素字。
    /// 元素字是**子物体**，所以色块在哪字就在哪，移动 / 生成 / 消失的缩放动画也自动联动 ——
    /// 不会再出现「色块还在字没了」或者「字还在色块没了」这种两边不同步的情况。
    /// </summary>
    public sealed class CreepView : MonoBehaviour
    {
        /// <summary>色块相对格子的比例（1 格 = 1 单位）。</summary>
        public const float BodyScale = 0.72f;

        const int LabelFontSize = 90;

        /// <summary>
        /// 元素字的基准字高是 0.675 格（= fontSize/10 × 0.075）。
        /// 字挂在 BodyScale 缩放的小怪下面，所以这里除掉它，最终世界字高才还是 0.675 格。
        /// </summary>
        const float LabelGlyphScale = 0.075f / BodyScale;

        public int Uid { get; private set; }
        public Element Element { get; private set; }
        public SpriteRenderer Body { get; private set; }
        public TextMesh Label { get; private set; }

        public void Build(int uid, Font font)
        {
            Uid = uid;

            Body = gameObject.AddComponent<SpriteRenderer>();
            Body.sprite = BoardView.SpriteOf(Color.white);
            Body.sortingOrder = 2;

            Label = BoardView.MakeText(transform, "label", "", font,
                new Color(0.05f, 0.05f, 0.07f), LabelFontSize, LabelGlyphScale, 5);
        }

        /// <summary>刷新成逻辑层这只小怪的样子（元素被沙盒改过时也要跟着变）。</summary>
        public void Show(Creep creep)
        {
            Element = creep.Element;
            if (Body == null) return;

            Body.color = ElementDefs.Tint(creep.Element);
            Body.sprite = BoardView.SpriteOf(Color.white);

            string text = ElementDefs.Name(creep.Element);
            if (Label != null && Label.text != text) Label.text = text;
        }
    }
}
