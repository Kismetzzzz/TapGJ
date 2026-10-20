using System;
using System.IO;
using System.Linq;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TapGJ.EditorTools
{
    /// <summary>复用现有美术界面，把保存的玩法场景接到真实关卡。</summary>
    public static class BattleGameplayInstaller
    {
        const string GamePath = "Assets/Scenes/Game.unity";
        const string UiPath = "Assets/Scenes/BattleUI.unity";
        const string PrefabPath = "Assets/UI/Prefabs/BattleGameplay.prefab";
        static readonly Color Ink = new Color(.21f, .27f, .31f);

        [MenuItem("TapGJ/接入现有局内 UI", false, 6)]
        public static void Install()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再接入局内 UI。");
            string backup = "Artifacts/BattleUiIntegration/Game-before-integration.unity";
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(GamePath, backup);
            var sourceScene = SceneManager.GetSceneByPath(UiPath);
            bool openedSource = !sourceScene.isLoaded;
            if (openedSource) sourceScene = EditorSceneManager.OpenScene(UiPath, OpenSceneMode.Additive);
            var gameScene = SceneManager.GetSceneByPath(GamePath);
            if (!gameScene.isLoaded) gameScene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Additive);
            var session = Find<GameSession>(gameScene);
            var oldHud = Find<Hud>(gameScene);
            if (oldHud != null) Object.DestroyImmediate(oldHud);
            session.Hand.ShowCards = false;
            session.Board.TransparentCells = true;
            session.Input.enabled = false;
            var previous = Find<BattleGameplayHud>(gameScene);
            if (previous != null) Object.DestroyImmediate(previous.gameObject);
            var oldBoardCamera = gameScene.GetRootGameObjects().FirstOrDefault(g => g.name == "BoardRenderCamera");
            if (oldBoardCamera != null) Object.DestroyImmediate(oldBoardCamera);

            var source = Find<BattleUiController>(sourceScene);
            var copy = Object.Instantiate(source.gameObject);
            copy.name = "BattleGameplay";
            foreach (var child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
            SceneManager.MoveGameObjectToScene(copy, gameScene);
            var ui = copy.GetComponent<BattleUiController>();
            SetBool(ui, "previewMode", false);
            ApplyHandArea(ui);
            ui.ArrangeHand();
            var composition = copy.transform.Find("Composition");
            var hint = (RectTransform)composition.Find("Controls/SelectionLabel");
            hint.anchoredPosition = new Vector2(470, -163);
            hint.sizeDelta = new Vector2(1020, 62);
            hint.GetComponent<Text>().fontSize = 20;
            var objective = composition.Find("Controls/TaskPanel/Objective").GetComponent<Text>();
            objective.resizeTextForBestFit = true;
            objective.resizeTextMinSize = 18;
            objective.resizeTextMaxSize = 26;
            var progress = composition.Find("Controls/TaskPanel/Progress").GetComponent<Text>();
            progress.resizeTextForBestFit = true;
            progress.resizeTextMinSize = 16;
            progress.resizeTextMaxSize = 20;
            var viewport = ui.BoardContent.gameObject.AddComponent<BattleBoardViewport>();
            viewport.color = Color.white;
            viewport.raycastTarget = true;
            viewport.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/BoardEffectComposite.mat");
            ApplyBackgroundBoardCalibration(viewport);
            var hud = copy.AddComponent<BattleGameplayHud>();
            SetRef(hud, "ui", ui);
            SetRef(hud, "viewport", viewport);
            var settings = (RectTransform)composition.Find("SettingsPanel");
            settings.sizeDelta = new Vector2(640, 446);
            var pauseHint = settings.Find("PauseHint").GetComponent<Text>();
            pauseHint.text = "对局已暂停";
            SetRef(hud, "restartButton", Button(settings, "RestartButton", "重开本关", 60, 336, 240, 66));
            SetRef(hud, "menuButton", Button(settings, "MenuButton", "返回菜单", 340, 336, 240, 66));

            var result = Rect(composition, "ResultPanel", 0, 0, 1920, 1080);
            var shade = result.gameObject.AddComponent<Image>();
            shade.color = new Color(.12f, .18f, .22f, .58f);
            var panel = Rect(result, "Dialog", 610, 335, 700, 400);
            var paper = panel.gameObject.AddComponent<Image>();
            paper.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiSurface.png");
            paper.type = Image.Type.Sliced;
            paper.color = new Color(.93f, .93f, .89f);
            SetRef(hud, "resultPanel", result.gameObject);
            SetRef(hud, "resultHeading", Text(panel, "Heading", "本局结束", 40, 28, 620, 65, 38));
            SetRef(hud, "resultMessage", Text(panel, "Message", "", 40, 104, 620, 130, 25));
            SetRef(hud, "resultRestartButton", Button(panel, "RestartButton", "重开本关", 35, 278, 195, 72));
            SetRef(hud, "nextLevelButton", Button(panel, "NextLevelButton", "下一关", 252, 278, 195, 72));
            SetRef(hud, "resultMenuButton", Button(panel, "MenuButton", "返回菜单", 469, 278, 195, 72));
            result.gameObject.SetActive(false);
            BattleButtonArtInstaller.Apply(ui);

            // 预制体只保存界面内部引用；场景中的对局与相机在实例上接线。
            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
            Object.DestroyImmediate(copy);
            copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, gameScene);
            hud = copy.GetComponent<BattleGameplayHud>();
            SetRef(hud, "session", session);
            var boardCameraObject = new GameObject("BoardRenderCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(boardCameraObject, gameScene);
            var boardCamera = boardCameraObject.GetComponent<Camera>();
            boardCamera.orthographic = true;
            boardCamera.clearFlags = CameraClearFlags.SolidColor;
            boardCamera.backgroundColor = Color.clear;
            boardCamera.cullingMask = 1 << session.Board.gameObject.layer;
            boardCamera.allowHDR = false;
            boardCameraObject.transform.position = session.Board.transform.position + new Vector3(0, 0, -10);
            SetRef(hud.Viewport, "boardCamera", boardCamera);
            session.Input.WorldCamera.cullingMask = 0;
            if (Find<EventSystem>(gameScene) == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                SceneManager.MoveGameObjectToScene(eventSystem, gameScene);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud.Viewport);
            EditorSceneManager.SaveScene(gameScene);
            AssetDatabase.SaveAssets();
            if (openedSource) EditorSceneManager.CloseScene(sourceScene, true);
            SceneManager.SetActiveScene(gameScene);
            Debug.Log("[TapGJ] 局内 UI 已接入关卡：" + GamePath);
        }

        public static void RefreshBoardCalibration()
        {
            var savedScale = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("Composition").localScale;
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                ApplyBackgroundBoardCalibration(prefab.GetComponent<BattleGameplayHud>().Viewport);
                prefab.transform.Find("Composition").localScale = savedScale;
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            Debug.Log("[TapGJ] 已按背景实际格线保存不可见棋盘校准。");
        }

        static void ApplyBackgroundBoardCalibration(BattleBoardViewport viewport)
        {
            // 共用原图 1123×633，四角按可落子格线内沿标定：近左、近右、远右、远左。
            const float left = 100, top = 150, width = 1720, height = 670;
            var rect = viewport.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
            var imageCorners = new[]
            {
                new Vector2(79, 465), new Vector2(1051, 465),
                new Vector2(908, 117), new Vector2(229, 117)
            };
            float scale = Mathf.Min(1920f / 1123, 1080f / 633);
            float imageLeft = (1920 - 1123 * scale) * .5f;
            float imageTop = (1080 - 633 * scale) * .5f;
            viewport.SetCornerCalibration(imageCorners.Select(p => new Vector2(
                (imageLeft + p.x * scale - left) / width, (top + height - imageTop - p.y * scale) / height)).ToArray());
        }

        public static void RefreshHandLayout()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("请先保存当前场景，再更新手牌排布。");
            var sourceScene = SceneManager.GetSceneByPath(UiPath);
            bool openedSource = !sourceScene.isLoaded;
            if (openedSource) sourceScene = EditorSceneManager.OpenScene(UiPath, OpenSceneMode.Additive);
            var ui = Find<BattleUiController>(sourceScene);
            ApplyHandArea(ui);
            ui.ArrangeHand();
            foreach (var card in ui.Cards) PrefabUtility.RecordPrefabInstancePropertyModifications(card.transform);
            EditorSceneManager.SaveScene(sourceScene);
            if (openedSource) EditorSceneManager.CloseScene(sourceScene, true);
            var savedScale = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("Composition").localScale;
            var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var prefabUi = prefab.GetComponent<BattleUiController>();
                ApplyHandArea(prefabUi);
                prefabUi.ArrangeHand();
                // 预制体编辑环境中的画布可能为零尺寸，保留原有整体缩放。
                prefab.transform.Find("Composition").localScale = savedScale;
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            AssetDatabase.SaveAssets();
            Debug.Log("[TapGJ] 已保存预览与玩法界面的手牌排布。");
        }

        static void ApplyHandArea(BattleUiController ui)
        {
            var rect = (RectTransform)ui.Cards[0].transform.parent;
            rect.anchoredPosition = new Vector2(342, -738);
            rect.sizeDelta = new Vector2(1240, 340);
        }

        static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<T>(true)).FirstOrDefault();

        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = parent.gameObject.layer;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Text Text(Transform parent, string name, string value, float x, float y, float width, float height, int size)
        {
            var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<Text>();
            text.text = value;
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Art/Battle/BattleLabels.ttf");
            text.fontSize = size;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            UiFontThemeInstaller.BindText(text, name == "Heading" ? UiFontRole.Title : UiFontRole.Body);
            return text;
        }

        static Button Button(Transform parent, string name, string value, float x, float y, float width, float height)
        {
            var rect = Rect(parent, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiSurface.png");
            image.type = Image.Type.Sliced;
            image.color = new Color(.72f, .76f, .87f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Text(rect, "Label", value, 4, 4, width - 8, height - 8, 26);
            return button;
        }

        static void SetBool(Object target, string field, bool value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).boolValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRef(Object target, string field, Object value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).objectReferenceValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
