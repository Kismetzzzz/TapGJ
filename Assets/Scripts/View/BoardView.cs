using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 棋盘表现层：把 GameEngine 的状态画成 2D 网格。
    ///
    /// ⚠ 尺寸与位置由**场景**决定，不由屏幕决定：
    ///   · CellSize / CellGap 决定格子多大（Inspector 上改）
    ///   · 棋盘以**自己的 transform 为中心** —— 把 BoardAnchor 摆到哪儿，棋盘就出现在哪儿
    ///   相机不再被代码接管：你摆好的构图会一直保持，窗口尺寸变了也不会自己动。
    ///
    /// ⚠ 对象所有权只有两份，不要混：
    ///   CellView  —— 格子自己的底板 / 高亮 / 坐标角标，**静态**，一辈子跟着格子不动。
    ///   CreepView —— 一只小怪的色块 + 元素字，**全部**挂在 _creepRoot 下，位置用局部坐标。
    ///   Sync() 是唯一入口：以引擎为准双向对账，多退少补。
    ///
    /// ⚠ 这个组件会自己生成 W×H 个格子：不要手工往它下面塞格子物体，
    /// 运行时 Setup() 会先清空子物体再重建。
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        [Header("格子尺寸（世界单位）")]
        [Tooltip("单个格子多大。1 = 一个 Unity 单位")]
        public float CellSize = 1.0f;

        [Tooltip("格子之间的间隙")]
        public float CellGap = 0.10f;

        [Header("编辑器预览（只影响 Scene 视图，不进运行时）")]
        [Tooltip("没进 Play 时 Scene 视图里画几列格子")]
        public int PreviewWidth = 8;

        [Tooltip("没进 Play 时 Scene 视图里画几行格子")]
        public int PreviewHeight = 8;

        public GameEngine Engine;
        [Tooltip("让背景中的棋盘透出；墙与合法目标高亮仍保留。")]
        public bool TransparentCells;

        [Header("棋子美术")]
        public BattlePieceArt PieceArt;

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

        public float BoardWidthWorld => Engine.Board.Width * CellSize + (Engine.Board.Width - 1) * CellGap;
        public float BoardHeightWorld => Engine.Board.Height * CellSize + (Engine.Board.Height - 1) * CellGap;
        public Vector3 BoardCenter => _cellRoot != null ? _cellRoot.transform.position : Vector3.zero;

        public event System.Action<Pos> CellClicked;

        /// <summary>由 GameSession 控制：动画播放中 / 过场中时禁止点选。</summary>
        public bool InteractionEnabled = true;

        void OnValidate()
        {
            // 手滑填 0 或负数会让整个棋盘消失，这里兜一下
            if (CellSize < 0.05f) CellSize = 0.05f;
            if (CellGap < 0f) CellGap = 0f;
        }

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

            Build();
            ApplyCellArt();
        }

        /// <summary>
        /// 按当前 CellSize 决定格子的精细度：格子太小就隐藏坐标角标，
        /// 否则会出现「坐标字比格子还大」的情况。
        /// 元素字不用管 —— 它挂在小怪下面，跟着格子缩放走。
        /// </summary>
        void ApplyCellArt()
        {
            bool showCoords = CellSize > 0.55f;
            foreach (var cell in _cells)
            {
                cell.ApplyScale(showCoords);
                cell.SetBackgroundVisible(!TransparentCells || Engine.Board.IsBlocked(cell.Pos));
            }
        }

        /// <summary>
        /// 格子在棋盘**局部空间**里的位置。
        /// _cellRoot / _creepRoot 的 localPosition 都是零，所以小怪用这个值当 localPosition，
        /// 就和格子严丝合缝地对齐；棋盘被挪动/缩放时小怪也会自动跟着走。
        /// </summary>
        public Vector3 CellLocalPos(Pos p)
        {
            if (Engine == null) return Vector3.zero;

            float w = BoardWidthWorld;
            float h = BoardHeightWorld;
            float x = -w * 0.5f + CellSize * 0.5f + p.X * (CellSize + CellGap);
            float y = -h * 0.5f + CellSize * 0.5f + p.Y * (CellSize + CellGap);
            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Scene 视图里把棋盘占地 + 每个格子画出来（选中 BoardAnchor 可见）。
        /// 格子和手牌一样是运行时才生成的，所以用 Gizmo 让你「没进 Play」也能看到
        /// 棋盘有多大、摆在哪 —— 相机构图时尤其有用。
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (CellSize <= 0f) return;

            int w = Engine != null ? Engine.Board.Width : PreviewWidth;
            int h = Engine != null ? Engine.Board.Height : PreviewHeight;
            if (w <= 0 || h <= 0) return;

            float bw = w * CellSize + (w - 1) * CellGap;
            float bh = h * CellSize + (h - 1) * CellGap;
            var center = transform.position;

            // 外框
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireCube(center, new Vector3(bw, bh, 0f));

            // 逐格：Play 里画真实元素色，编辑里画中性色
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var local = new Vector3(
                        -bw * 0.5f + CellSize * 0.5f + x * (CellSize + CellGap),
                        -bh * 0.5f + CellSize * 0.5f + y * (CellSize + CellGap), 0f);

                    var pos = center + transform.right * local.x + transform.up * local.y;
                    pos.z = center.z;

                    Color c = new Color(1f, 1f, 1f, 0.14f);
                    if (Engine != null)
                    {
                        var creep = Engine.Board.At(new Pos(x, y));
                        if (creep != null) c = ElementDefs.Tint(creep.Element);
                    }

                    Gizmos.color = c;
                    Gizmos.DrawWireCube(pos, new Vector3(CellSize, CellSize, 0f));
                }
            }
        }

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
                    view.Build(pos, _font, CellSize, Engine.Board.IsBlocked(pos));
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
        ///
        /// 复合元素走的是**同一套对象**（同样是 Creep_{uid} 挂在 Creeps 下），
        /// 所以「表现对象数 = 逻辑层元素数 + 复合元素数」这条不变量依然成立。
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
                view.ShowCreep(creep);
                view.transform.localPosition = CellLocalPos(creep.Pos);
            }

            foreach (var composite in Engine.Composites)
            {
                _live.Add(composite.Uid);
                if (_frozen.Contains(composite.Uid)) continue;

                var view = EnsureCreepView(composite.Uid);
                view.ShowComposite(composite);
                view.transform.localPosition = CellLocalPos(composite.Pos);
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
            go.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale);
            var view = go.AddComponent<CreepView>();
            view.Build(uid, _font, PieceArt);
            _creeps[uid] = go;
            return view;
        }

        public GameObject CreepGo(int uid) => _creeps.TryGetValue(uid, out var go) ? go : null;

        public CreepView CreepViewOf(int uid)
        {
            var go = CreepGo(uid);
            return go == null ? null : go.GetComponent<CreepView>();
        }

        /// <summary>格子上的小怪表现对象；该格空着 / 是复合元素就返回 null。</summary>
        public CreepView CreepViewAt(Pos pos)
        {
            var creep = Engine.Board.At(pos);
            return creep == null ? null : CreepViewOf(creep.Uid);
        }

        /// <summary>某只占格者（元素小怪或复合元素）的显示对象。</summary>
        public CreepView OccupantViewAt(Pos pos)
        {
            if (Engine == null) return null;
            var occupant = Engine.Board.OccupantAt(pos);
            return occupant == null ? null : CreepViewOf(occupant.Uid);
        }

        /// <summary>某格上复合元素的显示对象（没有就返回 null）。</summary>
        public CreepView CompositeViewAt(Pos pos)
        {
            var composite = Engine.Board.CompositeAt(pos);
            return composite == null ? null : CreepViewOf(composite.Uid);
        }

        /// <summary>场上复合元素表现对象的数量。</summary>
        public int CompositeObjectCount
        {
            get
            {
                if (Engine == null) return 0;
                int n = 0;
                foreach (var c in Engine.Composites)
                    if (_creeps.ContainsKey(c.Uid)) n++;
                return n;
            }
        }

        /// <summary>
        /// 场上所有占格者的表现对象数量（小怪 + 复合元素）。
        /// 不变量：它必须等于 Engine.CreepCount + Engine.CompositeCount。
        /// </summary>
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
        public IEnumerator<object> PlayStep(StepRecord step, float stepDuration, float cardEffectDuration = 0)
        {
            if (step == null) yield break;
            if (cardEffectDuration > 0)
            {
                yield return PlayCardEffects(step, cardEffectDuration);
                yield break;
            }

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
                        // 相生：小怪换了元素。闪一下格子，动画播完 GameSession 会 Sync 一次，
                        // 颜色和元素字就跟着换过来了（uid 没变，所以是同一个对象换皮）。
                        yield return PlayFlash(e.From, new Color(0.75f, 0.95f, 1f), 0.20f);
                        break;
                    case GameEventType.Vanish:
                        yield return PlayVanish(e, 0.16f);
                        break;
                    case GameEventType.Spawn:
                        yield return PlaySpawn(e, 0.16f);
                        break;
                    case GameEventType.CompositeSpawn:
                        yield return PlayCompositeSpawn(e, 0.20f);
                        break;
                    case GameEventType.CompositeVanish:
                        yield return PlayVanish(e, 0.18f);
                        break;
                    case GameEventType.SpawnBlocked:
                        yield return PlayFlash(e.From, new Color(1f, 0.4f, 0.3f), 0.14f);
                        break;
                }

                if (freeze) _frozen.Remove(uid);
            }

            _frozen.Clear(); // 兜底：一个步骤播完，动画期一定结束
        }

        IEnumerator<object> PlayCardEffects(StepRecord step, float duration)
        {
            var transitions = new List<(CreepView view, bool vanish)>();
            _live.Clear();
            foreach (var creep in Engine.Creeps) _live.Add(creep.Uid);
            foreach (var composite in Engine.Composites) _live.Add(composite.Uid);
            // 融合消耗素材时没有独立的消失事件，直接与现存表现对象对账。
            foreach (var entry in _creeps)
                if (!_live.Contains(entry.Key) && entry.Value != null)
                {
                    var view = entry.Value.GetComponent<CreepView>();
                    if (view == null) continue;
                    _frozen.Add(view.Uid);
                    transitions.Add((view, true));
                    ApplyCardAppearance(view, 1);
                }
            foreach (var e in step.Events)
            {
                CreepView view = null;
                bool vanish = e.Type == GameEventType.Vanish || e.Type == GameEventType.CompositeVanish;
                if (vanish) view = CreepViewOf(e.Uid);
                else if (e.Type == GameEventType.Spawn || e.Type == GameEventType.Transform)
                {
                    var creep = FindCreep(e.Uid);
                    if (creep == null) continue;
                    view = EnsureCreepView(creep.Uid);
                    view.ShowCreep(creep);
                    view.transform.localPosition = CellLocalPos(creep.Pos);
                }
                else if (e.Type == GameEventType.CompositeSpawn)
                {
                    var composite = FindComposite(e.Uid);
                    if (composite == null) continue;
                    view = EnsureCreepView(composite.Uid);
                    view.ShowComposite(composite);
                    view.transform.localPosition = CellLocalPos(composite.Pos);
                }
                if (view == null || !_frozen.Add(view.Uid)) continue;
                transitions.Add((view, vanish));
                ApplyCardAppearance(view, vanish ? 1 : 0);
            }

            // 多个棋子共用一条时间线，和卡牌一样使用不受暂停缩放影响的时间。
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / duration));
                foreach (var transition in transitions)
                    if (transition.view != null)
                        ApplyCardAppearance(transition.view, transition.vanish ? 1 - t : t);
                yield return null;
            }
            foreach (var transition in transitions)
            {
                if (transition.view == null) continue;
                if (transition.vanish)
                {
                    _creeps.Remove(transition.view.Uid);
                    Kill(transition.view.gameObject);
                }
                else ApplyCardAppearance(transition.view, 1);
            }
            _frozen.Clear();
        }

        void ApplyCardAppearance(CreepView view, float opacity)
        {
            view.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale * Mathf.Lerp(.85f, 1, opacity));
            var color = view.Body.color;
            color.a = opacity;
            view.Body.color = color;
            color = view.Label.color;
            color.a = opacity;
            view.Label.color = color;
            if (view.Ring != null)
            {
                color = view.Ring.color;
                color.a = .55f * opacity;
                view.Ring.color = color;
            }
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
                case GameEventType.CompositeSpawn:
                case GameEventType.CompositeVanish:
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
            view.ShowCreep(creep);
            view.transform.localPosition = CellLocalPos(e.To);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                view.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale) * k;
                yield return null;
            }
            view.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale);
        }

        /// <summary>复合元素出现：弹一下 + 先把格子闪成它的配色，比普通生成更有分量。</summary>
        IEnumerator<object> PlayCompositeSpawn(GameEvent e, float duration)
        {
            var composite = FindComposite(e.Uid);
            if (composite == null) yield break;

            var view = EnsureCreepView(composite.Uid);
            view.ShowComposite(composite);
            view.transform.localPosition = CellLocalPos(composite.Pos);

            var cell = CellAt(composite.Pos);
            if (cell != null && cell.Highlight != null)
                cell.Highlight.color = new Color(CompositeDefs.Tint(composite.Kind).r,
                    CompositeDefs.Tint(composite.Kind).g, CompositeDefs.Tint(composite.Kind).b, 0.7f);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                // 先放大到 1.12 再回到 1，做出「弹出来」的手感
                float pop = 1f + 0.12f * Mathf.Sin(k * Mathf.PI);
                view.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale) * k * pop;
                yield return null;
            }
            view.transform.localScale = Vector3.one * (CellSize * CreepView.BodyScale);

            if (cell != null && cell.Highlight != null) cell.Highlight.color = new Color(1f, 1f, 1f, 0f);
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

        Composite FindComposite(int uid)
        {
            foreach (var c in Engine.Composites)
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
        /// 点击格子的入口。射线检测由 ClickRouter 统一做（它要同时区分手牌 / 格子 / UI），
        /// 所以这里不再自己读 Input，避免一处点击被两边各处理一次。
        /// </summary>
        public void NotifyCellClicked(Pos pos)
        {
            if (Engine == null || !InteractionEnabled) return;
            OnCellClicked(pos);
        }

        /// <summary>
        /// 玩家选中的牌：能发生反应的元素格 + 能召唤的空格都高亮，帮助测试时看清规则。
        /// 「哪些格子合法」由引擎算（<see cref="GameEngine.CellsForElementCard"/>），
        /// 和融合牌 / 分解牌走同一条路 —— 表现层不自己拼规则。
        /// </summary>
        public void RefreshHighlights(Card selectedCard)
        {
            var valid = new HashSet<Pos>();
            if (selectedCard != null && Engine != null && selectedCard.IsElementCard)
            {
                foreach (var pos in Engine.CellsForElementCard(selectedCard)) valid.Add(pos);
            }

            RefreshHighlights(valid, Pos.None);
        }

        /// <summary>
        /// 高亮一批格子（融合牌的两段式选点、分解牌的目标格都用它），
        /// <paramref name="focus"/> 那一格再加一层「已选中」的暖色。
        ///
        /// 会替换掉之前的高亮，所以「取消选中」只要传两个空参数即可。
        /// </summary>
        public void RefreshHighlights(ICollection<Pos> valid, Pos focus)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                var view = _cells[i];
                Color color;

                if (valid != null && valid.Contains(view.Pos))
                    color = new Color(0.4f, 1f, 0.5f, 0.26f);
                else if (view.Pos == focus)
                    color = new Color(1f, 0.85f, 0.3f, 0.34f);
                else
                    color = new Color(1f, 1f, 1f, 0f);

                view.SetHighlight(color);
            }

            _selectedCell = focus;
            _selectedCellDirty = false;
        }

        /// <summary>清掉所有高亮（右键取消选中时用）。</summary>
        public void ClearHighlights() => RefreshHighlights(new HashSet<Pos>(), Pos.None);

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

        public void SetBackgroundVisible(bool visible)
        {
            if (_background != null) _background.enabled = visible;
            if (_coord != null) _coord.gameObject.SetActive(visible);
        }

        public void Build(Pos pos, Font font, float cellSize, bool blocked)
        {
            Pos = pos;

            if (blocked)
            {
                // 墙：实心暗色块，没有碰撞体、没有坐标角标（纯障碍）
                _background = BoardView.MakeSolid(transform, "bg", new Color(0.15f, 0.14f, 0.19f), 0);
            }
            else
            {
                // 普通格子：深色方格 + 描边，带碰撞体
                _background = BoardView.MakeBox(transform, "bg", new Color(0.16f, 0.18f, 0.22f), 0);
                var col = gameObject.AddComponent<BoxCollider2D>();
                col.size = Vector2.one * cellSize;
            }

            // 高亮层：默认全透明，鼠标/选牌时上色
            _highlight = BoardView.MakeBox(transform, "highlight", new Color(1f, 1f, 1f, 0f), 3);
            _highlight.color = new Color(1f, 1f, 1f, 0f);

            if (blocked) return;

            // 坐标角标：默认隐藏，只有格子够大时才显示（见 ApplyScale）
            _coord = BoardView.MakeText(transform, "coord", $"{pos.X},{pos.Y}", font,
                new Color(1f, 1f, 1f, 0.28f), CoordFontSize, CoordGlyphScale, 1);
            _coord.transform.localPosition = new Vector3(0.30f, -0.33f, -0.02f);
            _coord.gameObject.SetActive(false);
        }

        /// <summary>格子太小时就别显示坐标角标了。</summary>
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
    /// 一只小怪或复合元素的显示对象：使用对应素材，缺图时显示色块和元素字。
    /// 移动、生成和消失动画沿用同一个节点；复合元素缺图时再补充光环。
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

        /// <summary>这个显示对象当前画的是不是复合元素。</summary>
        public bool IsComposite { get; private set; }

        /// <summary>画复合元素时的种类（IsComposite = false 时没意义）。</summary>
        public CompositeKind CompositeKind { get; private set; }

        public SpriteRenderer Body { get; private set; }
        /// <summary>复合元素的光环；普通元素上没有这个对象。</summary>
        public SpriteRenderer Ring { get; private set; }
        public TextMesh Label { get; private set; }
        public bool HasArtwork { get; private set; }
        BattlePieceArt _art;

        public void Build(int uid, Font font, BattlePieceArt art = null)
        {
            Uid = uid;
            _art = art;

            Body = gameObject.AddComponent<SpriteRenderer>();
            Body.sprite = BoardView.SpriteOf(Color.white);
            Body.sortingOrder = 2;

            Label = BoardView.MakeText(transform, "label", "", font,
                new Color(0.05f, 0.05f, 0.07f), LabelFontSize, LabelGlyphScale, 5);
        }

        /// <summary>刷新成逻辑层这只小怪的样子（元素被沙盒改过时也要跟着变）。</summary>
        public void ShowCreep(Creep creep)
        {
            IsComposite = false;
            Element = creep.Element;
            if (Body == null) return;

            var sprite = _art != null ? _art.GetSprite(creep.Element) : null;
            HasArtwork = sprite != null;
            Body.color = HasArtwork ? Color.white : ElementDefs.Tint(creep.Element);
            Body.sprite = sprite != null ? sprite : BoardView.SpriteOf(Color.white);

            string text = ElementDefs.Name(creep.Element);
            if (Label != null && Label.text != text) Label.text = text;
            if (Label != null) Label.gameObject.SetActive(!HasArtwork);

            if (Ring != null) Ring.enabled = false;
        }

        /// <summary>刷新成逻辑层这个复合元素的样子（配色 + 名字 + 光环）。</summary>
        public void ShowComposite(Composite composite)
        {
            IsComposite = true;
            CompositeKind = composite.Kind;
            if (Body == null) return;

            var tint = CompositeDefs.Tint(composite.Kind);
            var sprite = _art != null ? _art.GetSprite(composite.Kind) : null;
            HasArtwork = sprite != null;
            Body.color = HasArtwork ? Color.white : tint;
            Body.sprite = sprite != null ? sprite : BoardView.SpriteOf(Color.white);

            string text = CompositeDefs.Name(composite.Kind);
            if (Label != null && Label.text != text) Label.text = text;
            if (Label != null) Label.gameObject.SetActive(!HasArtwork);

            if (!HasArtwork) EnsureRing();
            if (Ring != null)
            {
                Ring.enabled = !HasArtwork;
                Ring.color = new Color(tint.r, tint.g, tint.b, 0.55f);
            }
        }

        /// <summary>复合元素的光环是**按需生成**的：普通元素上不该多一个空 SpriteRenderer 子物体。</summary>
        void EnsureRing()
        {
            if (Ring != null) return;

            Ring = BoardView.MakeSolid(transform, "ring", new Color(1f, 1f, 1f, 0.4f), 1);
            Ring.transform.localScale = Vector3.one * 1.28f;
        }
    }
}
