using TapGJ.Flow;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// **调试用**的关卡解锁面板：不想一关一关打过去时，用它把后面的关卡打开。
    ///
    /// 三种用法：
    ///   · 菜单 <b>TapGJ ▸ 调试 ▸ 解锁下一关</b> —— 点一下，展平顺序里第一个还没通关的关卡算通关，
    ///     于是**下一关立刻解锁**（想快速往后推就用它，按一次推一关）
    ///   · 菜单 <b>TapGJ ▸ 调试 ▸ 全部解锁</b> —— 20 关全部打开
    ///   · 打开面板（<b>TapGJ ▸ 调试 ▸ 关卡解锁面板</b>）—— 每关一行，可以「解锁到这里」单点突破
    ///
    /// 它只改 PlayerPrefs 里的「已通关」标记，**不写最好成绩**（见 <see cref="LevelUnlockTools"/>）。
    /// 注意：菜单页的卡片是打开页面时建的，改完要**重新进一次章节页**才看得到新状态。
    /// </summary>
    public sealed class LevelDebugWindow : EditorWindow
    {
        const string CatalogPath = "Assets/UI/Data/ChapterCatalog.asset";

        ChapterCatalog _catalog;
        Vector2 _scroll;
        string _message;

        [MenuItem("TapGJ/调试/关卡解锁面板 (Level Debug)", false, 40)]
        public static void Open()
        {
            var window = GetWindow<LevelDebugWindow>("关卡调试");
            window.minSize = new Vector2(430, 320);
            window.Reload();
            window.Show();
        }

        [MenuItem("TapGJ/调试/解锁下一关 (Unlock Next)", false, 41)]
        public static void UnlockNextFromMenu()
        {
            var catalog = LoadCatalog();
            if (catalog == null) return;

            var cleared = LevelUnlockTools.UnlockNext(catalog);
            if (cleared == null)
            {
                Debug.Log("[关卡调试] 已经全部通关了，没有「下一关」可以解锁。");
                return;
            }

            var next = LevelUnlockTools.NextToUnlock(catalog);
            Debug.Log($"[关卡调试] 已把「{cleared.displayName}（{cleared.levelId}）」标成已通关"
                      + (next != null ? $" → 现在可以进「{next.displayName}（{next.levelId}）」" : " → 全部通关了")
                      + "。菜单页要重新进一次章节页才会刷新。");
        }

        [MenuItem("TapGJ/调试/全部解锁 (Unlock All)", false, 42)]
        public static void UnlockAllFromMenu()
        {
            var catalog = LoadCatalog();
            if (catalog == null) return;

            int changed = LevelUnlockTools.UnlockAll(catalog);
            Debug.Log($"[关卡调试] 全部解锁：新标了 {changed} 关，"
                      + $"现在 {LevelUnlockTools.ClearedCount(catalog)} / {LevelUnlockTools.TotalCount(catalog)} 已通关。");
        }

        [MenuItem("TapGJ/调试/清空进度 (Reset Progress)", false, 43)]
        public static void ResetAllFromMenu()
        {
            var catalog = LoadCatalog();
            if (catalog == null) return;

            int changed = LevelUnlockTools.ResetAll(catalog);
            Debug.Log($"[关卡调试] 已清空 {changed} 关的通关记录（只剩第一关能进）。");
        }

        static ChapterCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ChapterCatalog>(CatalogPath);
            if (catalog != null) return catalog;

            foreach (var guid in AssetDatabase.FindAssets("t:ChapterCatalog"))
            {
                catalog = AssetDatabase.LoadAssetAtPath<ChapterCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (catalog != null) return catalog;
            }

            Debug.LogError("[关卡调试] 找不到 ChapterCatalog，先确认 Assets/UI/Data/ChapterCatalog.asset 还在。");
            return null;
        }

        void OnEnable() => Reload();

        void Reload()
        {
            if (_catalog == null) _catalog = LoadCatalog();
        }

        void OnGUI()
        {
            Reload();
            if (_catalog == null)
            {
                EditorGUILayout.HelpBox("找不到 ChapterCatalog（Assets/UI/Data/ChapterCatalog.asset）。",
                    MessageType.Error);
                return;
            }

            DrawHeader();
            EditorGUILayout.Space(4);
            DrawBulkButtons();
            DrawMessage();
            EditorGUILayout.Space(4);
            DrawLevelList();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("章节配置", GUILayout.Width(56));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(_catalog, typeof(ChapterCatalog), false);
                EditorGUI.EndDisabledGroup();
            }

            int cleared = LevelUnlockTools.ClearedCount(_catalog);
            int total = LevelUnlockTools.TotalCount(_catalog);
            var next = LevelUnlockTools.NextToUnlock(_catalog);

            EditorGUILayout.LabelField($"已通关 {cleared} / {total} 关"
                                       + (next == null ? "　—　全部通关" : $"　—　下一关：{next.displayName}（{next.levelId}）"),
                EditorStyles.boldLabel);
        }

        void DrawBulkButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("解锁下一关", GUILayout.Height(26)))
                {
                    var cleared = LevelUnlockTools.UnlockNext(_catalog);
                    _message = cleared == null
                        ? "已经全部通关了，没有「下一关」。"
                        : $"已把「{cleared.displayName}（{cleared.levelId}）」标成已通关 → 下一关解锁。";
                }

                if (GUILayout.Button("全部解锁", GUILayout.Height(26)))
                {
                    int changed = LevelUnlockTools.UnlockAll(_catalog);
                    _message = $"新标了 {changed} 关（现在 {LevelUnlockTools.ClearedCount(_catalog)} / "
                               + $"{LevelUnlockTools.TotalCount(_catalog)} 已通关）。";
                }

                if (GUILayout.Button("清空进度", GUILayout.Height(26)))
                {
                    if (EditorUtility.DisplayDialog("清空进度",
                            "把这份目录里所有关卡的「已通关」标记删掉（只剩第一关能进）。确定？", "清空", "算了"))
                    {
                        int changed = LevelUnlockTools.ResetAll(_catalog);
                        _message = $"清掉了 {changed} 关的通关记录。";
                    }
                }

                if (GUILayout.Button("刷新", GUILayout.Height(26), GUILayout.Width(56))) Repaint();
            }
        }

        void DrawMessage()
        {
            if (string.IsNullOrEmpty(_message)) return;

            EditorGUILayout.HelpBox(_message + "\n（菜单页的卡片是打开页面时建的：回菜单重新进一次章节页才会刷新）",
                MessageType.Info);
        }

        void DrawLevelList()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int c = 0; c < _catalog.chapters.Length; c++)
            {
                var chapter = _catalog.chapters[c];
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"{chapter.title}　{chapter.description?.Replace("\n", " · ")}",
                    EditorStyles.boldLabel);

                if (chapter.levels == null) continue;

                for (int i = 0; i < chapter.levels.Length; i++)
                    DrawLevelRow(c, i);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawLevelRow(int chapterIndex, int levelIndex)
        {
            var entry = _catalog.LevelAt(chapterIndex, levelIndex);
            if (entry == null) return;

            bool bound = entry.definition != null;
            bool cleared = bound && ProgressStore.IsCleared(entry.definition.levelId);
            bool unlocked = bound && _catalog.IsUnlocked(chapterIndex, levelIndex);

            using (new EditorGUILayout.HorizontalScope())
            {
                var status = bound ? _catalog.Badge(chapterIndex, levelIndex) : "待开放";
                var id = bound ? entry.definition.levelId : "（没绑关卡）";
                GUI.color = cleared ? new Color(0.55f, 0.85f, 0.55f)
                    : unlocked ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                EditorGUILayout.LabelField($"{entry.title}　{id}　[{status}]", GUILayout.MinWidth(200));
                GUI.color = Color.white;

                EditorGUI.BeginDisabledGroup(!bound || (!cleared && unlocked));
                if (GUILayout.Button("解锁到这里", GUILayout.Width(88)))
                {
                    int changed = LevelUnlockTools.UnlockUpTo(_catalog, entry.definition);
                    _message = $"为「{entry.title}（{entry.definition.levelId}）」解锁：新标了 {changed} 关。";
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!cleared);
                if (GUILayout.Button("取消通关", GUILayout.Width(76)))
                {
                    ProgressStore.Reset(entry.definition.levelId);
                    _message = $"「{entry.title}」已改回未通关。";
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!bound);
                if (GUILayout.Button("进入", GUILayout.Width(52)))
                {
                    // 只在 Play 模式里有效：这时 SceneFlow 才能真的加载 Game 场景。
                    if (!EditorApplication.isPlaying)
                        _message = "「进入」只在 Play 模式可用 —— 先点 Play 进菜单，再按它直接跳进这一关。";
                    else if (entry.definition != null)
                        SceneFlow.LoadLevel(entry.definition);
                }
                EditorGUI.EndDisabledGroup();
            }
        }
    }
}
