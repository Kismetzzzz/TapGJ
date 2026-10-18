using System.Collections.Generic;
using TapGJ.Core;
using TapGJ.Flow;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 临时 HUD：用最朴素的 IMGUI 画出来，不依赖 Canvas / 预制体。
    ///
    /// ⚠ 它是「场景化重构」过渡期的脚手架：不需要任何场景对象就能跑，
    ///   所以在 Canvas 还没搭好之前游戏仍然能玩。
    ///   正式 uGUI 版 `GameHud` 做好后，**删掉这个文件即可** —— GameSession 走的是
    ///   <see cref="IGameHud"/> 接口，不认识这个类（见该接口的注释）。
    ///
    /// ⚠ 它只**调用** GameSession 的 5 个方法推进流程（EndPlayerRound / EndDiscardRound /
    ///   Restart / RestartWithNewSeed / GoToNextLevel），不自己决定流程 —— 所以换 UI 不需要动回合状态机。
    ///   「下一关」的知识也不在这里：它问 GameSession（→ ILevelHost → LevelCatalog）要名字和「有没有」。
    ///
    /// ⚠ 唯一的例外是**退出游戏**：那不是「这一局」的事，所以它走 `SceneFlow.QuitGame()`
    ///   （Flow 层负责「编辑器里怎么退、构建里怎么退」），HUD 不自己判断在哪个环境里跑。
    /// </summary>
    public sealed class Hud : MonoBehaviour, IGameHud
    {
        readonly List<string> _lines = new List<string>();
        Vector2 _scroll;
        QuitConfirm _quit;
        GUIStyle _label, _small, _title, _wrap, _button;

        [Tooltip("面板宽度（像素）；窄窗口会自动收窄")]
        public float PanelWidth = 430f;

        GameSession _session;

        /// <summary>选中的手牌 id：真身存在 GameSession 上，手牌区与 HUD 共用同一份。</summary>
        public int SelectedCardId
        {
            get => _session != null ? _session.SelectedCardId : -1;
            set { if (_session != null) _session.SelectedCardId = value; }
        }

        GameEngine Engine => _session != null ? _session.Engine : null;
        public Font Font => BoardView.LoadFont();

        /// <summary>面板实际宽度：窄窗口时自动收窄，保证棋盘还有地方显示。</summary>
        public float EffectivePanelWidth
        {
            get
            {
                float max = Mathf.Max(280f, Screen.width * 0.62f);
                return Mathf.Min(PanelWidth, max);
            }
        }

        /// <summary>HUD 面板在屏幕上的像素矩形，用来挡住棋盘点击。</summary>
        public Rect PanelPixels => new Rect(Screen.width - EffectivePanelWidth, 0f, EffectivePanelWidth, Screen.height);

        /// <summary>
        /// <see cref="IGameHud"/> 的实现。**幂等**：GameSession 的同物体扫描和「自行注册」
        /// 可能都调到这里。
        /// </summary>
        public void Setup(GameSession session)
        {
            // 换关 / 重开要清日志面板：换一个实例就把旧实例的订阅摘掉（重复 Setup 也不会挂两遍）
            if (_session != null) _session.LevelStarted -= OnLevelStarted;
            _session = session;
            if (_session != null)
            {
                _session.LevelStarted -= OnLevelStarted;
                _session.LevelStarted += OnLevelStarted;
            }

            // IMGUI 不参与 EventSystem，所以「面板区域挡住棋盘点击」只能自己注册给 ClickRouter。
            // ⚠ 换成 uGUI 的 GameHud 时**不要照抄这一段**：Canvas + GraphicRaycaster + EventSystem
            //   一挂上，ClickRouter 走 IsPointerOverGameObject() 就够了，ExtraBlocker 保持 null。
            if (session == null || session.Input == null) return;
            session.Input.ExtraBlocker = p => PanelPixels.Contains(p);
        }

        void OnDestroy()
        {
            if (_session != null) _session.LevelStarted -= OnLevelStarted;
        }

        /// <summary>
        /// 新开一局（首次开局 / 重开 / **切到下一关**）时清空日志面板。
        /// 日志属于「这一局」：不清的话切完关会看到上一关的记录混在里面。
        /// </summary>
        void OnLevelStarted(GameEngine engine)
        {
            _lines.Clear();
            _scroll = Vector2.zero;
        }

        void Awake()
        {
            // 跟 GameSession 挂在同一个物体上时，由 GameSession.Awake 扫 IGameHud 调 Setup()；
            // 挂在别的物体上时这里自己找一次并注册。
            // 两个 Awake 的先后顺序不保证，所以两边都可能调到 Setup —— 它是幂等的。
            if (_session != null) return;

            var session = FindFirstObjectByType<GameSession>();
            if (session != null) session.RegisterHud(this);
        }

        void EnsureStyles()
        {
            if (_label != null) return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = false };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, richText = true };
            _wrap = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true, wordWrap = true };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        }

        void OnGUI()
        {
            if (_session == null || Engine == null) return;
            EnsureStyles();

            _session.DrainNotes(_lines);

            DrawTopBar();
            DrawPanel();
            DrawBanner();
        }

        // ---------------------------------------------------------------- 顶栏

        void DrawTopBar()
        {
            var rect = new Rect(12f, 10f, Mathf.Max(200f, Screen.width - EffectivePanelWidth - 36f), 74f);
            GUI.Box(rect, GUIContent.none);

            var phase = Engine.Phase;
            string phaseColor = phase == GamePhase.Preparation ? "#ffd166"
                : phase == GamePhase.Draw ? "#74c0fc"
                : phase == GamePhase.PlayerAction ? "#8ce99a"
                : phase == GamePhase.Discard ? "#ffa94d"
                : phase == GamePhase.Victory ? "#63e6be"
                : phase == GamePhase.Defeat ? "#ff8787" : "#adb5bd";

            string hint = _session.Busy ? "播放中…"
                : phase == GamePhase.PlayerAction ? "可操作：出牌"
                : phase == GamePhase.Discard ? $"可操作：弃牌（还可弃 {Engine.DiscardsRemaining} 张）"
                : phase == GamePhase.Draw ? "自动补牌至上限"
                : "等待";

            GUI.Label(new Rect(rect.x + 12f, rect.y + 6f, rect.width - 24f, 26f),
                $"<b>回合 {Engine.Round} / {Engine.Rules.RoundLimit}</b>　" +
                $"阶段：<color={phaseColor}><b>{GameEngine.PhaseName(phase)}</b></color>　" +
                $"（{hint}）" +
                // 现在在玩哪一关：切到下一关之后靠它确认真的换关了（没有关卡时（开发兜底）不显示）
                (_session.CurrentLevelName != null ? $"　｜　关卡：<b>{_session.CurrentLevelName}</b>" : ""), _title);

            GUI.Label(new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, 34f),
                $"场上：{Engine.StatusLine()}　｜　瓶子：{Engine.Bottle.DebugLine()}　｜　"
                + $"手牌 {Engine.Hand.Count}/{Engine.Rules.HandLimit}"
                + (Engine.CompositeCount > 0 ? $"　｜　复合元素 {Engine.CompositeCount}" : ""), _label);
        }

        // ---------------------------------------------------------------- 右侧面板

        void DrawPanel()
        {
            var panel = PanelPixels;
            GUI.Box(panel, GUIContent.none);

            float x = panel.x + 12f;
            float w = panel.width - 24f;
            float y = 12f;

            // ---- 操作按钮放最上面：面板下沿会被下方手牌区盖住，重要按钮不能放在那里
            // ⚠ 面板上任何一个**别的**按钮被按下 = 玩家去干别的了 → 顺手把「退出游戏」的确认态清掉，
            //   否则它还挂着红色的「确认退出游戏？」，下一次误点就直接退了。
            var prevBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.9f, 0.85f, 0.5f);
            GUI.enabled = Engine.Phase == GamePhase.PlayerAction && !_session.Busy;
            if (GUI.Button(new Rect(x, y, w * 0.48f, 30f), "结束出牌 → 弃牌", _button))
            {
                _quit.Disarm();
                _session.EndPlayerRound();
            }
            GUI.enabled = true;

            GUI.backgroundColor = new Color(0.75f, 0.9f, 0.75f);
            GUI.enabled = Engine.Phase == GamePhase.Discard && !_session.Busy;
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 30f), "结束弃牌 → 判定", _button))
            {
                _quit.Disarm();
                _session.EndDiscardRound();
            }
            GUI.enabled = true;
            GUI.backgroundColor = prevBg;
            y += 34f;

            GUI.backgroundColor = new Color(0.7f, 0.8f, 0.95f);
            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f), "重开（同种子）", _button))
            {
                _quit.Disarm();
                _session.Restart();
            }
            GUI.backgroundColor = new Color(0.95f, 0.8f, 0.75f);
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f), "重开（换种子）", _button))
            {
                _quit.Disarm();
                _session.RestartWithNewSeed();
            }
            GUI.backgroundColor = prevBg;
            y += 32f;

            // ---- 下一关：目录里当前关之后的下一关，到目录末尾就停止。
            // 没有关卡加载器（开发兜底路径）/ 后面没有可用关卡时按钮置灰，标签会写明原因。
            // ⚠ 播动画时不给按：换关会重建整关，正在播的步骤动画属于上一关，按下去会错位。
            GUI.backgroundColor = new Color(0.75f, 0.95f, 0.8f);
            GUI.enabled = _session.HasNextLevel && !_session.Busy;
            if (GUI.Button(new Rect(x, y, w, 26f), NextLevelButtonLabel(_session), _button))
            {
                _quit.Disarm();
                _session.GoToNextLevel();
            }
            GUI.enabled = true;
            GUI.backgroundColor = prevBg;
            y += 32f;

            // ---- 退出游戏：**两段式**（第一下变「确认退出游戏？」，第二下才退），
            // 因为它和上面几个按钮挤在一起，误点一下整局就没了。
            // ⚠ 退出只能走 SceneFlow（编辑器里 Application.Quit 不生效，由它分流），HUD 不自己判断。
            // ⚠ 播放动画时也**保持可点**：这是随时能跑掉的出口，不能因为正在播动画就锁住。
            bool quitArmed = _quit.Armed(Time.time);
            GUI.backgroundColor = quitArmed ? new Color(1f, 0.42f, 0.38f) : new Color(0.95f, 0.66f, 0.6f);
            if (GUI.Button(new Rect(x, y, w, 26f), QuitButtonLabel(quitArmed), _button) && _quit.Click(Time.time))
                SceneFlow.QuitGame();
            GUI.backgroundColor = prevBg;
            y += 32f;

            // ---- 回合事件：本回合生效的那条 + 下一次落在第几回合（预告）
            // ⚠ 读的是引擎的 ActiveEvent / NextEventRound，不是调度细节 —— 事件系统怎么排节奏 HUD 不关心。
            if (Engine.EventsEnabled)
            {
                var active = Engine.ActiveEvent;
                string what = active != null ? $"<b>{active.DisplayName}</b>：{active.Description}" : "无";

                string next = "";
                if (Engine.NextEventRound > 0)
                    next = $"　下一次：第 <b>{Engine.NextEventRound}</b> 回合";

                GUI.Label(new Rect(x, y, w, 40f), $"<b>本回合事件</b>：{what}{next}", _wrap);
                y += 44f;
            }

            // ---- 目标进度
            var progress = Engine.GetGoalProgress();
            GUI.Label(new Rect(x, y, w, 24f), $"<b>胜利条件</b>：{progress.Description}", _label);
            y += 26f;

            DrawBar(new Rect(x, y, w, 18f), progress.Fill, progress.Label);
            y += 30f;

            // ---- 元素小瓶子（本关关掉瓶子时整块不画）
            // ⚠ 容量只有**一个**真源：Engine.Bottle.Capacity（本回合实际生效的容量）。
            //   不要读 Rules.BottleCapacity —— 那是本关的基准值，回合事件「玉翁敛息」改的是前者，
            //   读后者会让进度条显示 4/5 而实际 3 就爆。
            if (Engine.Rules.BottlesEnabled)
            {
                GUI.Label(new Rect(x, y, w, 22f), "<b>元素小瓶子</b>（场上每消失一个元素 +1，满瓶出一张牌）", _label);
                y += 24f;
                int capacity = Mathf.Max(1, Engine.Bottle.Capacity);
                foreach (var e in ElementDefs.All)
                {
                    int c = Engine.Bottle.Count(e);
                    var color = ElementDefs.Tint(e);
                    GUI.color = color;
                    GUI.Label(new Rect(x, y, 22f, 20f), ElementDefs.Name(e), _label);
                    GUI.color = Color.white;

                    float fill = (float)c / capacity;
                    DrawBar(new Rect(x + 26f, y + 3f, w - 60f, 14f), fill, $"{c}/{capacity}", color);
                    y += 21f;
                }
                y += 6f;
            }

            // ---- 阶段提示（手牌本身在下方的手牌区里点）
            string hint;
            switch (Engine.Phase)
            {
                case GamePhase.PlayerAction:
                    var sel = _session.SelectedCard;
                    hint = sel == null
                        ? "点下方手牌区里的牌选中，再点棋盘上绿色高亮的目标格"
                          + "（元素格=发生反应，空格=在该格召唤）"
                        : DescribeSelectedCard(sel);
                    if (Engine.Rules.MaxCardsPerRound > 0)
                        hint += $"\n本回合还可出 {Engine.PlaysRemaining} 张";
                    else
                        hint += $"\n本回合已出 {Engine.CardsPlayedThisRound} 张（不限）";
                    break;
                case GamePhase.Discard:
                    hint = $"点下方手牌区里的牌即弃掉\n本回合还可弃 {Engine.DiscardsRemaining} 张";
                    break;
                case GamePhase.Preparation:
                    hint = "准备阶段：小怪正在随机移动，玩家不能操作";
                    break;
                case GamePhase.Judgment:
                    hint = "判定阶段：正在结算胜负";
                    break;
                default:
                    hint = GameEngine.PhaseName(Engine.Phase);
                    break;
            }
            GUI.Label(new Rect(x, y, w, 46f), hint, _wrap);
            y += 50f;

            // ---- 事件日志
            GUI.Label(new Rect(x, y, w, 22f), "<b>反应日志</b>", _label);
            y += 24f;

            float logHeight = panel.height - y - 12f;
            if (logHeight < 80f) logHeight = 80f;
            var viewRect = new Rect(x, y, w, logHeight);
            var contentRect = new Rect(0f, 0f, w - 20f, Mathf.Max(logHeight, _lines.Count * 17f + 10f));
            _scroll = GUI.BeginScrollView(viewRect, _scroll, contentRect);
            for (int i = 0; i < _lines.Count; i++)
            {
                GUI.Label(new Rect(2f, i * 17f, contentRect.width - 4f, 17f), _lines[i], _small);
            }
            GUI.EndScrollView();
        }

        /// <summary>选中一张牌之后的操作提示（元素牌 / 融合牌 / 分解牌三种说法）。</summary>
        string DescribeSelectedCard(Card sel)
        {
            if (sel.IsFusionCard)
                return "已选中 <b>融合牌</b> → 先点第一个元素，再点绿色高亮的第二个元素（点右键返回）";

            if (sel.IsDecomposeCard)
                return "已选中 <b>分解牌</b> → 点绿色高亮的复合元素把它拆回两张元素牌（点右键返回）";

            return $"已选中 <b>{sel.Label}</b> → 点绿色高亮的<b>元素格</b>发生反应，"
                   + $"或点绿色高亮的<b>空格</b>在该格召唤 1 个{ElementDefs.Name(sel.Element)}元素（点右键返回）";
        }

        /// <summary>这张牌现在有几个合法目标（元素格 + 可召唤的空格）。</summary>
        int CountTargets(Card card)
        {
            if (card == null || Engine == null) return 0;
            return Engine.CellsForElementCard(card).Count;
        }

        /// <summary>
        /// 「下一关」按钮上的字。**点之前就知道要去哪一关**（关卡名写在按钮上）；
        /// 没有下一关时写明原因，省得玩家以为按钮坏了。
        /// </summary>
        public static string NextLevelButtonLabel(GameSession session)
        {
            if (session == null || !session.HasNextLevel) return "下一关（没有下一关）";

            var name = session.NextLevelName;
            return string.IsNullOrEmpty(name) ? "下一关 →" : $"下一关 → {name}";
        }

        /// <summary>
        /// 「退出游戏」按钮上的字。**退出不可撤销**，所以按钮是两段式的（见 <see cref="QuitConfirm"/>）：
        /// 第一下只把字换成问句，第二下才真的退 —— 字必须跟着变，否则第二下会显得像「按钮没反应」。
        /// </summary>
        public static string QuitButtonLabel(bool confirming) => confirming ? "确认退出游戏？" : "退出游戏";

        /// <summary>
        /// 「退出游戏」按钮的两段式确认状态。**纯逻辑**：不碰 GUI、不碰 Unity 的退出 API，
        /// 所以 EditMode 测试能直接演练一串点击（见 HudQuitTests），不需要真的开 Play。
        ///
        /// 为什么要有确认：这个按钮和「重开」「下一关」挤在同一块面板里，误点一下整局就没了；
        /// 为什么不是弹窗：IMGUI 弹窗要自己管焦点和层级，两段式按钮零成本得到同样的保护。
        /// 超时自己取消，免得按钮一直挂着「确认」态，下一个人点它就退了。
        /// </summary>
        public struct QuitConfirm
        {
            /// <summary>点了第一下之后，多少秒内再点才算「确认」。</summary>
            public const float ConfirmSeconds = 4f;

            float _armedUntil;

            /// <summary>现在是不是「等确认」状态。</summary>
            public bool Armed(float now) => now < _armedUntil;

            /// <summary>
            /// 点一下按钮。返回 **true = 玩家确认了，调用方去真的退出**；
            /// false = 只是刚进入确认态（第一下）。
            /// </summary>
            public bool Click(float now)
            {
                if (!Armed(now))
                {
                    _armedUntil = now + ConfirmSeconds;
                    return false;
                }

                Disarm();
                return true;
            }

            /// <summary>取消确认态（自己超时、或者玩家去点了别的按钮）。</summary>
            public void Disarm() => _armedUntil = float.NegativeInfinity;
        }

        void DrawBar(Rect rect, float fill, string text, Color? color = null)
        {
            var bg = new Color(0.18f, 0.20f, 0.24f, 1f);
            var fg = color ?? new Color(0.35f, 0.75f, 1f);

            var prev = GUI.color;
            GUI.color = bg;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fg;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fill), rect.height), Texture2D.whiteTexture);

            // 进度条上的文字用半透明黑，压在填充色上仍然清楚
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.Label(new Rect(rect.x + 6f, rect.y - 1f, rect.width - 12f, rect.height + 2f), text, _small);
            GUI.color = prev;
        }

        void DrawBanner()
        {
            var banner = _session.Banner;
            if (string.IsNullOrEmpty(banner)) return;

            var rect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 70f, 520f, 140f);
            GUI.Box(rect, GUIContent.none);
            var big = new GUIStyle(_title) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, banner, big);

            // 赢了之后顺手说清下一步：右边那个「下一关」就是继续玩的地方
            if (Engine.Phase == GamePhase.Victory && _session.HasNextLevel)
            {
                var hint = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(rect.x, rect.yMax + 6f, rect.width, 24f),
                    $"点右侧「{NextLevelButtonLabel(_session)}」继续", hint);
            }
        }
    }
}
