using System;
using System.Collections.Generic;
using System.Text;
using TapGJ.Core;
using UnityEditor;
using UnityEngine;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 规则窗户：左边是图2 的完整反应表；右边是对局模拟器。
    /// 用来回答「这个玩法到底能不能赢」「改一条规则会怎样」这类问题，不用一局一局手点。
    /// </summary>
    public sealed class RulesInspectorWindow : EditorWindow
    {
        int _tab;

        // 模拟器参数
        Rules _rules = new Rules();
        int _seeds = 200;
        int _baseSeed = 1;
        string _report = "";
        Vector2 _reportScroll;

        [MenuItem("TapGJ/规则与批量模拟 %#r")]
        public static void Open()
        {
            var win = GetWindow<RulesInspectorWindow>("TapGJ 规则窗");
            win.minSize = new Vector2(880f, 560f);
            win.Show();
        }

        void OnGUI()
        {
            _tab = GUILayout.Toolbar(_tab, new[] { "反应表（图2）", "批量模拟", "玩法说明" });
            EditorGUILayout.Space(6f);

            switch (_tab)
            {
                case 0: DrawTable(); break;
                case 1: DrawSimulator(); break;
                default: DrawHelp(); break;
            }
        }

        // ------------------------------------------------------------ 反应表

        void DrawTable()
        {
            EditorGUILayout.HelpBox(
                "行 = 主动移动 / 出牌的一方，列 = 被撞上的一方。\n" +
                "「消失」= 该元素小怪从棋盘移除；「X+1」= 反应后紧贴反应点在相邻空格生成 1 个 X 元素。\n" +
                "同元素相遇不发生反应，只禁止这一次移动。",
                MessageType.Info);

            var table = new ReactionTable();
            const float cell = 150f;

            // 表头
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("", GUILayout.Width(70f));
            foreach (var col in ElementDefs.All)
                GUILayout.Label($"撞上 {ElementDefs.Name(col)}", EditorStyles.boldLabel, GUILayout.Width(cell));
            EditorGUILayout.EndHorizontal();

            foreach (var row in ElementDefs.All)
            {
                EditorGUILayout.BeginHorizontal();
                var prev = GUI.color;
                GUI.color = ElementDefs.Tint(row);
                GUILayout.Label($"{ElementDefs.Name(row)} 主动", EditorStyles.boldLabel, GUILayout.Width(70f));
                GUI.color = prev;

                foreach (var col in ElementDefs.All)
                {
                    var def = table.Get(row, col);
                    string text;
                    if (row == col) text = "不反应 / 禁止移动";
                    else if (def.Rule == ReactionRule.BothVanish) text = "双方都消失\n不生成";
                    else if (def.CreatesCreep) text = $"{ElementDefs.Name(row)} 遇 {ElementDefs.Name(col)}\n→ {ElementDefs.Name(row)}消失\n{ElementDefs.Name(def.Spawn)}+1";
                    else text = $"{ElementDefs.Name(row)} 消失";

                    GUILayout.Label(text, EditorStyles.helpBox, GUILayout.Width(cell), GUILayout.Height(58f));
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("设计观察（原型阶段的自动检查）", EditorStyles.boldLabel);
            foreach (var line in AnalyseTable()) EditorGUILayout.LabelField("· " + line, EditorStyles.wordWrappedLabel);
        }

        /// <summary>把反应表里能一眼看出来的结构性事实列出来，省得每次人工核对。</summary>
        static List<string> AnalyseTable()
        {
            var table = new ReactionTable();
            var lines = new List<string>();

            int decided = 0, undecided = 0;
            foreach (var a in ElementDefs.All)
                foreach (var b in ElementDefs.All)
                {
                    if (a == b) continue;
                    if (table.Get(a, b).IsDecided) decided++;
                    else undecided++;
                }

            lines.Add($"25 格 = 同元素 5 格（不反应/禁止移动）+ 跨元素 {decided + undecided} 格；"
                      + $"其中 {decided / 2} 对已判定赢家，{undecided / 2} 对无赢家。");
            lines.Add("读法：原表两种句式（「X+1」与「该X元素消失」）统一解成「被点名的元素是输家」，"
                      + "两个方向指向同一赢家，10 对全部可判。");

            // 谁能被「生成」出来，谁永远只会减少
            foreach (var e in ElementDefs.All)
            {
                bool canBeSpawned = false;
                foreach (var a in ElementDefs.All)
                    foreach (var b in ElementDefs.All)
                        if (a != b && table.Get(a, b).CreatesCreep && table.Get(a, b).Spawn == e)
                            canBeSpawned = true;

                lines.Add(canBeSpawned
                    ? $"{ElementDefs.Name(e)} 会被反应生成（数量可能回升）"
                    : $"{ElementDefs.Name(e)} 从来不会被生成 → 场上数量单调不增");
            }

            // 「只剩 X」这类胜利条件是否可达
            var neverGenerated = new List<string>();
            foreach (var e in ElementDefs.All)
            {
                bool canBeSpawned = false;
                foreach (var a in ElementDefs.All)
                    foreach (var b in ElementDefs.All)
                        if (a != b && table.Get(a, b).CreatesCreep && table.Get(a, b).Spawn == e)
                            canBeSpawned = true;
                if (!canBeSpawned) neverGenerated.Add(ElementDefs.Name(e));
            }

            if (neverGenerated.Count > 0)
            {
                lines.Add($"⚠ 永远不会新增的元素：{string.Join("、", neverGenerated)}。" +
                          $"因此「全场只剩{string.Join("或", neverGenerated)}」理论上要靠把其它 4 种全部清零才可能达成，" +
                          "而清零只能靠反应，且每次反应都可能生成别的元素 → 该胜利条件在纯反应下几乎不可达。");
            }

            return lines;
        }

        // ------------------------------------------------------------ 批量模拟

        void DrawSimulator()
        {
            EditorGUILayout.HelpBox(
                "用当前参数跑多局（每局自动推进 15 回合上限），统计胜负与终局形态。\n" +
                "原型阶段主要看两件事：这个玩法有没有「可赢」的路径，以及回合数是否够玩家操作。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("棋盘", GUILayout.Width(36f));
            _rules.BoardWidth = EditorGUILayout.IntField(_rules.BoardWidth, GUILayout.Width(40f));
            _rules.BoardHeight = EditorGUILayout.IntField(_rules.BoardHeight, GUILayout.Width(40f));

            EditorGUILayout.LabelField("每种元素初始数量", GUILayout.Width(130f));
            _rules.CreepsPerElement = EditorGUILayout.IntField(_rules.CreepsPerElement, GUILayout.Width(40f));

            EditorGUILayout.LabelField("每回合最多走", GUILayout.Width(90f));
            _rules.MaxMoveSteps = EditorGUILayout.IntField(_rules.MaxMoveSteps, GUILayout.Width(40f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("初始手牌", GUILayout.Width(60f));
            _rules.InitialHandSize = EditorGUILayout.IntField(_rules.InitialHandSize, GUILayout.Width(40f));

            EditorGUILayout.LabelField("瓶子容量", GUILayout.Width(60f));
            _rules.BottleCapacity = EditorGUILayout.IntField(_rules.BottleCapacity, GUILayout.Width(40f));

            EditorGUILayout.LabelField("回合上限", GUILayout.Width(60f));
            _rules.RoundLimit = EditorGUILayout.IntField(_rules.RoundLimit, GUILayout.Width(40f));

            EditorGUILayout.LabelField("胜利条件", GUILayout.Width(60f));
            _rules.WinCondition = (WinCondition)EditorGUILayout.EnumPopup(_rules.WinCondition, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("可打出的牌的作用", GUILayout.Width(130f));
            _rules.CardEffect = (CardEffectMode)EditorGUILayout.EnumPopup(_rules.CardEffect, GUILayout.Width(200f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("跑一次纯反应（玩家不出牌）", GUILayout.Height(28f))) RunSimulation(false, false);
            if (GUILayout.Button("跑一次含玩家最优出牌", GUILayout.Height(28f))) RunSimulation(true, false);
            if (GUILayout.Button($"批量 {_seeds} 局（含出牌）", GUILayout.Height(28f))) RunSimulation(true, true);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("批量局数", GUILayout.Width(60f));
            _seeds = EditorGUILayout.IntSlider(_seeds, 1, 2000);
            EditorGUILayout.LabelField("起始种子", GUILayout.Width(60f));
            _baseSeed = EditorGUILayout.IntField(_baseSeed, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 跑一批对局：准备阶段交给引擎一次跑完，玩家行动阶段交给 GreedyPlayer。
        /// 和 EditMode 里的平衡性测试共用同一份贪心策略，保证两边结论一致。
        /// </summary>
        void RunSimulation(bool playerPlays, bool batch)
        {
            var sb = new StringBuilder();
            var rules = _rules.Clone();

            try { rules.Validate(); }
            catch (Exception e)
            {
                _report = "参数不合法：" + e.Message;
                return;
            }

            int games = batch ? _seeds : 1;
            int wins = 0, defeats = 0, ended = 0;
            int totalRounds = 0;
            int totalCards = 0;
            int totalDiscards = 0;
            var endStates = new Dictionary<string, int>();
            var roundHistogram = new Dictionary<int, int>();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < games; i++)
            {
                var engine = new GameEngine(rules, _baseSeed + i);
                engine.Start();

                int guard = 0;
                while (!engine.IsOver && guard++ < rules.RoundLimit + 4)
                {
                    engine.EndPreparationNow();                 // 准备阶段 + 自动抽牌
                    if (engine.IsOver) break;
                    if (engine.Phase != GamePhase.PlayerAction) break;

                    if (playerPlays) totalCards += GreedyPlayer.Play(engine);
                    engine.EndPlayerPhase();                    // 出牌阶段 → 弃牌阶段

                    if (engine.Phase == GamePhase.Discard)
                    {
                        if (playerPlays) totalDiscards += GreedyPlayer.DiscardWorst(engine);
                        engine.EndDiscardPhase();               // 弃牌阶段 → 判定阶段
                    }

                    engine.ResolveJudgment();
                }

                totalRounds += engine.Round;
                roundHistogram.TryGetValue(engine.Round, out var h);
                roundHistogram[engine.Round] = h + 1;

                if (engine.Phase == GamePhase.Victory) wins++;
                else if (engine.Phase == GamePhase.Defeat) defeats++;
                if (engine.IsOver) ended++;

                string key = $"金{engine.Board.CountOf(Element.Metal)} 木{engine.Board.CountOf(Element.Wood)} "
                           + $"水{engine.Board.CountOf(Element.Water)} 火{engine.Board.CountOf(Element.Fire)} "
                           + $"土{engine.Board.CountOf(Element.Earth)}";
                endStates.TryGetValue(key, out var c);
                endStates[key] = c + 1;

                if (!batch)
                {
                    sb.AppendLine($"seed={_baseSeed + i}");
                    sb.AppendLine($"  结束阶段：{GameEngine.PhaseName(engine.Phase)}　回合：{engine.Round}");
                    sb.AppendLine($"  终局棋盘：{key}");
                    sb.AppendLine($"  累计消失 {engine.TotalRemoved} 个，生成 {engine.TotalSpawned} 个，出生失败 {engine.SpawnBlockedCount} 次");
                    sb.AppendLine($"  各元素消失次数：" + PerElement(engine, true));
                    sb.AppendLine($"  各元素生成次数：" + PerElement(engine, false));
                    sb.AppendLine($"  剩余手牌：{engine.Hand.Count} 张");
                }
            }

            sw.Stop();

            sb.AppendLine("──────── 汇总 ────────");
            sb.AppendLine($"局数 {games}　胜利 {wins}（{(games == 0 ? 0 : wins * 100f / games):F1}%）　" +
                          $"失败 {defeats}（{(games == 0 ? 0 : defeats * 100f / games):F1}%）　未结束 {games - ended}");
            sb.AppendLine($"平均回合数 {(games == 0 ? 0 : (float)totalRounds / games):F2}　"
                          + $"平均出牌 {(games == 0 ? 0f : (float)totalCards / games):F2} 张/局　"
                          + $"平均弃牌 {(games == 0 ? 0f : (float)totalDiscards / games):F2} 张/局　耗时 {sw.ElapsedMilliseconds} ms");
            sb.AppendLine($"胜利条件：{GameEngine.DescribeWinCondition(rules.WinCondition)}　牌的作用：{rules.CardEffect}　玩家出牌：{(playerPlays ? "是" : "否")}");

            sb.AppendLine();
            sb.AppendLine("回合数分布：");
            foreach (var kv in SortedKeys(roundHistogram))
                sb.AppendLine($"  第 {kv} 回合结束：{roundHistogram[kv]} 局");

            sb.AppendLine();
            sb.AppendLine("终局棋盘形态 Top 10：");
            int shown = 0;
            foreach (var kv in SortedByValue(endStates))
            {
                sb.AppendLine($"  {kv.Value,5} 局　{kv.Key}");
                if (++shown >= 10) break;
            }

            _report = sb.ToString();
        }

        static string PerElement(GameEngine engine, bool removed)
        {
            var parts = new List<string>();
            foreach (var e in ElementDefs.All)
                parts.Add($"{ElementDefs.Name(e)}{(removed ? engine.RemovedOf(e) : engine.SpawnedOf(e))}");
            return string.Join(" ", parts);
        }

        static IEnumerable<int> SortedKeys(Dictionary<int, int> d)
        {
            var keys = new List<int>(d.Keys);
            keys.Sort();
            return keys;
        }

        static IEnumerable<KeyValuePair<string, int>> SortedByValue(Dictionary<string, int> d)
        {
            var list = new List<KeyValuePair<string, int>>(d);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        // ------------------------------------------------------------ 说明

        void DrawHelp()
        {
            EditorGUILayout.LabelField("一回合流程", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "1) 准备阶段：每只元素小怪随机选一个方向，走 0~3 格；每走一格检查目标格：\n" +
                "   · 空格 → 走进去\n" +
                "   · 同元素 → 禁止本次移动，双方留在原地\n" +
                "   · 不同元素 → 立刻按反应表结算（一方消失，可能生成新元素），本次移动结束\n" +
                "2) 玩家行动阶段：小怪静止。玩家选一张手牌，再点棋盘目标格 → 发生一次反应；可以连续出牌，\n" +
                "   也可以直接点「结束回合」。\n" +
                "3) 判定阶段：达成胜利条件 → 胜利；回合数 > 上限 → 失败；否则进入下一回合。", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("两种资源循环", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "· 棋盘：元素小怪互相反应，数量随反应增减 —— 这是玩家要操控的主战场。\n" +
                "· 瓶子/手牌：棋盘上每消失 1 个元素，对应元素的瓶子 +1；满瓶（默认 5）自动换 1 张对应元素牌。\n" +
                "  于是「消耗某元素」同时也在攒对应资源，玩家需要判断先攒哪一路。", EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("原型里暂时没有做的（图2 里的复合元素）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "烬（木+火）、熔岩（火+土）、矿晶（土+金）、寒钢（金+水）、灵泉（水+木）这几行需要\n" +
                "「两个元素合成一个复合元素」的机制，属于第二阶段内容，目前只实现了五行基础反应。",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("导出规则说明到 Console", GUILayout.Height(24f))) DumpRules();
        }

        static void DumpRules()
        {
            var table = new ReactionTable();
            var sb = new StringBuilder();
            sb.AppendLine("TapGJ 元素反应表：");
            foreach (var a in ElementDefs.All)
            {
                foreach (var b in ElementDefs.All)
                {
                    if (a == b) continue;
                    var def = table.Get(a, b);
                    sb.AppendLine($"  {ElementDefs.Name(a)} 遇 {ElementDefs.Name(b)} → "
                        + (def.CreatesCreep
                            ? $"{ElementDefs.Name(b)}消失并生成 1 个{ElementDefs.Name(def.Spawn)}"
                            : $"{ElementDefs.Name(a)}消失"));
                }
            }
            Debug.Log(sb.ToString());
        }
    }
}
