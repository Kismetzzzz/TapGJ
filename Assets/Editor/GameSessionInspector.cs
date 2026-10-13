using TapGJ.Core;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// GameSession 的 Inspector：把「规则到底是谁说了算」摆在最上面。
    ///
    /// 场景里挂了 LevelLoader 时，规则来自关卡定义（LevelDefinition.rules），
    /// 所以 GameSession 上那份「开发兜底规则」会被置灰 —— 编辑它没有任何效果，
    /// 与其让人改半天没反应，不如直接不让改。
    /// </summary>
    [CustomEditor(typeof(GameSession))]
    public sealed class GameSessionInspector : UnityEditor.Editor
    {
        SerializedProperty _board;
        SerializedProperty _hand;
        SerializedProperty _input;
        SerializedProperty _rules;
        SerializedProperty _seed;
        SerializedProperty _stepDuration;
        SerializedProperty _stepPause;
        SerializedProperty _animations;

        void OnEnable()
        {
            _board = serializedObject.FindProperty("board");
            _hand = serializedObject.FindProperty("hand");
            _input = serializedObject.FindProperty("input");
            _rules = serializedObject.FindProperty("Rules");
            _seed = serializedObject.FindProperty("Seed");
            _stepDuration = serializedObject.FindProperty("StepDuration");
            _stepPause = serializedObject.FindProperty("StepPause");
            _animations = serializedObject.FindProperty("AnimationsEnabled");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var session = (GameSession)target;
            var loader = Object.FindObjectOfType<LevelLoader>();
            bool levelDriven = loader != null;
            bool playing = Application.isPlaying && session.Engine != null;

            DrawRuleSource(session, loader, levelDriven, playing);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("必需引用", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_board, new GUIContent("棋盘 BoardView"));
            EditorGUILayout.PropertyField(_hand, new GUIContent("手牌 HandView"));
            EditorGUILayout.PropertyField(_input, new GUIContent("输入 ClickRouter"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("开发兜底规则", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(levelDriven))
            {
                EditorGUILayout.PropertyField(_rules, new GUIContent("Rules"), true);
            }

            if (levelDriven)
            {
                EditorGUILayout.HelpBox(
                    "本场景有 LevelLoader：实际规则来自关卡定义（LevelDefinition.rules），这份兜底规则不会生效。" +
                    "要改规则请选中关卡资产（Assets/Levels/Level_*.asset）。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "本场景没有 LevelLoader：会用这份兜底规则随机铺盘开局。" +
                    "要做关卡请跑菜单 TapGJ ▸ 在当前场景搭建/补齐骨架。",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("随机种子", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_seed, new GUIContent("Seed"));
            if (levelDriven)
            {
                EditorGUILayout.LabelField("实际种子",
                    playing ? session.Seed.ToString() : "（关卡定义决定：固定种子 / 菜单传入 / 随机）");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("播放", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_stepDuration, new GUIContent("每步时长"));
            EditorGUILayout.PropertyField(_stepPause, new GUIContent("每步间隔"));
            EditorGUILayout.PropertyField(_animations, new GUIContent("播放动画"));

            if (playing) DrawLiveStatus(session);

            serializedObject.ApplyModifiedProperties();
        }

        void DrawRuleSource(GameSession session, LevelLoader loader, bool levelDriven, bool playing)
        {
            if (!levelDriven)
            {
                EditorGUILayout.HelpBox("规则来源：场景兜底（没有 LevelLoader）", MessageType.None);
                return;
            }

            // 场景里有 LevelLoader 不等于它成功开局了（缺引用 / 没解析到关卡时会提前 return，
            // 于是 GameSession 自己用兜底规则开了局）。这种情况必须说清楚，否则改规则会改错地方。
            if (playing && !session.RulesFromLevel)
            {
                EditorGUILayout.HelpBox(
                    "场景里有 LevelLoader，但它没能开局（缺少引用 / 目录为空 / 没指 inlineLevel），"
                    + "所以现在生效的是 GameSession 上的兜底规则。看 Console 里 LevelLoader 报的错。",
                    MessageType.Warning);
                return;
            }

            var level = playing ? loader.CurrentLevel : null;
            string name = level != null
                ? $"{level.displayName}（{level.levelId}）"
                : "（Play 之后显示关卡名）";

            EditorGUILayout.HelpBox($"规则来源：关卡定义 {name}", MessageType.None);

            if (!playing) return;

            var rules = session.ActiveRules;
            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("棋盘", $"{rules.BoardWidth} × {rules.BoardHeight}");
            EditorGUILayout.LabelField("每回合出牌上限", rules.MaxCardsPerRound <= 0 ? "不限" : rules.MaxCardsPerRound.ToString());
            EditorGUILayout.LabelField("回合上限", rules.RoundLimit.ToString());
            EditorGUILayout.LabelField("胜利条件", session.Engine.WinConditionDescription());
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }

        void DrawLiveStatus(GameSession session)
        {
            var engine = session.Engine;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("当前对局", EditorStyles.boldLabel);

            var progress = engine.GetGoalProgress();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.LabelField("阶段", GameEngine.PhaseName(engine.Phase));
            EditorGUILayout.LabelField("回合", $"{engine.Round} / {engine.Rules.RoundLimit}");
            EditorGUILayout.LabelField("场上元素", engine.CreepCount.ToString());
            EditorGUILayout.LabelField("手牌", engine.Hand.Count.ToString());
            EditorGUILayout.LabelField("目标进度", string.IsNullOrEmpty(progress.Label)
                ? $"{progress.Description}（{progress.Fill:P0}）"
                : $"{progress.Label}（{progress.Fill:P0}）");
            EditorGUILayout.LabelField("关卡机制", engine.LevelRules.Count.ToString());
            EditorGUI.EndDisabledGroup();
        }
    }
}
