using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;

namespace TapGJ.View
{
    /// <summary>
    /// 原型 HUD：用最朴素的 IMGUI 画出来，不依赖 Canvas / 预制体，
    /// 这样「打开工程就能玩」不会因为 UI 没连好而卡住。
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        readonly List<string> _lines = new List<string>();
        Vector2 _scroll;
        GUIStyle _label, _small, _title, _wrap, _button;

        public int SelectedCardId = -1;
        public float PanelWidth = 430f;

        GameRunner _runner;

        GameEngine Engine => _runner.Engine;
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

        public void Setup(GameRunner runner)
        {
            _runner = runner;
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
            if (_runner == null || Engine == null) return;
            EnsureStyles();

            _runner.DrainNotes(_lines);

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

            string hint = _runner.Busy ? "播放中…"
                : phase == GamePhase.PlayerAction ? "可操作：出牌"
                : phase == GamePhase.Discard ? $"可操作：弃牌（还可弃 {Engine.DiscardsRemaining} 张）"
                : phase == GamePhase.Draw ? "自动补牌至上限"
                : "等待";

            GUI.Label(new Rect(rect.x + 12f, rect.y + 6f, rect.width - 24f, 26f),
                $"<b>回合 {Engine.Round} / {Engine.Rules.RoundLimit}</b>　" +
                $"阶段：<color={phaseColor}><b>{GameEngine.PhaseName(phase)}</b></color>　" +
                $"（{hint}）", _title);

            GUI.Label(new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, 34f),
                $"场上：{Engine.StatusLine()}　｜　瓶子：{Engine.Bottle.DebugLine()}　｜　"
                + $"手牌 {Engine.Hand.Count}/{Engine.Rules.HandLimit}", _label);
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
            var prevBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.9f, 0.85f, 0.5f);
            GUI.enabled = Engine.Phase == GamePhase.PlayerAction && !_runner.Busy;
            if (GUI.Button(new Rect(x, y, w * 0.48f, 30f), "结束出牌 → 弃牌", _button))
                _runner.EndPlayerRound();
            GUI.enabled = true;

            GUI.backgroundColor = new Color(0.75f, 0.9f, 0.75f);
            GUI.enabled = Engine.Phase == GamePhase.Discard && !_runner.Busy;
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 30f), "结束弃牌 → 判定", _button))
                _runner.EndDiscardRound();
            GUI.enabled = true;
            GUI.backgroundColor = prevBg;
            y += 34f;

            GUI.backgroundColor = new Color(0.7f, 0.8f, 0.95f);
            if (GUI.Button(new Rect(x, y, w * 0.48f, 26f), "重开（同种子）", _button))
                _runner.Restart();
            GUI.backgroundColor = new Color(0.95f, 0.8f, 0.75f);
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 26f), "重开（换种子）", _button))
                _runner.RestartWithNewSeed();
            GUI.backgroundColor = prevBg;
            y += 32f;

            // ---- 目标进度
            GUI.Label(new Rect(x, y, w, 24f), $"<b>胜利条件</b>：{Engine.WinConditionDescription()}", _label);
            y += 26f;

            int water = Engine.Board.WaterCount();
            int total = Engine.Board.CreepCount();
            if (Engine.Rules.WinCondition == WinCondition.WaterOnly)
            {
                DrawBar(new Rect(x, y, w, 18f), total == 0 ? 0f : (float)water / total,
                    $"水 {water} / 场上 {total}");
            }
            else
            {
                int distinct = Engine.Board.DistinctElementCount();
                DrawBar(new Rect(x, y, w, 18f), 1f - (distinct - 1) / 4f, $"元素种类 {distinct} / 5");
            }
            y += 30f;

            // ---- 元素小瓶子
            GUI.Label(new Rect(x, y, w, 22f), "<b>元素小瓶子</b>（场上每消失一个元素 +1，满瓶出一张牌）", _label);
            y += 24f;
            foreach (var e in ElementDefs.All)
            {
                int c = Engine.Bottle.Count(e);
                var color = ElementDefs.Tint(e);
                GUI.color = color;
                GUI.Label(new Rect(x, y, 22f, 20f), ElementDefs.Name(e), _label);
                GUI.color = Color.white;

                float fill = Engine.Rules.BottleCapacity == 0 ? 0f : (float)c / Engine.Rules.BottleCapacity;
                DrawBar(new Rect(x + 26f, y + 3f, w - 60f, 14f), fill, $"{c}/{Engine.Rules.BottleCapacity}", color);
                y += 21f;
            }
            y += 6f;

            // ---- 阶段提示（手牌本身在下方的手牌区里点）
            string hint;
            switch (Engine.Phase)
            {
                case GamePhase.PlayerAction:
                    var sel = Engine.FindCard(SelectedCardId);
                    hint = sel != null
                        ? $"已选中 <b>{sel.Label}</b> → 点下方手牌区里的牌可换，或直接点棋盘目标格"
                        : "点下方手牌区里的牌选中，再点棋盘上绿色高亮的目标格";
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

        int CountTargets(Card card)
        {
            int n = 0;
            foreach (var creep in Engine.Creeps)
                if (Engine.CanPlayCardOn(card, creep)) n++;
            return n;
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
            var banner = _runner.Banner;
            if (string.IsNullOrEmpty(banner)) return;

            var rect = new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.5f - 70f, 520f, 140f);
            GUI.Box(rect, GUIContent.none);
            var big = new GUIStyle(_title) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            GUI.Label(rect, banner, big);
        }
    }
}
