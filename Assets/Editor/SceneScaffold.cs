using System.Linq;
using TapGJ.Core;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.EditorTools
{
    /// <summary>
    /// 场景脚手架：把本工程需要的 GameObject 结构直接建到当前场景里，方便手工搭 / 改。
    ///
    /// 本工程刻意做成「代码自己搭」的同时，也允许你把结构摆进场景：
    ///   GameRunner  → 回合流程主控（规则、种子、播放速度都在它的 Inspector 上）
    ///   └─ Board    → 棋盘渲染 + 点击拾取（BoardView）
    ///   Main Camera → 正交相机（GameRunner 会在运行时接管它的位置与大小）
    /// HUD 没有实体对象：它是 IMGUI，挂在 GameRunner 上画。
    /// </summary>
    public static class SceneScaffold
    {
        const string MenuRoot = "TapGJ/";

        [MenuItem(MenuRoot + "在场景里创建游戏主控 (GameRunner)", false, 1)]
        public static void CreateGameRunner()
        {
            var existing = Object.FindFirstObjectByType<GameRunner>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog("TapGJ",
                    "场景里已经有 GameRunner 了，已帮你选中它。\n\n" +
                    "如果不需要两份，先删掉其中一份再执行本菜单。", "好");
                return;
            }

            var go = new GameObject("GameRunner");
            Undo.RegisterCreatedObjectUndo(go, "Create GameRunner");
            var runner = go.AddComponent<GameRunner>();

            // 棋盘子物体：让结构在 Hierarchy 里可见、可调
            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(go.transform, false);
            boardGo.AddComponent<BoardView>();

            EnsureMainCamera();

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);

            Debug.Log("[TapGJ] 已在场景里创建 GameRunner（含 Board 子物体）。\n" +
                      "规则参数在 GameRunner 的 Inspector 上；点 Play 即可开玩。\n" +
                      "注意：场景里没有 GameRunner 时，运行时也会自动创建一个，所以两种用法都行。");
        }

        [MenuItem(MenuRoot + "在选中物体下添加 Board", false, 2)]
        public static void AddBoardUnderSelection()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("TapGJ", "请先在 Hierarchy 里选中一个物体（通常是 GameRunner）。", "好");
                return;
            }

            if (selected.GetComponent<GameRunner>() == null)
                Debug.LogWarning("[TapGJ] 选中的物体上没有 GameRunner —— Board 应该是它的子物体，否则运行时不会被用到。");

            if (selected.GetComponentInChildren<BoardView>(true) != null)
            {
                EditorUtility.DisplayDialog("TapGJ", "它下面已经有 BoardView 了。", "好");
                return;
            }

            var boardGo = new GameObject("Board");
            Undo.RegisterCreatedObjectUndo(boardGo, "Add Board");
            boardGo.transform.SetParent(selected.transform, false);
            boardGo.AddComponent<BoardView>();
            Selection.activeGameObject = boardGo;
        }

        [MenuItem(MenuRoot + "校验当前场景", false, 20)]
        public static void ValidateScene()
        {
            var runner = Object.FindFirstObjectByType<GameRunner>();
            var cam = Camera.main;
            var boardInScene = Object.FindFirstObjectByType<BoardView>();

            var report = new System.Text.StringBuilder();
            report.AppendLine("TapGJ 场景校验");
            report.AppendLine("──────────────");
            report.AppendLine(runner != null
                ? "✔ GameRunner：场景里已有（点 Play 用的就是这一份）"
                : "· GameRunner：场景里没有 → 运行时会自动创建一个，也能玩；想手工调参就执行「在场景里创建游戏主控」");
            report.AppendLine(cam != null
                ? $"✔ Main Camera：{cam.name}（正交={cam.orthographic}，大小={cam.orthographicSize}）"
                  + "　运行时位置与大小会被 GameRunner 接管"
                : "✘ Main Camera：没有 → 运行时会自动创建一个");
            report.AppendLine(cam != null && !cam.orthographic
                ? "  ⚠ 相机不是正交模式，2D 棋盘会看起来不对（运行时会被强制改成正交）"
                : null);
            report.AppendLine(boardInScene != null
                ? $"· BoardView：场景里有（{boardInScene.name}）"
                  + (runner != null && boardInScene.transform.IsChildOf(runner.transform)
                      ? "，且是 GameRunner 的子物体 ✔"
                      : "，但不在 GameRunner 下面 → 运行时 GameRunner 会找不到它并另建一个")
                : "· BoardView：场景里没有 → 运行时由 GameRunner 自动创建（正常）");
            report.AppendLine(boardInScene != null && boardInScene.GetComponentsInChildren<CellView>(true).Length > 0
                ? "  ⚠ 这个 BoardView 下面已经有格子了：格子是运行时生成的，手放的会被清掉"
                : null);
            report.AppendLine("· HUD：IMGUI，运行时挂在 GameRunner 上，不需要场景对象");

            var text = report.ToString();
            Debug.Log("[TapGJ] " + text.Replace("\n", "\n[TapGJ] "));
            EditorUtility.DisplayDialog("TapGJ 场景校验", text, "好");
        }

        static void EnsureMainCamera()
        {
            if (Camera.main != null) return;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            Undo.RegisterCreatedObjectUndo(camGo, "Create Main Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.10f);
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            camGo.AddComponent<AudioListener>();

            Debug.Log("[TapGJ] 场景里没有 Main Camera，已自动补一个正交相机。");
        }

        // ------------------------------------------------------------ 运行时探针

        [MenuItem(MenuRoot + "打印当前对局状态（仅 Play 时）", false, 40)]
        public static void DumpRuntimeState()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("TapGJ", "这个功能只在 Play 模式下有用。", "好");
                return;
            }

            var runner = Object.FindFirstObjectByType<GameRunner>();
            if (runner == null || runner.Engine == null)
            {
                EditorUtility.DisplayDialog("TapGJ", "没找到正在运行的 GameRunner。", "好");
                return;
            }

            var e = runner.Engine;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"阶段：{GameEngine.PhaseName(e.Phase)}　回合：{e.Round}/{e.Rules.RoundLimit}");
            sb.AppendLine($"场上：{e.StatusLine()}");
            sb.AppendLine($"瓶子：{e.Bottle.DebugLine()}");
            sb.AppendLine($"手牌：{e.Hand.Count} 张　[出牌] {string.Join(" ", e.Hand.Select(c => c.Label))}");
            sb.AppendLine($"累计消失 {e.TotalRemoved}　生成 {e.TotalSpawned}　出生失败 {e.SpawnBlockedCount}");
            sb.AppendLine($"胜利条件：{e.WinConditionDescription()}");
            sb.AppendLine($"Board 位置：{runner.Board.transform.position}　相机正交大小：{runner.Cam.orthographicSize}");

            Debug.Log("[TapGJ 对局状态]\n" + sb);
        }
    }
}
