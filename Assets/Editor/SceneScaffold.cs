using System.Collections.Generic;
using System.IO;
using System.Linq;
using TapGJ.Core;
using TapGJ.Levels;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 场景工具。
    ///
    /// 本工程**不**在运行时自动创建任何东西：场景里有什么就是什么，漏了引用会明确报错。
    /// 所以这里提供三个东西：
    ///   1. 「在当前场景搭建骨架」—— 想省事时可以一键搭出层级并连好引用（可撤销、不写文件）
    ///   2. 「新建 Game 场景（含骨架）」—— 只在这个场景不存在时创建，绝不覆盖你手搭的场景
    ///   3. 「校验当前场景」—— 逐条列出哪个物体的哪个字段没连
    ///
    /// 手工搭建的完整接线清单见 README 第七节。
    /// </summary>
    public static class SceneScaffold
    {
        const string MenuRoot = "TapGJ/";
        const string GameScenePath = "Assets/Scenes/Game.unity";

        // 骨架的默认摆放（你可以随便改，相机构图是场景作者的事）
        static readonly Vector3 BoardAnchorPos = new Vector3(0f, 1.0f, 0f);
        static readonly Vector3 HandAnchorPos = new Vector3(0f, -4.8f, 0f);
        const float DefaultOrthoSize = 6.5f;

        // ---------------------------------------------------------------- 一键骨架

        [MenuItem(MenuRoot + "在当前场景搭建/补齐骨架", false, 1)]
        public static void BuildSkeletonInCurrentScene()
        {
            var session = BuildSkeleton();
            Selection.activeGameObject = session;

            Debug.Log("[TapGJ] 骨架已就绪（**已有的物体不会被重建或移动**，只补缺的部分）：\n"
                      + "  Systems：GameSession（+ 临时 IMGUI HUD）、ClickRouter、LevelLoader\n"
                      + "  Anchors：BoardAnchor（BoardView）、HandAnchor（HandView）\n"
                      + "  LevelRoot + Main Camera（缺相机才补）\n"
                      + "  Canvas + EventSystem（缺才补；正式 uGUI 直接往 Canvas 下面加）\n"
                      + "引用已经连好；相机构图和锚点位置请自己调。\n"
                      + "LevelLoader.catalog 故意留空：跑一次 TapGJ ▸ 新建关卡 会自动接上。");
        }

        [MenuItem(MenuRoot + "新建 Game 场景（含骨架）", false, 2)]
        public static void CreateGameScene()
        {
            if (File.Exists(GameScenePath))
            {
                EditorUtility.DisplayDialog("TapGJ",
                    $"{GameScenePath} 已经存在，本菜单不会覆盖它。\n\n"
                    + "想重新生成就先自己删掉或改名；已经打开的旧场景不会被改动。", "好");
                return;
            }

            // 新建场景会丢掉当前未保存的改动，先问一句
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildSkeleton();

            Directory.CreateDirectory(Path.GetDirectoryName(GameScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, GameScenePath);
            AssetDatabase.Refresh();
            AddToBuildSettings(GameScenePath);

            Debug.Log($"[TapGJ] 已创建 {GameScenePath}（含骨架，并加入 Build Settings）。"
                      + "菜单 TapGJ ▸ 校验当前场景 可以检查接线。");
        }

        /// <summary>
        /// 搭出（或补齐）层级并连好所有必需引用，返回 GameSession 物体。
        /// **幂等**：已有的组件不会被重建、已有的锚点位置不会被移动，只补缺的部分 + 重新接线。
        /// 这样老场景升到新版本时（比如 P2 新增了 LevelLoader）直接再跑一次就行。
        ///
        /// public 是给 EditMode 测试用的：测试直接调它，就能验证「接线清单里的字段名还有效」——
        /// 比让测试去读文件、或者靠人去点菜单可靠得多。
        /// </summary>
        public static GameObject BuildSkeleton()
        {
            _wiringFailures.Clear();

            var systems = FindOrCreateRoot("Systems");
            var anchors = FindOrCreateRoot("Anchors");
            var levelRoot = FindOrCreateRoot("LevelRoot"); // 关卡预制体的挂载点

            // ---- Systems
            var session = FindOrCreate<GameSession>(systems, "GameSession");
            if (session.GetComponent<Hud>() == null) session.gameObject.AddComponent<Hud>(); // 过渡期 IMGUI HUD（实现 IGameHud，删掉它不影响主控）
            var router = FindOrCreate<ClickRouter>(systems, "ClickRouter");
            var loader = FindOrCreate<LevelLoader>(systems, "LevelLoader");

            // ---- Anchors（已存在的锚点保持原位，不动它）
            var board = FindOrCreate<BoardView>(anchors, "BoardAnchor", BoardAnchorPos);
            var hand = FindOrCreate<HandView>(anchors, "HandAnchor", HandAnchorPos);

            // ---- 相机
            var cam = EnsureMainCamera();

            // ---- UI 根：Canvas + EventSystem 先备好，正式 UI 直接往里加就行
            EnsureUiRoot();

            // ---- 接线（catalog / inlineLevel 留给关卡流程：新建关卡 菜单会接 catalog）
            SetRef(router, "worldCamera", cam);
            SetRef(session, "board", board);
            SetRef(session, "hand", hand);
            SetRef(session, "input", router);
            SetRef(loader, "session", session);
            SetRef(loader, "levelRoot", levelRoot.transform);

            return session.gameObject;
        }

        /// <summary>找场景里同名的根物体；没有才新建（幂等）。</summary>
        static GameObject FindOrCreateRoot(string name)
        {
            var existing = GameObject.Find(name);
            return existing != null ? existing : NewChild(null, name);
        }

        /// <summary>找场景里已有的组件；没有才在指定父物体下新建。</summary>
        static T FindOrCreate<T>(GameObject parent, string name, Vector3? localPosition = null) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>();
            if (existing != null) return existing;

            var go = NewChild(parent, name);
            if (localPosition.HasValue) go.transform.position = localPosition.Value;
            return go.AddComponent<T>();
        }

        static GameObject NewChild(GameObject parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "TapGJ 搭骨架");
            if (parent != null) go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <summary>
        /// 给私有 [SerializeField] 字段赋值。
        /// 字段改名时这里会明确报错，而不是静默失效 —— 记得同步改 README 的接线清单。
        /// 失败会记进 <see cref="LastWiringFailures"/>，EditMode 测试会断言它是空的。
        /// </summary>
        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                var message = $"{target.GetType().Name} 上找不到字段「{field}」——"
                              + "字段被改名了？请同步更新 SceneScaffold 与 README 第七节的接线清单。";
                _wiringFailures.Add(message);
                Debug.LogError("[TapGJ] " + message);
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static readonly List<string> _wiringFailures = new List<string>();

        /// <summary>上一次搭骨架时连不上的字段（正常应该是空的）。</summary>
        public static IReadOnlyList<string> LastWiringFailures => _wiringFailures;

        static Camera EnsureMainCamera()
        {
            var cam = Camera.main;
            if (cam != null) return cam;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Undo.RegisterCreatedObjectUndo(camGo, "Create Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = DefaultOrthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();

            Debug.Log("[TapGJ] 场景里没有 Main Camera，已补一个正交相机（大小 " + DefaultOrthoSize + "）。");
            return cam;
        }

        /// <summary>
        /// 备好 uGUI 的两件套：Canvas（+ CanvasScaler + GraphicRaycaster）和 EventSystem。
        /// **幂等**：已有就复用，不会重建或移动。
        ///
        /// 为什么脚手架要管这个：uGUI 想收到点击就**必须**有 EventSystem，少一个的表现是
        /// 「按钮点了没反应」，很容易查半天；而空 Canvas 没有 Graphic 就没有 raycast 目标，
        /// 所以它**不会**挡住棋盘点击，对现在的 IMGUI HUD 也毫无影响。
        /// </summary>
        static Canvas EnsureUiRoot()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var go = NewChild(null, "Canvas");
                canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                go.AddComponent<UnityEngine.UI.CanvasScaler>();
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                Debug.Log("[TapGJ] 场景里没有 Canvas，已补一个（ScreenSpaceOverlay + CanvasScaler + GraphicRaycaster）。"
                          + "正式 UI 直接往它下面加就行。");
            }

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var go = NewChild(null, "EventSystem");
                go.AddComponent<UnityEngine.EventSystems.EventSystem>();
                go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Debug.Log("[TapGJ] 场景里没有 EventSystem，已补一个 —— 没有它 uGUI 收不到任何点击。");
            }

            return canvas;
        }

        static void AddToBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (list.Any(s => s.path == path)) return;

            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ---------------------------------------------------------------- 关卡

        /// <summary>关卡资产都放这个目录下（下面的 EnsureFolder 会按段创建）。</summary>
        const string LevelDir = "Assets/Levels";
        const string CatalogPath = LevelDir + "/LevelCatalog.asset";

        [MenuItem(MenuRoot + "新建关卡（定义 + 预制体 + 加入目录）", false, 3)]
        public static void CreateLevel()
        {
            var level = CreateLevelAsset();
            if (level == null) return;

            Selection.activeObject = level;
            EditorGUIUtility.PingObject(level);

            Debug.Log($"[TapGJ] 已创建 {AssetDatabase.GetAssetPath(level)} 与 "
                      + $"{AssetDatabase.GetAssetPath(level.levelPrefab)}，并加入目录。\n"
                      + "下一步：在 Inspector 里填 layoutText（字符画）、可用元素/卡牌、胜利目标，"
                      + "面板下半部分会实时告诉你还有没有问题；然后点「Play 这一关」。");
        }

        /// <summary>
        /// 真正建资产的逻辑（不碰 Selection / Ping / 对话框）。
        ///
        /// 三个参数是为了让测试能建在一个**隔离的临时目录**里 ——
        /// 测试要是往真正的 `Assets/Levels` + `LevelCatalog.asset` 里写，
        /// 一旦中途失败，就会在你真实的关卡目录里留下一条指向已删资产的悬空引用（踩过一次）。
        /// </summary>
        public static LevelDefinition CreateLevelAsset(
            string levelDir = LevelDir,
            string catalogPath = CatalogPath,
            bool wireIntoScene = true)
        {
            // ⚠ AssetDatabase.CreateFolder 的第二个参数是**文件夹名**，不是路径 ——
            //    以前写成 CreateFolder("Assets", "Assets/Levels")，结果建出一个叫
            //    「Assets_Levels」的杂物目录，而 CreateAsset 因为 Assets/Levels 不存在直接失败。
            EnsureFolder(levelDir);

            // 找一个没被占用的编号
            int n = 1;
            string soPath;
            do
            {
                soPath = $"{levelDir}/Level_{n:D2}.asset";
                n++;
            } while (AssetDatabase.LoadAssetAtPath<LevelDefinition>(soPath) != null);
            n--; // 回退到真正空闲的编号
            string prefabPath = $"{levelDir}/Level_{n:D2}.prefab";

            var level = ScriptableObject.CreateInstance<LevelDefinition>();
            level.levelId = $"level_{n:D2}";
            level.displayName = $"第 {n} 关";
            level.rules = new Rules();
            AssetDatabase.CreateAsset(level, soPath);

            // 关卡预制体：先给一个空壳，美术/机制往里面加
            // 注意：SaveAsPrefabAsset 的返回值在不同 Unity 版本里可能是实例也可能是资产，
            // 所以这里不依赖它 —— 存完就从资产路径回读，保证 levelPrefab 指向的是真正的资产。
            var instance = new GameObject($"Level_{n:D2}");
            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            Object.DestroyImmediate(instance);

            level.levelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (level.levelPrefab == null)
                Debug.LogWarning($"[TapGJ] 关卡预制体 {prefabPath} 没建成功，请手工建一个再拖进 levelPrefab。");
            EditorUtility.SetDirty(level);

            // 加入目录（没有就新建）
            var catalog = EnsureCatalog(catalogPath);
            var list = new List<LevelDefinition>(catalog.levels ?? new LevelDefinition[0]);
            list.Add(level);
            catalog.levels = list.ToArray();
            EditorUtility.SetDirty(catalog);

            // 顺手把目录接到当前场景的 LevelLoader 上（如果有）
            if (wireIntoScene) WireCatalogIntoScene(catalog);

            AssetDatabase.SaveAssets();
            return level;
        }

        /// <summary>按路径一段一段建目录（"Assets/Levels/Sub" 也能建）。</summary>
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            var parts = path.Split('/');
            var current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static LevelCatalog EnsureCatalog(string catalogPath)
        {
            EnsureFolder(Path.GetDirectoryName(catalogPath).Replace('\\', '/'));

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>(catalogPath);
            if (catalog != null) return catalog;

            catalog = ScriptableObject.CreateInstance<LevelCatalog>();
            AssetDatabase.CreateAsset(catalog, catalogPath);
            return catalog;
        }

        static void WireCatalogIntoScene(LevelCatalog catalog)
        {
            var loader = Object.FindFirstObjectByType<LevelLoader>();
            if (loader == null) return;
            SetRef(loader, "catalog", catalog);
        }

        // ---------------------------------------------------------------- 校验

        [MenuItem(MenuRoot + "校验当前场景", false, 20)]
        public static void ValidateScene()
        {
            var issues = SceneValidator.Collect(SceneManager.GetActiveScene());
            var text = SceneValidator.Report(issues);

            Debug.Log("[TapGJ] " + text.Replace("\n", "\n[TapGJ] "));
            EditorUtility.DisplayDialog("TapGJ 场景校验", text, "好");
        }

        // ---------------------------------------------------------------- 关卡校验

        [MenuItem(MenuRoot + "校验所有关卡（目录 + 全部关卡资产）", false, 21)]
        public static void ValidateAllLevels()
        {
            var levels = new List<LevelDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:LevelDefinition"))
                levels.Add(AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(guid)));

            var catalog = AssetDatabase.LoadAssetAtPath<LevelCatalog>("Assets/Levels/LevelCatalog.asset");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共 {levels.Count} 个关卡资产，目录 {(catalog != null ? catalog.levels?.Length ?? 0 : 0)} 关。");

            // 目录里的关卡必须都存在、且 id 不重复（进度存档和菜单跳转都按 id 走）
            if (catalog != null && catalog.levels != null)
            {
                var seen = new Dictionary<string, int>();
                for (int i = 0; i < catalog.levels.Length; i++)
                {
                    var lv = catalog.levels[i];
                    if (lv == null)
                    {
                        sb.AppendLine($"\n✘ 目录第 {i + 1} 项是 None（关卡被删了但目录没清）。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(lv.levelId)) continue;
                    if (seen.TryGetValue(lv.levelId, out var first))
                        sb.AppendLine($"\n✘ levelId「{lv.levelId}」重复：目录第 {first + 1} 项和第 {i + 1} 项。"
                                      + "进度存档只看 id，两关会互相覆盖通关状态。");
                    else
                        seen[lv.levelId] = i;
                }
            }

            int bad = 0;
            foreach (var level in levels)
            {
                var issues = new List<string>();
                level.CollectAuthoringIssues(issues);
                if (issues.Count == 0) continue;

                bad++;
                sb.AppendLine($"\n⚠ {level.displayName}（{level.levelId}）");
                foreach (var issue in issues) sb.AppendLine($"    · {issue}");
            }

            sb.AppendLine(bad == 0 ? "\n✔ 所有关卡都没发现配置问题。" : $"\n共 {bad} 个关卡有问题。");

            var text = sb.ToString();
            Debug.Log("[TapGJ 关卡校验] " + text.Replace("\n", "\n[TapGJ] "));
            EditorUtility.DisplayDialog("TapGJ 关卡校验", text, "好");
        }

        // ---------------------------------------------------------------- 运行时探针

        [MenuItem(MenuRoot + "打印当前对局状态（仅 Play 时）", false, 40)]
        public static void DumpRuntimeState()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("TapGJ", "这个功能只在 Play 模式下有用。", "好");
                return;
            }

            var session = Object.FindFirstObjectByType<GameSession>();
            if (session == null || session.Engine == null)
            {
                EditorUtility.DisplayDialog("TapGJ", "没找到正在运行的 GameSession。", "好");
                return;
            }

            var e = session.Engine;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"阶段：{GameEngine.PhaseName(e.Phase)}　回合：{e.Round}/{e.Rules.RoundLimit}");
            sb.AppendLine($"场上：{e.StatusLine()}");
            sb.AppendLine($"瓶子：{e.Bottle.DebugLine()}");
            sb.AppendLine($"手牌：{e.Hand.Count} 张　[出牌] {string.Join(" ", e.Hand.Select(c => c.Label))}");
            sb.AppendLine($"累计消失 {e.TotalRemoved}（相克）　相生转换 {e.TotalTransformed}　生成 {e.TotalSpawned}　召唤 {e.TotalSummoned}　出生失败 {e.SpawnBlockedCount}");
            sb.AppendLine($"胜利条件：{e.WinConditionDescription()}");
            sb.AppendLine($"Board 位置：{session.Board.transform.position}　相机正交大小：{(Camera.main != null ? Camera.main.orthographicSize : 0f)}");

            Debug.Log("[TapGJ 对局状态]\n" + sb);
        }
    }
}
