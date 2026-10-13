using System.IO;
using System.Linq;
using TapGJ.Core;
using TapGJ.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TapGJ.EditorTools
{
    /// <summary>首次生成独立的局内 UI；现有场景和 Prefab 优先保留。</summary>
    public static class BattleUiSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/BattleUI.unity";
        const string ArtPath = "Assets/UI/Art/Battle";
        const string CardPath = "Assets/UI/Prefabs/BattleCard.prefab";
        const string CardArtPath = "Assets/UI/Data/BattleCardArt.asset";
        static readonly Vector2 CardSize = new Vector2(214, 346);
        static readonly Color Ink = new Color(.21f, .27f, .31f);
        static readonly Color Lavender = new Color(.72f, .76f, .87f, .96f);
        static readonly Color Border = new Color(.43f, .48f, .62f);
        static Font _font;
        static Sprite _surface, _disc, _ring, _focus;

        [MenuItem("TapGJ/打开独立局内 UI", false, 5)]
        public static void OpenOrCreate()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
            else Build();
        }

        public static void Build()
        {
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/UI/Prefabs");
            AssetDatabase.Refresh();
            ImportArt();
            _font = AssetDatabase.LoadAssetAtPath<Font>(ArtPath + "/BattleLabels.ttf");
            if (_font == null) throw new FileNotFoundException("局内 UI 字体缺失");
            _surface = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiSurface.png");
            _disc = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiDisc.png");
            _ring = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiRing.png");
            _focus = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Art/UiFocus.png");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera", typeof(Camera));
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10);
            var cam = camera.GetComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.055f, .07f, .075f);
            cam.cullingMask = 0;

            var canvas = new GameObject("BattleUICanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BattleUiLayout.ReferenceSize;
            scaler.matchWidthOrHeight = .5f;
            var composition = Rect(canvas.transform, "Composition", 0, 0, 1920, 1080);
            composition.anchorMin = composition.anchorMax = composition.pivot = new Vector2(.5f, .5f);
            composition.anchoredPosition = Vector2.zero;
            composition.gameObject.AddComponent<RectMask2D>();
            var bg = Picture(composition, "Background", 0, 0, 1920, 1080, Color.white,
                AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/BattleBackground.jpg"));
            bg.preserveAspect = true;
            var board = Rect(composition, "BoardContent", 420, 236, 1080, 532);
            var controls = Rect(composition, "Controls", 0, 0, 1920, 1080);
            var controlsGroup = controls.gameObject.AddComponent<CanvasGroup>();

            var task = Panel(controls, "TaskPanel", 24, 24, 418, 202);
            Label(task, "Heading", "任务目标", 28, 14, 360, 44, 26, Ink);
            var objective = Label(task, "Objective", "使场上只剩水元素", 28, 60, 360, 44, 30, Ink);
            var progress = Label(task, "Progress", "任务进度  0 / 20", 28, 116, 360, 34, 23, Ink);
            Picture(task, "ProgressTrack", 28, 166, 360, 10, new Color(.38f, .43f, .57f, .3f), _surface);
            var progressFill = Picture(task, "ProgressFill", 28, 166, 360, 10, new Color(.40f, .57f, .60f), _surface);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0;

            var timer = Panel(controls, "TimerPanel", 760, 24, 400, 90);
            Label(timer, "Heading", "计时器", 24, 12, 100, 62, 26, Ink);
            var timerLabel = Label(timer, "Time", "00:00", 156, 10, 220, 64, 42, Ink);
            timerLabel.alignment = TextAnchor.MiddleCenter;
            var round = Label(controls, "RoundLabel", "第 01 回合 / 15", 760, 122, 400, 40, 23, Ink);
            round.alignment = TextAnchor.MiddleCenter;

            var rail = Rect(controls, "ElementRail", 18, 424, 112, 594);
            var elements = new Button[5];
            var selections = new CanvasGroup[5];
            for (int i = 0; i < 5; i++)
            {
                var element = (Element)i;
                var button = MakeButton(rail, "Element_" + element, ElementDefs.Name(element), 10, i * 116,
                    88, 88, new Color(.80f, .72f, .91f, .96f), 36, Ink, true);
                elements[i] = button;
                var selected = Picture(button.transform, "SelectedRing", -4, -4, 96, 96, Border, _ring);
                selections[i] = selected.gameObject.AddComponent<CanvasGroup>();
                selections[i].alpha = 0;
                selections[i].blocksRaycasts = selections[i].interactable = false;
            }

            var hand = Rect(controls, "Hand", 342, 810, 1240, 300);
            var cards = new BattleUiCard[5];
            var cardPrefab = EnsureCardPrefab();
            Element[] sampleElements = { Element.Fire, Element.Earth, Element.Water, Element.Wood, Element.Fire };
            float[] angles = { 10, 5, 0, -5, -10 };
            float[] positionsY = { 48, 14, 0, 14, 48 };
            for (int i = 0; i < cards.Length; i++)
            {
                var card = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab, scene);
                card.name = "CardSlot" + i;
                card.transform.SetParent(hand, false);
                var rect = (RectTransform)card.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(i * 252, -positionsY[i]);
                rect.localRotation = Quaternion.Euler(0, 0, angles[i]);
                cards[i] = card.GetComponent<BattleUiCard>();
                StyleCard(cards[i], sampleElements[i]);
            }
            var selectedLabel = Label(controls, "SelectionLabel", "选择一张手牌", 760, 168, 400, 40, 23, Ink);
            selectedLabel.alignment = TextAnchor.MiddleCenter;

            var next = MakeButton(controls, "NextRoundButton", "下一回合", 1666, 742, 230, 82, Lavender, 32, Ink);
            var discard = MakeButton(controls, "DiscardButton", "弃牌", 1666, 854, 230, 82, Lavender, 32, Ink);
            discard.interactable = false;
            var settings = MakeButton(controls, "SettingsButton", "", 1794, 28, 96, 96,
                new Color(.90f, .69f, .78f, .97f), 32, Ink, true);
            MakeGear(settings.transform);

            var backdrop = MakeButton(composition, "SettingsBackdrop", "", 0, 0, 1920, 1080,
                new Color(.12f, .18f, .22f, .5f), 28, Ink);
            backdrop.GetComponent<Image>().sprite = null;
            backdrop.GetComponent<Image>().type = Image.Type.Simple;
            var modal = Panel(composition, "SettingsPanel", 640, 352, 640, 360);
            modal.GetComponent<Image>().color = new Color(.93f, .93f, .89f);
            Label(modal, "Heading", "对局设置", 44, 30, 550, 54, 36, Ink);
            Label(modal, "PauseHint", "已暂停预览计时", 44, 118, 550, 48, 28, Ink);
            var close = MakeButton(modal, "CloseButton", "继续", 180, 242, 280, 76, Lavender, 30, Ink);
            backdrop.gameObject.SetActive(false);
            modal.gameObject.SetActive(false);

            var controller = canvas.AddComponent<BattleUiController>();
            var data = new SerializedObject(controller);
            Bind(data, "objectiveLabel", objective); Bind(data, "progressLabel", progress);
            Bind(data, "progressFill", progressFill); Bind(data, "timerLabel", timerLabel);
            Bind(data, "roundLabel", round); Bind(data, "selectionLabel", selectedLabel);
            Bind(data, "boardContent", board); Bind(data, "nextRoundButton", next);
            Bind(data, "discardButton", discard); Bind(data, "settingsButton", settings);
            Bind(data, "settingsBackdrop", backdrop); Bind(data, "closeSettingsButton", close);
            Bind(data, "settingsPanel", modal.gameObject); Bind(data, "controls", controlsGroup);
            BindArray(data, "elementButtons", elements); BindArray(data, "elementSelections", selections);
            BindArray(data, "cards", cards);
            data.ApplyModifiedPropertiesWithoutUndo();
            var layout = canvas.AddComponent<BattleUiLayout>();
            data = new SerializedObject(layout);
            Bind(data, "canvasRect", canvas.GetComponent<RectTransform>());
            Bind(data, "composition", composition);
            data.ApplyModifiedPropertiesWithoutUndo();
            layout.Refresh();
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
            AssetDatabase.SaveAssets();
            if (!Application.isBatchMode) Selection.activeGameObject = canvas;
            Debug.Log("[TapGJ] 独立局内 UI 已保存：" + ScenePath);
        }

        static BattleCardArt EnsureCardArt()
        {
            string[] names = { "Wood", "Water", "Fire", "Earth" };
            foreach (string name in names) ImportSprite(ArtPath + "/Cards/" + name + ".jpg", 4096, true);
            var art = AssetDatabase.LoadAssetAtPath<BattleCardArt>(CardArtPath);
            if (art != null) return art;
            Directory.CreateDirectory("Assets/UI/Data");
            art = ScriptableObject.CreateInstance<BattleCardArt>();
            art.wood = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/Cards/Wood.jpg");
            art.water = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/Cards/Water.jpg");
            art.fire = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/Cards/Fire.jpg");
            art.earth = AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + "/Cards/Earth.jpg");
            AssetDatabase.CreateAsset(art, CardArtPath);
            return art;
        }

        static void ImportArt()
        {
            ImportSprite(ArtPath + "/BattleBackground.jpg", 2048);
        }

        static void ImportSprite(string path, int maxSize, bool mipmaps = false)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("局内 UI 美术缺失", path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.mipmapEnabled = mipmaps;
            importer.filterMode = mipmaps ? FilterMode.Trilinear : FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static GameObject EnsureCardPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath);
            if (existing != null) return existing;
            var rect = Rect(null, "BattleCard", 0, 0, CardSize.x, CardSize.y);
            var artwork = Rect(rect, "Artwork", 0, 0, CardSize.x, CardSize.y);
            var focus = Picture(artwork, "FocusRing", -4, -4, CardSize.x + 8, CardSize.y + 8, new Color(1f, .89f, .46f), _focus);
            var feedbackRing = focus.gameObject.AddComponent<CanvasGroup>();
            feedbackRing.alpha = 0;
            feedbackRing.blocksRaycasts = feedbackRing.interactable = false;
            var selected = Picture(artwork, "SelectedRing", -5, -5, CardSize.x + 10, CardSize.y + 10, new Color(.22f, .51f, .52f), _focus);
            var selection = selected.gameObject.AddComponent<CanvasGroup>();
            selection.alpha = 0;
            selection.blocksRaycasts = selection.interactable = false;
            var button = rect.gameObject.AddComponent<Button>();
            ConfigureButton(button);
            AddFeedback(button, feedbackRing);
            var view = rect.gameObject.AddComponent<BattleUiCard>();
            var data = new SerializedObject(view);
            Bind(data, "button", button); Bind(data, "artwork", artwork); Bind(data, "selectedRing", selection);
            data.ApplyModifiedPropertiesWithoutUndo();
            ConfigureCardArtwork(view, EnsureCardArt());
            StyleCard(view, Element.Fire);
            var prefab = PrefabUtility.SaveAsPrefabAsset(rect.gameObject, CardPath);
            Object.DestroyImmediate(rect.gameObject);
            return prefab;
        }

        static void StyleCard(BattleUiCard card, Element element)
        {
            var data = new SerializedObject(card);
            data.FindProperty("element").enumValueIndex = (int)element;
            data.ApplyModifiedPropertiesWithoutUndo();
            card.RefreshArt();
            if (PrefabUtility.IsPartOfPrefabInstance(card))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(card);
            }
        }

        static void ConfigureCardArtwork(BattleUiCard card, BattleCardArt art)
        {
            var artwork = (RectTransform)card.transform.Find("Artwork");
            ((RectTransform)card.transform).sizeDelta = artwork.sizeDelta = CardSize;
            foreach (string name in new[] { "Frame", "Paper", "InnerRule", "Seal", "ElementLabel", "Illustration", "CardName" })
            {
                var old = artwork.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var face = artwork.Find("CardFace") != null ? artwork.Find("CardFace").GetComponent<Image>() :
                Picture(artwork, "CardFace", 0, 0, CardSize.x, CardSize.y, Color.white, null);
            face.transform.SetAsFirstSibling();
            face.type = Image.Type.Simple;
            face.preserveAspect = true;
            face.raycastTarget = true;
            var fallback = artwork.Find("FallbackArtwork") as RectTransform;
            if (fallback == null)
            {
                fallback = Rect(artwork, "FallbackArtwork", 0, 0, CardSize.x, CardSize.y);
                Picture(fallback, "Frame", 0, 0, CardSize.x, CardSize.y, new Color(.78f, .73f, .62f), _surface).raycastTarget = true;
                Label(fallback, "ElementLabel", "金", 0, 34, CardSize.x, 70, 40, Ink).alignment = TextAnchor.MiddleCenter;
                Label(fallback, "ArtHint", "卡面待补充", 0, 118, CardSize.x, 44, 24, Ink).alignment = TextAnchor.MiddleCenter;
            }
            fallback.SetSiblingIndex(1);
            ResizeRing((RectTransform)artwork.Find("FocusRing"), 4);
            ResizeRing((RectTransform)artwork.Find("SelectedRing"), 5);
            var data = new SerializedObject(card);
            Bind(data, "cardArt", art); Bind(data, "cardFace", face); Bind(data, "fallbackArtwork", fallback.gameObject);
            Bind(data, "fallbackFrame", fallback.Find("Frame").GetComponent<Image>());
            Bind(data, "fallbackLabel", fallback.Find("ElementLabel").GetComponent<Text>());
            data.ApplyModifiedPropertiesWithoutUndo();
            card.RefreshArt();
        }

        static void ResizeRing(RectTransform ring, float margin)
        {
            ring.anchoredPosition = new Vector2(-margin, margin);
            ring.sizeDelta = CardSize + Vector2.one * margin * 2;
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Image Picture(Transform parent, string name, float x, float y, float w, float h, Color color, Sprite sprite)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.type = sprite == _surface || sprite == _focus ? Image.Type.Sliced : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        static RectTransform Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            var image = Picture(parent, name, x, y, w, h, Lavender, _surface);
            var outline = image.gameObject.AddComponent<Outline>();
            outline.effectColor = Border;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return image.rectTransform;
        }

        static Text Label(Transform parent, string name, string value, float x, float y, float w, float h, int size, Color color)
        {
            var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Text>();
            text.text = value;
            text.font = _font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            // 新建的文字直接接入共享配置，后续换字体无需逐个改场景对象。
            UiFontThemeInstaller.BindText(text, name == "Heading" ? UiFontRole.Title :
                name == "Time" ? UiFontRole.Number : UiFontRole.Body);
            return text;
        }

        static Button MakeButton(Transform parent, string name, string value, float x, float y, float w, float h, Color color, int size, Color textColor, bool circle = false)
        {
            var image = Picture(parent, name, x, y, w, h, color, circle ? _disc : _surface);
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ConfigureButton(button);
            Label(image.transform, "Label", value, 4, 4, w - 8, h - 8, size, textColor).alignment = TextAnchor.MiddleCenter;
            var focus = Picture(image.transform, "FocusRing", -3, -3, w + 6, h + 6,
                new Color(.98f, .84f, .48f), circle ? _ring : _focus);
            var ring = focus.gameObject.AddComponent<CanvasGroup>();
            ring.alpha = 0;
            ring.blocksRaycasts = ring.interactable = false;
            AddFeedback(button, ring);
            if (!circle)
            {
                var outline = image.gameObject.AddComponent<Outline>();
                outline.effectColor = Border;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
            return button;
        }

        static void ConfigureButton(Button button)
        {
            var colors = button.colors;
            colors.normalColor = colors.highlightedColor = colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.86f, .89f, .94f);
            colors.disabledColor = new Color(.83f, .83f, .83f, .7f);
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        }

        static void AddFeedback(Button button, CanvasGroup ring)
        {
            var feedback = button.gameObject.AddComponent<MenuButtonFeedback>();
            var data = new SerializedObject(feedback);
            Bind(data, "focusRing", ring);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static void MakeGear(Transform parent)
        {
            var ring = Picture(parent, "GearRing", 26, 26, 44, 44, new Color(.99f, .97f, .90f), _ring);
            for (int i = 0; i < 8; i++)
            {
                float radians = i * Mathf.PI / 4;
                var tooth = Picture(parent, "GearTooth" + i, 48 + Mathf.Sin(radians) * 24,
                    48 + Mathf.Cos(radians) * 24, 12, 22, ring.color, _surface);
                tooth.rectTransform.pivot = new Vector2(.5f, .5f);
                tooth.rectTransform.localRotation = Quaternion.Euler(0, 0, -i * 45);
            }
        }

        static void Bind(SerializedObject data, string name, Object value) => data.FindProperty(name).objectReferenceValue = value;
        static void BindArray<T>(SerializedObject data, string name, T[] values) where T : Object
        {
            var array = data.FindProperty(name);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
