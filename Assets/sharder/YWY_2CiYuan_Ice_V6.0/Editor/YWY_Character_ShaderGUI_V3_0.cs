#if UNITY_EDITOR
// Compatible ShaderGUI file. Put this script under an Assets/Editor folder.
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class YWY_Character_ShaderGUI_V3_0 : ShaderGUI
{
    private static Dictionary<int, Dictionary<string, bool>> materialFoldoutStates = new Dictionary<int, Dictionary<string, bool>>();

    // Inspector 性能优化：
    // 每次 OnGUI 统一计算一次模块是否被修改，标题绘制时只读取缓存，
    // 避免每个主模块/子模块都反复 new Material + 遍历属性导致展开卡顿。
    private Dictionary<string, bool> currentModuleModifiedStates = new Dictionary<string, bool>();
    private Dictionary<string, ShaderUtil.ShaderPropertyType> currentShaderPropertyTypeCache = new Dictionary<string, ShaderUtil.ShaderPropertyType>();

    private GUIStyle centerButtonStyle;
    private GUIStyle centerPopupStyle;
    private GUIStyle beginnerTipStyle;
    private GUIStyle mainResetButtonStyle;
    private GUIStyle subResetButtonStyle;
    private GUIStyle mainResetLabelStyle;
    private GUIStyle subResetLabelStyle;

    // 初学者模式按材质球单独记忆，避免一个材质开启后影响所有材质。
    private static Dictionary<int, bool> materialBeginnerModeStates = new Dictionary<int, bool>();
    private bool currentBeginnerMode = false;


    private GUIStyle GetCenteredPopupStyle()
    {
        if (centerPopupStyle == null)
        {
            centerPopupStyle = new GUIStyle(EditorStyles.popup);
            centerPopupStyle.alignment = TextAnchor.MiddleCenter;
        }
        return centerPopupStyle;
    }

    private GUIStyle GetMainFoldoutStyle(bool hasContent)
    {
        Color textColor = hasContent ? new Color(0.7f, 0.8f, 0.9f) : new Color(0.65f, 0.55f, 0.55f);
        var style = new GUIStyle("ShurikenModuleTitle");
        style.font = new GUIStyle(EditorStyles.boldLabel).font;
        style.border = new RectOffset(15, 7, 4, 4);
        style.fixedHeight = 22;
        style.contentOffset = new Vector2(20f, -2f);
        style.fontSize = 11;
        style.normal.textColor = textColor;
        return style;
    }

    private GUIStyle GetSubFoldoutStyle()
    {
        var style = new GUIStyle("ShurikenModuleTitle");
        style.font = new GUIStyle(EditorStyles.boldLabel).font;
        style.border = new RectOffset(15, 7, 4, 4);
        style.fixedHeight = 18;
        style.contentOffset = new Vector2(30f, -2f);
        style.fontSize = 10;
        style.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        return style;
    }



    private GUIStyle GetBeginnerTipStyle()
    {
        if (beginnerTipStyle == null)
        {
            beginnerTipStyle = new GUIStyle(EditorStyles.helpBox);
            beginnerTipStyle.wordWrap = true;
            beginnerTipStyle.fontSize = 10;
            beginnerTipStyle.normal.textColor = new Color(0.62f, 0.62f, 0.62f);
            beginnerTipStyle.padding = new RectOffset(8, 8, 5, 5);
        }
        return beginnerTipStyle;
    }

    private void DrawBeginnerModeToggle(int matID)
    {
        if (!materialBeginnerModeStates.TryGetValue(matID, out currentBeginnerMode))
        {
            currentBeginnerMode = false;
            materialBeginnerModeStates[matID] = currentBeginnerMode;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent("初学者模式"));
        buttonRect.width = Mathf.Min(100f, buttonRect.width);

        if (centerButtonStyle == null)
        {
            centerButtonStyle = new GUIStyle(EditorStyles.miniButton);
            centerButtonStyle.alignment = TextAnchor.MiddleCenter;
        }

        string text = currentBeginnerMode ? "已开启" : "已关闭";
        if (GUI.Button(buttonRect, text, centerButtonStyle))
        {
            currentBeginnerMode = !currentBeginnerMode;
            materialBeginnerModeStates[matID] = currentBeginnerMode;
        }

        if (currentBeginnerMode)
        {
            DrawBeginnerTip("开启后会在各模块和关键参数下显示用途说明。这个开关只影响当前材质球的 Shader 面板显示，不会写入材质，也不会影响渲染结果。");
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(5);
    }

    private void DrawBeginnerTip(string text)
    {
        if (!currentBeginnerMode || string.IsNullOrEmpty(text)) return;
        GUILayout.Label("说明：" + text, GetBeginnerTipStyle());
    }

    private void DrawBeginnerTipForModule(string moduleName)
    {
        if (!moduleTips.TryGetValue(moduleName, out var text)) return;
        DrawBeginnerTip(text);
    }

    private void DrawBeginnerTipForSubModule(string subModuleName)
    {
        if (!subModuleTips.TryGetValue(subModuleName, out var text)) return;
        DrawBeginnerTip(text);
    }

    private void DrawBeginnerTipForProperty(string propName)
    {
        if (!currentBeginnerMode) return;
        if (propertyTips.TryGetValue(propName, out var text))
            DrawBeginnerTip(text);
    }


    private GUIStyle GetResetButtonStyle(bool isMain)
    {
        if (isMain)
        {
            if (mainResetButtonStyle == null)
            {
                mainResetButtonStyle = new GUIStyle(EditorStyles.miniButton);
                mainResetButtonStyle.alignment = TextAnchor.MiddleCenter;
                mainResetButtonStyle.fontStyle = FontStyle.Bold;
                mainResetButtonStyle.fontSize = 0;
                mainResetButtonStyle.fixedWidth = 0f;
                mainResetButtonStyle.fixedHeight = 0f;
                mainResetButtonStyle.padding = new RectOffset(0, 0, 0, 0);
                mainResetButtonStyle.margin = new RectOffset(0, 0, 0, 0);
                mainResetButtonStyle.contentOffset = Vector2.zero;
                mainResetButtonStyle.clipping = TextClipping.Clip;
            }
            return mainResetButtonStyle;
        }

        if (subResetButtonStyle == null)
        {
            subResetButtonStyle = new GUIStyle(EditorStyles.miniButton);
            subResetButtonStyle.alignment = TextAnchor.MiddleCenter;
            subResetButtonStyle.fontStyle = FontStyle.Bold;
            subResetButtonStyle.fontSize = 0;
            subResetButtonStyle.fixedWidth = 0f;
            subResetButtonStyle.fixedHeight = 0f;
            subResetButtonStyle.padding = new RectOffset(0, 0, 0, 0);
            subResetButtonStyle.margin = new RectOffset(0, 0, 0, 0);
            subResetButtonStyle.contentOffset = Vector2.zero;
            subResetButtonStyle.clipping = TextClipping.Clip;
        }
        return subResetButtonStyle;
    }

    private GUIStyle GetResetLabelStyle(bool isMain)
    {
        if (isMain)
        {
            if (mainResetLabelStyle == null)
            {
                mainResetLabelStyle = new GUIStyle(EditorStyles.label);
                mainResetLabelStyle.alignment = TextAnchor.MiddleCenter;
                mainResetLabelStyle.fontStyle = FontStyle.Bold;
                mainResetLabelStyle.fontSize = 11;
                mainResetLabelStyle.padding = new RectOffset(0, 0, 0, 0);
                mainResetLabelStyle.margin = new RectOffset(0, 0, 0, 0);
                mainResetLabelStyle.contentOffset = Vector2.zero;
                mainResetLabelStyle.clipping = TextClipping.Clip;
                mainResetLabelStyle.normal.textColor = EditorStyles.miniButton.normal.textColor;
            }
            return mainResetLabelStyle;
        }

        if (subResetLabelStyle == null)
        {
            subResetLabelStyle = new GUIStyle(EditorStyles.label);
            subResetLabelStyle.alignment = TextAnchor.MiddleCenter;
            subResetLabelStyle.fontStyle = FontStyle.Bold;
            subResetLabelStyle.fontSize = 10;
            subResetLabelStyle.padding = new RectOffset(0, 0, 0, 0);
            subResetLabelStyle.margin = new RectOffset(0, 0, 0, 0);
            subResetLabelStyle.contentOffset = Vector2.zero;
            subResetLabelStyle.clipping = TextClipping.Clip;
            subResetLabelStyle.normal.textColor = EditorStyles.miniButton.normal.textColor;
        }
        return subResetLabelStyle;
    }

    private void DrawResetButtonLabel(Rect resetRect, bool isMain)
    {
        // Unity 内置字体在很小的按钮里，几何居中会有视觉偏差。
        // X 方向继续向右 1px；Y 方向从上一版回调：
        // 主模块不再上移；子模块只上移 1px，避免 R 贴到按钮上边。
        float xOffset = 1f;
        float yOffset = isMain ? 0f : -1f;
        Rect labelRect = new Rect(resetRect.x + xOffset, resetRect.y + yOffset, resetRect.width, resetRect.height);
        GUI.Label(labelRect, "R", GetResetLabelStyle(isMain));
    }



    private static readonly Dictionary<string, string[]> subModulePropertyMap = new Dictionary<string, string[]>
    {
        { "光照遮罩贴图", new string[]{ "_UseLightMaskTex", "_LightMaskTex", "_LightMaskUseUV2", "_LightMaskStrength" } },
        { "暗部色贴图", new string[]{ "_UseDarkColorTex", "_DarkColorTex", "_DarkColorUseUV2", "_DarkColorStrength", "_DarkColorDepth" } },
        { "线条贴图", new string[]{ "_UseLineTex", "_LineTex", "_LineColor", "_LineTexUseUV2" } },
        { "基础贴图叠色", new string[]{ "_UseDiffuseOverlayTex", "_DiffuseOverlayTex", "_DiffuseOverlayStrength", "_DiffuseOverlayUseUV2" } },
        { "法线贴图", new string[]{ "_UseNormalMap", "_CharNormalTex", "_NormalUseUV2", "_CharNormalStrength" } },
        { "粗糙度贴图", new string[]{ "_UseRoughness", "_RoughnessTex", "_RoughnessUseUV2", "_RoughnessStrength" } },
        { "AO贴图", new string[]{ "_UseAO", "_AOTex", "_AOUseUV2", "_AOStrength" } },
        { "金属度", new string[]{ "_UseMetallic", "_MetallicTex", "_MetallicUseUV2", "_Metallic", "_MetallicDiffuseAmount", "_MetallicSpecularMult", "_MetallicGlossMult" } },
        { "位置边缘光", new string[]{ "_GampColor", "_PosGamp", "_GampUseVertexColor", "_GampIntensity", "_GampIntensityUseParticleCustom", "_GampIntensityCustomData", "_GampIntensityCustomChannel", "_GampOne", "_HighLine" } },
        { "菲涅尔效果", new string[]{ "_Frenel", "_Fresnecolor", "_FrenelPower" } },
        { "自发光", new string[]{ "_Light", "_LightUseVertexColor", "_LightMask", "_2UV", "_Lightcolor", "_LightIntensity", "_LightIntensityUseParticleCustom", "_LightIntensityCustomData", "_LightIntensityCustomChannel", "_LightFrenel", "_LightFresnelPower", "_LMU_Speed", "_LMV_Spped", "_DissolveThresholdCustomData", "_DissolveThresholdCustomChannel" } },
        { "附加溶解贴图", new string[]{ "_DissolveTex2", "_DissolveTex2UseUV2", "_UseDissolveTex2", "_DissolveBlendMode", "_DissolveBlendStrength", "_DissolveTex2USpeed", "_DissolveTex2VSpeed", "_DissolveTex2Rot" } },
        { "方向溶解", new string[]{ "_DissolveUseHeight", "_DissolveAngleX", "_DissolveAngleY", "_DissolveAngleZ", "_DissolveHeightBottom", "_DissolveHeightTop", "_DissolveHeightReverse", "_DissolveMainEdgeNoise", "_DissolveEdgeNoiseStr", "_IceProgressUseParticleCustom", "_IceProgressCustomData", "_IceProgressCustomChannel" } },
        { "结冰边缘扰动", new string[]{ "_IceEdgeNoiseToggle", "_IceEdgeNoiseStrength", "_IceEdgeNoiseSize", "_IceProgressTexNoiseStrength", "_IceProgressTexNoiseSize", "_IceEdgeNoiseUseUV2" } },
        { "结冰溶解", new string[]{ "_IceDissolveToggle", "_IceDissolveTex", "_IceDissolveThreshold", "_IceDissolveThresholdUseParticleCustom", "_IceDissolveThresholdCustomData", "_IceDissolveThresholdCustomChannel", "_IceDissolveReverse", "_IceUseUV2" } },
    };

    private static readonly Dictionary<string, string[]> mainModuleSubModuleMap = new Dictionary<string, string[]>
    {
        { "角色基础", new string[]{ "光照遮罩贴图", "暗部色贴图", "线条贴图", "基础贴图叠色", "法线贴图", "粗糙度贴图", "AO贴图", "金属度", "位置边缘光", "菲涅尔效果", "自发光" } },
        { "溶解", new string[]{ "附加溶解贴图", "方向溶解" } },
        { "描边", new string[]{  } },
        { "结冰", new string[]{ "结冰边缘扰动", "结冰溶解" } },
        { "曲面细分", new string[]{  } },
    };

    private string[] GetSubModuleProps(string title)
    {
        if (subModulePropertyMap.TryGetValue(title, out var props)) return props;
        return new string[0];
    }

    private string[] GetMainModuleProps(string title, string[] directProps)
    {
        List<string> allProps = new List<string>();
        if (directProps != null) allProps.AddRange(directProps);

        if (mainModuleSubModuleMap.TryGetValue(title, out var subTitles))
        {
            foreach (var subTitle in subTitles)
            {
                if (subModulePropertyMap.TryGetValue(subTitle, out var subProps))
                {
                    allProps.AddRange(subProps);
                }
            }
        }

        List<string> unique = new List<string>();
        HashSet<string> seen = new HashSet<string>();
        foreach (var p in allProps)
        {
            if (!string.IsNullOrEmpty(p) && seen.Add(p)) unique.Add(p);
        }
        return unique.ToArray();
    }

    private Material[] GetTargetMaterials(MaterialEditor materialEditor)
    {
        List<Material> mats = new List<Material>();
        foreach (var obj in materialEditor.targets)
        {
            if (obj is Material mat) mats.Add(mat);
        }
        return mats.ToArray();
    }

    private bool TryGetShaderPropertyType(Shader shader, string propName, out ShaderUtil.ShaderPropertyType type)
    {
        type = ShaderUtil.ShaderPropertyType.Color;
        if (shader == null || string.IsNullOrEmpty(propName)) return false;

        string cacheKey = shader.GetInstanceID().ToString() + ":" + propName;
        if (currentShaderPropertyTypeCache.TryGetValue(cacheKey, out type))
            return true;

        int count = ShaderUtil.GetPropertyCount(shader);
        for (int i = 0; i < count; i++)
        {
            if (ShaderUtil.GetPropertyName(shader, i) == propName)
            {
                type = ShaderUtil.GetPropertyType(shader, i);
                currentShaderPropertyTypeCache[cacheKey] = type;
                return true;
            }
        }
        return false;
    }

    private bool IsPropertyModified(Material mat, Material defaultMat, string propName)
    {
        if (mat == null || defaultMat == null || string.IsNullOrEmpty(propName)) return false;
        if (!mat.HasProperty(propName) || !defaultMat.HasProperty(propName)) return false;
        if (!TryGetShaderPropertyType(mat.shader, propName, out var type)) return false;

        switch (type)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                return mat.GetColor(propName) != defaultMat.GetColor(propName);
            case ShaderUtil.ShaderPropertyType.Vector:
                return mat.GetVector(propName) != defaultMat.GetVector(propName);
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                return !Mathf.Approximately(mat.GetFloat(propName), defaultMat.GetFloat(propName));
            case ShaderUtil.ShaderPropertyType.TexEnv:
                return mat.GetTexture(propName) != defaultMat.GetTexture(propName)
                    || mat.GetTextureScale(propName) != defaultMat.GetTextureScale(propName)
                    || mat.GetTextureOffset(propName) != defaultMat.GetTextureOffset(propName);
        }
        return false;
    }


    private void BuildModuleModifiedStateCache(MaterialEditor materialEditor)
    {
        currentModuleModifiedStates.Clear();
        currentShaderPropertyTypeCache.Clear();

        Material[] mats = GetTargetMaterials(materialEditor);
        Dictionary<int, Material> defaultMaterialCache = new Dictionary<int, Material>();

        try
        {
            foreach (var mod in modules)
            {
                string key = "M:" + mod.title;
                currentModuleModifiedStates[key] = HasAnyModifiedPropertiesCached(mats, defaultMaterialCache, GetMainModuleProps(mod.title, mod.props));
            }

            foreach (var pair in subModulePropertyMap)
            {
                string key = "S:" + pair.Key;
                currentModuleModifiedStates[key] = HasAnyModifiedPropertiesCached(mats, defaultMaterialCache, pair.Value);
            }
        }
        finally
        {
            foreach (var pair in defaultMaterialCache)
            {
                if (pair.Value != null) Object.DestroyImmediate(pair.Value);
            }
        }
    }

    private Material GetDefaultMaterialForShader(Shader shader, Dictionary<int, Material> defaultMaterialCache)
    {
        if (shader == null) return null;
        int id = shader.GetInstanceID();
        if (!defaultMaterialCache.TryGetValue(id, out var defaultMat) || defaultMat == null)
        {
            defaultMat = new Material(shader);
            defaultMat.hideFlags = HideFlags.HideAndDontSave;
            defaultMaterialCache[id] = defaultMat;
        }
        return defaultMat;
    }

    private bool HasAnyModifiedPropertiesCached(Material[] mats, Dictionary<int, Material> defaultMaterialCache, string[] propNames)
    {
        if (propNames == null || propNames.Length == 0) return false;

        foreach (var mat in mats)
        {
            if (mat == null || mat.shader == null) continue;

            Material defaultMat = GetDefaultMaterialForShader(mat.shader, defaultMaterialCache);
            if (defaultMat == null) continue;

            foreach (var propName in propNames)
            {
                if (IsPropertyModified(mat, defaultMat, propName))
                    return true;
            }
        }
        return false;
    }

    private bool IsModuleModified(bool isMain, string title)
    {
        string key = (isMain ? "M:" : "S:") + title;
        return currentModuleModifiedStates.TryGetValue(key, out var modified) && modified;
    }

    private bool HasAnyModifiedProperties(MaterialEditor materialEditor, string[] propNames)
    {
        if (propNames == null || propNames.Length == 0) return false;
        Material[] mats = GetTargetMaterials(materialEditor);
        foreach (var mat in mats)
        {
            if (mat == null || mat.shader == null) continue;
            Material defaultMat = new Material(mat.shader);
            foreach (var propName in propNames)
            {
                if (IsPropertyModified(mat, defaultMat, propName))
                {
                    Object.DestroyImmediate(defaultMat);
                    return true;
                }
            }
            Object.DestroyImmediate(defaultMat);
        }
        return false;
    }

    private void ResetPropertyToDefault(Material mat, Material defaultMat, string propName)
    {
        if (mat == null || defaultMat == null || string.IsNullOrEmpty(propName)) return;
        if (!mat.HasProperty(propName) || !defaultMat.HasProperty(propName)) return;
        if (!TryGetShaderPropertyType(mat.shader, propName, out var type)) return;

        switch (type)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                mat.SetColor(propName, defaultMat.GetColor(propName));
                break;
            case ShaderUtil.ShaderPropertyType.Vector:
                mat.SetVector(propName, defaultMat.GetVector(propName));
                break;
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                mat.SetFloat(propName, defaultMat.GetFloat(propName));
                break;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                mat.SetTexture(propName, defaultMat.GetTexture(propName));
                mat.SetTextureScale(propName, defaultMat.GetTextureScale(propName));
                mat.SetTextureOffset(propName, defaultMat.GetTextureOffset(propName));
                break;
        }
    }

    private void ResetModuleToDefault(MaterialEditor materialEditor, string[] propNames)
    {
        if (propNames == null || propNames.Length == 0) return;
        Material[] mats = GetTargetMaterials(materialEditor);
        Undo.RecordObjects(mats, "Reset Shader Module");

        foreach (var mat in mats)
        {
            if (mat == null || mat.shader == null) continue;
            Material defaultMat = new Material(mat.shader);
            foreach (var propName in propNames)
            {
                ResetPropertyToDefault(mat, defaultMat, propName);
            }
            Object.DestroyImmediate(defaultMat);
            EditorUtility.SetDirty(mat);
        }

        currentModuleModifiedStates.Clear();
        currentShaderPropertyTypeCache.Clear();

        materialEditor.PropertiesChanged();
        GUI.changed = true;
    }

    private bool DrawMainFoldout(string title, bool display, MaterialEditor materialEditor, string[] propNames, bool hasContent = true)
    {
        var style = GetMainFoldoutStyle(hasContent);
        var rect = GUILayoutUtility.GetRect(16f, 22f, style);
        GUI.Box(rect, title, style);
        Event e = Event.current;
        var toggleRect = new Rect(rect.x + 4f, rect.y + 3f, 13f, 13f);

        bool showReset = IsModuleModified(true, title);
        Rect resetRect = Rect.zero;
        if (showReset)
        {
            // 主模块标题框高度约 22，这里让按钮框明显小一圈，并上下居中。
            float btnHeight = 16f;
            float btnWidth = 18f;
            // miniButton 的视觉外框会略微偏下，几何居中后看起来仍不居中，所以这里向上微调 1 像素。
            float btnY = rect.y + Mathf.Round((rect.height - btnHeight) * 0.5f) - 1f;
            resetRect = new Rect(rect.xMax - btnWidth - 4f, btnY, btnWidth, btnHeight);
            if (GUI.Button(resetRect, GUIContent.none, GetResetButtonStyle(true)))
            {
                ResetModuleToDefault(materialEditor, propNames);
            }
            DrawResetButtonLabel(resetRect, true);
        }

        if (e.type == EventType.Repaint)
            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);

        bool clickedReset = showReset && resetRect.Contains(e.mousePosition);
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && !clickedReset)
        {
            display = !display;
            GUI.changed = true;
            e.Use();
        }
        return display;
    }

    private bool DrawSubFoldout(string title, bool display, MaterialEditor materialEditor, string[] propNames)
    {
        var style = GetSubFoldoutStyle();
        var rect = GUILayoutUtility.GetRect(16f, 18f, style);
        GUI.Box(rect, title, style);
        Event e = Event.current;
        var toggleRect = new Rect(rect.x + 15f, rect.y + 2f, 11f, 11f);

        bool showReset = IsModuleModified(false, title);
        Rect resetRect = Rect.zero;
        if (showReset)
        {
            // 子模块标题框高度约 18，按钮框比主模块更小，并上下居中。
            float btnHeight = 13f;
            float btnWidth = 15f;
            // 子模块按钮同样向上微调 1 像素，让按钮外框视觉上和标题框上下居中。
            float btnY = rect.y + Mathf.Round((rect.height - btnHeight) * 0.5f) - 1f;
            resetRect = new Rect(rect.xMax - btnWidth - 3f, btnY, btnWidth, btnHeight);
            if (GUI.Button(resetRect, GUIContent.none, GetResetButtonStyle(false)))
            {
                ResetModuleToDefault(materialEditor, propNames);
            }
            DrawResetButtonLabel(resetRect, false);
        }

        if (e.type == EventType.Repaint)
            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);

        bool clickedReset = showReset && resetRect.Contains(e.mousePosition);
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition) && !clickedReset)
        {
            display = !display;
            GUI.changed = true;
            e.Use();
        }
        return display;
    }

    private class ShaderModule
    {
        public string title;
        public string[] props;

        public ShaderModule(string title, string[] props)
        {
            this.title = title;
            this.props = props;
        }
    }

    private class ToggleTexts
    {
        public string on;
        public string off;

        public ToggleTexts(string on, string off)
        {
            this.on = on;
            this.off = off;
        }
    }

    private static readonly ShaderModule[] modules = new ShaderModule[]
    {
        new ShaderModule("角色基础", new string[]{
            "_CullMode", "_AlphaMode", "_TransparentZWrite", "_TransparentAlphaClip", "_UseCustomRenderQueue", "_CustomRenderQueue", 
            "_LightDir", "_LightShadowStrength", "_Diffuse", "_DiffuseUseUV2", "_SelfAlpha", "_DiffuseUseParticleColor", "_DiffuseAlphaUseVertexColor",
            "_DiffuseCOloer", "_ReceiveShadowStrength", "_Cutoff",
            "_Specular", "_Gloss"
        }),
        new ShaderModule("溶解", new string[]{
            "_UseNewDissolve", "_DissolveTex",
            "_DissolveUSpeed", "_DissolveVSpeed", "_DissolveTexRot",
            "_DissolveScreenUV", "_DissolveUseUV1",
            "_DissolveThreshold", "_DissolveUseParticleCustom", "_DissolveEdgeEnable", "_DissolveEdgeWidth", "_DissolveEdgeIntensity",
            "_DissolveEdgeColor", "_DissolveEdgeHardness",
            "_DissolveAlphaClip"
        }),
        new ShaderModule("描边", new string[]{
            "_EnableOutline", "_OutLine", "_OutLineMaskUseUV2", "_OutLineColor", "_OutLineTex", "_OutlineUseUV2", "_OutLinePower"
        }),
        new ShaderModule("结冰", new string[]{
            "_EnableIce",
            "_IceSurfaceTex", "_IceSurfaceUseUV2",
            "_IceColor", "_IceAlpha",
            "_IceEdgeColor", "_IceEdgeWidth", "_IceEdgeIntensity",
            "_IceProgressTex", "_IceProgressTexToggle", "_IceProgressTexUseUV2",
            "_IceProgressTexRot", "_IceProgress", "_IceEdgeBlur",
            "_IceDirectionToggle", "_IceAngleX", "_IceAngleY", "_IceAngleZ",
            "_IceHeight", "_IceHeightReverse",
            "_IceBumpTex", "_IceBumpUseUV2", "_IceBumpStrength",
            "_IceReflectTex", "_IceReflectStrength",
            "_IceNormalTex", "_IceNormalUseUV2", "_IceNormalStrength"
        }),
        new ShaderModule("曲面细分", new string[]{
            "_TessellationEnabled", "_EdgeLength"
        })
    };

    private static readonly string[] subModuleKeys = new string[]
    {
        "光照遮罩贴图", "暗部色贴图", "线条贴图", "基础贴图叠色", "法线贴图", "粗糙度贴图", "AO贴图", "金属度", "位置边缘光", "菲涅尔效果", "自发光",
        "附加溶解贴图", "方向溶解",
        "结冰溶解", "结冰边缘扰动"
    };

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        Material mat = materialEditor.target as Material;
        if (mat == null) return;

        materialEditor.SetDefaultGUIWidths();
        int matID = mat.GetInstanceID();
        if (!materialFoldoutStates.TryGetValue(matID, out var stateDict))
        {
            stateDict = new Dictionary<string, bool>();
            foreach (var mod in modules)
                stateDict[mod.title] = (mod.title == "角色基础");
            foreach (var key in subModuleKeys)
                stateDict[key] = false;
            materialFoldoutStates[matID] = stateDict;
        }

        var propDict = new Dictionary<string, MaterialProperty>();
        foreach (var p in properties) propDict[p.name] = p;

        SyncAlphaModeState(materialEditor, propDict);
        BuildModuleModifiedStateCache(materialEditor);

        EditorGUILayout.Space(5);
        DrawBeginnerModeToggle(matID);
        foreach (var mod in modules)
        {
            bool isCharacterBase = mod.title == "角色基础";
            bool isDissolve = mod.title == "溶解";
            bool isIce = mod.title == "结冰";

            bool state = stateDict[mod.title];
            bool newState = DrawMainFoldout(mod.title, state, materialEditor, GetMainModuleProps(mod.title, mod.props), true);
            stateDict[mod.title] = newState;

            if (newState)
            {
                DrawBeginnerTipForModule(mod.title);
                if (isCharacterBase)
                {
                    foreach (var propName in mod.props) DrawPropertySmart(propName, propDict, materialEditor);
                    GUILayout.Space(5);
                    bool lightMaskState = stateDict["光照遮罩贴图"];
                    lightMaskState = DrawSubFoldout("光照遮罩贴图", lightMaskState, materialEditor, GetSubModuleProps("光照遮罩贴图"));
                    stateDict["光照遮罩贴图"] = lightMaskState;
                    if (lightMaskState)
                    {
                        DrawBeginnerTipForSubModule("光照遮罩贴图");
                        DrawPropertySmart("_UseLightMaskTex", propDict, materialEditor);
                        DrawPropertySmart("_LightMaskTex", propDict, materialEditor);
                        DrawPropertySmart("_LightMaskUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_LightMaskStrength", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool darkColorState = stateDict["暗部色贴图"];
                    darkColorState = DrawSubFoldout("暗部色贴图", darkColorState, materialEditor, GetSubModuleProps("暗部色贴图"));
                    stateDict["暗部色贴图"] = darkColorState;
                    if (darkColorState)
                    {
                        DrawBeginnerTipForSubModule("暗部色贴图");
                        DrawPropertySmart("_UseDarkColorTex", propDict, materialEditor);
                        DrawPropertySmart("_DarkColorTex", propDict, materialEditor);
                        DrawPropertySmart("_DarkColorUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_DarkColorStrength", propDict, materialEditor);
                        DrawPropertySmart("_DarkColorDepth", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool lineTexState = stateDict["线条贴图"];
                    lineTexState = DrawSubFoldout("线条贴图", lineTexState, materialEditor, GetSubModuleProps("线条贴图"));
                    stateDict["线条贴图"] = lineTexState;
                    if (lineTexState)
                    {
                        DrawBeginnerTipForSubModule("线条贴图");
                        DrawPropertySmart("_UseLineTex", propDict, materialEditor);
                        DrawPropertySmart("_LineTex", propDict, materialEditor);
                        DrawPropertySmart("_LineColor", propDict, materialEditor);
                        DrawPropertySmart("_LineTexUseUV2", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool diffuseOverlayState = stateDict["基础贴图叠色"];
                    diffuseOverlayState = DrawSubFoldout("基础贴图叠色", diffuseOverlayState, materialEditor, GetSubModuleProps("基础贴图叠色"));
                    stateDict["基础贴图叠色"] = diffuseOverlayState;
                    if (diffuseOverlayState)
                    {
                        DrawBeginnerTipForSubModule("基础贴图叠色");
                        DrawPropertySmart("_UseDiffuseOverlayTex", propDict, materialEditor);
                        DrawPropertySmart("_DiffuseOverlayTex", propDict, materialEditor);
                        DrawPropertySmart("_DiffuseOverlayStrength", propDict, materialEditor);
                        DrawPropertySmart("_DiffuseOverlayUseUV2", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool normState = stateDict["法线贴图"];
                    normState = DrawSubFoldout("法线贴图", normState, materialEditor, GetSubModuleProps("法线贴图"));
                    stateDict["法线贴图"] = normState;
                    if (normState)
                    {
                        DrawBeginnerTipForSubModule("法线贴图");
                        DrawPropertySmart("_UseNormalMap", propDict, materialEditor);
                        DrawPropertySmart("_CharNormalTex", propDict, materialEditor);
                        DrawPropertySmart("_NormalUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_CharNormalStrength", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool roughnessState = stateDict["粗糙度贴图"];
                    roughnessState = DrawSubFoldout("粗糙度贴图", roughnessState, materialEditor, GetSubModuleProps("粗糙度贴图"));
                    stateDict["粗糙度贴图"] = roughnessState;
                    if (roughnessState)
                    {
                        DrawBeginnerTipForSubModule("粗糙度贴图");
                        DrawPropertySmart("_UseRoughness", propDict, materialEditor);
                        DrawPropertySmart("_RoughnessTex", propDict, materialEditor);
                        DrawPropertySmart("_RoughnessUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_RoughnessStrength", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool aoState = stateDict["AO贴图"];
                    aoState = DrawSubFoldout("AO贴图", aoState, materialEditor, GetSubModuleProps("AO贴图"));
                    stateDict["AO贴图"] = aoState;
                    if (aoState)
                    {
                        DrawBeginnerTipForSubModule("AO贴图");
                        DrawPropertySmart("_UseAO", propDict, materialEditor);
                        DrawPropertySmart("_AOTex", propDict, materialEditor);
                        DrawPropertySmart("_AOUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_AOStrength", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool metState = stateDict["金属度"];
                    metState = DrawSubFoldout("金属度", metState, materialEditor, GetSubModuleProps("金属度"));
                    stateDict["金属度"] = metState;
                    if (metState)
                    {
                        DrawBeginnerTipForSubModule("金属度");
                        DrawPropertySmart("_UseMetallic", propDict, materialEditor);
                        DrawPropertySmart("_MetallicTex", propDict, materialEditor);
                        DrawPropertySmart("_MetallicUseUV2", propDict, materialEditor);
                        DrawPropertySmart("_Metallic", propDict, materialEditor);
                        DrawPropertySmart("_MetallicDiffuseAmount", propDict, materialEditor);
                        DrawPropertySmart("_MetallicSpecularMult", propDict, materialEditor);
                        DrawPropertySmart("_MetallicGlossMult", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool gampState = stateDict["位置边缘光"];
                    gampState = DrawSubFoldout("位置边缘光", gampState, materialEditor, GetSubModuleProps("位置边缘光"));
                    stateDict["位置边缘光"] = gampState;
                    if (gampState)
                    {
                        DrawBeginnerTipForSubModule("位置边缘光");
                        DrawPropertySmart("_GampColor", propDict, materialEditor);
                        DrawPropertySmart("_PosGamp", propDict, materialEditor);
                        DrawPropertySmart("_GampUseVertexColor", propDict, materialEditor);
                        DrawPropertySmart("_GampIntensity", propDict, materialEditor);
                        DrawPropertySmart("_GampIntensityUseParticleCustom", propDict, materialEditor);
                        if (mat.HasProperty("_GampIntensityUseParticleCustom") && mat.GetFloat("_GampIntensityUseParticleCustom") > 0.5f)
                        {
                            DrawPropertySmart("_GampIntensityCustomData", propDict, materialEditor);
                            DrawPropertySmart("_GampIntensityCustomChannel", propDict, materialEditor);
                        }
                        DrawPropertySmart("_GampOne", propDict, materialEditor);
                        DrawPropertySmart("_HighLine", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool fnelState = stateDict["菲涅尔效果"];
                    fnelState = DrawSubFoldout("菲涅尔效果", fnelState, materialEditor, GetSubModuleProps("菲涅尔效果"));
                    stateDict["菲涅尔效果"] = fnelState;
                    if (fnelState)
                    {
                        DrawBeginnerTipForSubModule("菲涅尔效果");
                        DrawPropertySmart("_Frenel", propDict, materialEditor);
                        DrawPropertySmart("_Fresnecolor", propDict, materialEditor);
                        DrawPropertySmart("_FrenelPower", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool emitState = stateDict["自发光"];
                    emitState = DrawSubFoldout("自发光", emitState, materialEditor, GetSubModuleProps("自发光"));
                    stateDict["自发光"] = emitState;
                    if (emitState)
                    {
                        DrawBeginnerTipForSubModule("自发光");
                        DrawPropertySmart("_Light", propDict, materialEditor);
                        DrawPropertySmart("_LightUseVertexColor", propDict, materialEditor);
                        DrawPropertySmart("_LightMask", propDict, materialEditor);
                        DrawPropertySmart("_2UV", propDict, materialEditor);
                        DrawPropertySmart("_Lightcolor", propDict, materialEditor);
                        DrawPropertySmart("_LightIntensity", propDict, materialEditor);
                        DrawPropertySmart("_LightIntensityUseParticleCustom", propDict, materialEditor);
                        if (mat.HasProperty("_LightIntensityUseParticleCustom") && mat.GetFloat("_LightIntensityUseParticleCustom") > 0.5f)
                        {
                            DrawPropertySmart("_LightIntensityCustomData", propDict, materialEditor);
                            DrawPropertySmart("_LightIntensityCustomChannel", propDict, materialEditor);
                        }
                        DrawPropertySmart("_LightFrenel", propDict, materialEditor);
                        DrawPropertySmart("_LightFresnelPower", propDict, materialEditor);
                        DrawPropertySmart("_LMU_Speed", propDict, materialEditor);
                        DrawPropertySmart("_LMV_Spped", propDict, materialEditor);
                    }
                }
                else if (isDissolve)
                {
                    foreach (var propName in mod.props)
                    {
                        DrawPropertySmart(propName, propDict, materialEditor);
                        if (propName == "_DissolveUseParticleCustom" && mat.HasProperty("_DissolveUseParticleCustom") && mat.GetFloat("_DissolveUseParticleCustom") > 0.5f)
                        {
                            DrawPropertySmart("_DissolveThresholdCustomData", propDict, materialEditor);
                            DrawPropertySmart("_DissolveThresholdCustomChannel", propDict, materialEditor);
                        }
                    }
                    GUILayout.Space(5);
                    bool addTexState = stateDict["附加溶解贴图"];
                    addTexState = DrawSubFoldout("附加溶解贴图", addTexState, materialEditor, GetSubModuleProps("附加溶解贴图"));
                    stateDict["附加溶解贴图"] = addTexState;
                    if (addTexState)
                    {
                        DrawBeginnerTipForSubModule("附加溶解贴图");
                        DrawPropertySmart("_DissolveTex2", propDict, materialEditor);
                        DrawPropertySmart("_DissolveTex2UseUV2", propDict, materialEditor);
                        DrawPropertySmart("_UseDissolveTex2", propDict, materialEditor);
                        DrawPropertySmart("_DissolveBlendMode", propDict, materialEditor);
                        DrawPropertySmart("_DissolveBlendStrength", propDict, materialEditor);
                        DrawPropertySmart("_DissolveTex2USpeed", propDict, materialEditor);
                        DrawPropertySmart("_DissolveTex2VSpeed", propDict, materialEditor);
                        DrawPropertySmart("_DissolveTex2Rot", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool dirState = stateDict["方向溶解"];
                    dirState = DrawSubFoldout("方向溶解", dirState, materialEditor, GetSubModuleProps("方向溶解"));
                    stateDict["方向溶解"] = dirState;
                    if (dirState)
                    {
                        DrawBeginnerTipForSubModule("方向溶解");
                        DrawPropertySmart("_DissolveUseHeight", propDict, materialEditor);
                        DrawPropertySmart("_DissolveAngleX", propDict, materialEditor);
                        DrawPropertySmart("_DissolveAngleY", propDict, materialEditor);
                        DrawPropertySmart("_DissolveAngleZ", propDict, materialEditor);
                        DrawPropertySmart("_DissolveHeightBottom", propDict, materialEditor);
                        DrawPropertySmart("_DissolveHeightTop", propDict, materialEditor);
                        DrawPropertySmart("_DissolveHeightReverse", propDict, materialEditor);
                        DrawPropertySmart("_DissolveMainEdgeNoise", propDict, materialEditor);
                        DrawPropertySmart("_DissolveEdgeNoiseStr", propDict, materialEditor);
                    }
                }
                else if (isIce)
                {
                    foreach (var propName in mod.props)
                    {
                        DrawPropertySmart(propName, propDict, materialEditor);
                        if (propName == "_IceProgress" && mat.HasProperty("_IceProgressUseParticleCustom"))
                        {
                            DrawPropertySmart("_IceProgressUseParticleCustom", propDict, materialEditor);
                            if (mat.GetFloat("_IceProgressUseParticleCustom") > 0.5f)
                            {
                                DrawPropertySmart("_IceProgressCustomData", propDict, materialEditor);
                                DrawPropertySmart("_IceProgressCustomChannel", propDict, materialEditor);
                            }
                        }
                    }
                    GUILayout.Space(5);
                    bool edgeNoiseState = stateDict["结冰边缘扰动"];
                    edgeNoiseState = DrawSubFoldout("结冰边缘扰动", edgeNoiseState, materialEditor, GetSubModuleProps("结冰边缘扰动"));
                    stateDict["结冰边缘扰动"] = edgeNoiseState;
                    if (edgeNoiseState)
                    {
                        DrawBeginnerTipForSubModule("结冰边缘扰动");
                        DrawPropertySmart("_IceEdgeNoiseToggle", propDict, materialEditor);
                        DrawPropertySmart("_IceEdgeNoiseStrength", propDict, materialEditor);
                        DrawPropertySmart("_IceEdgeNoiseSize", propDict, materialEditor);
                        DrawPropertySmart("_IceProgressTexNoiseStrength", propDict, materialEditor);
                        DrawPropertySmart("_IceProgressTexNoiseSize", propDict, materialEditor);
                        DrawPropertySmart("_IceEdgeNoiseUseUV2", propDict, materialEditor);
                    }
                    GUILayout.Space(5);
                    bool iceDissState = stateDict["结冰溶解"];
                    iceDissState = DrawSubFoldout("结冰溶解", iceDissState, materialEditor, GetSubModuleProps("结冰溶解"));
                    stateDict["结冰溶解"] = iceDissState;
                    if (iceDissState)
                    {
                        DrawBeginnerTipForSubModule("结冰溶解");
                        DrawPropertySmart("_IceDissolveToggle", propDict, materialEditor);
                        DrawPropertySmart("_IceDissolveTex", propDict, materialEditor);
                        DrawPropertySmart("_IceDissolveThreshold", propDict, materialEditor);
                        DrawPropertySmart("_IceDissolveThresholdUseParticleCustom", propDict, materialEditor);
                        if (mat.HasProperty("_IceDissolveThresholdUseParticleCustom") && mat.GetFloat("_IceDissolveThresholdUseParticleCustom") > 0.5f)
                        {
                            DrawPropertySmart("_IceDissolveThresholdCustomData", propDict, materialEditor);
                            DrawPropertySmart("_IceDissolveThresholdCustomChannel", propDict, materialEditor);
                        }
                        DrawPropertySmart("_IceDissolveReverse", propDict, materialEditor);
                        DrawPropertySmart("_IceUseUV2", propDict, materialEditor);
                    }
                }
                else
                {
                    foreach (var propName in mod.props) DrawPropertySmart(propName, propDict, materialEditor);
                }
                EditorGUILayout.Space(5);
            }
        }
        MaterialEditor.ApplyMaterialPropertyDrawers(mat);
    }

    private void DrawPropertySmart(string propName, Dictionary<string, MaterialProperty> dict, MaterialEditor editor)
    {
        if (!dict.TryGetValue(propName, out var prop)) return;
        int prevIndent = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;

        if (propName == "_CullMode")
        {
            string[] options = { "双面显示", "显示背面", "显示正面" };
            int currentMode = Mathf.RoundToInt(prop.floatValue);
            currentMode = Mathf.Clamp(currentMode, 0, 2);

            // 使用与其他 Toggle 完全一致的布局
            Rect rect = EditorGUILayout.GetControlRect(true, 18f);
            Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
            buttonRect.width = Mathf.Min(100f, buttonRect.width);   // 与 Toggle 按钮宽度一致

            // 创建居中样式（如果还没创建）
            if (centerButtonStyle == null)
            {
                centerButtonStyle = new GUIStyle(EditorStyles.miniButton);
                centerButtonStyle.alignment = TextAnchor.MiddleCenter;
            }

            if (GUI.Button(buttonRect, options[currentMode], centerButtonStyle))
            {
                editor.RegisterPropertyChangeUndo(prop.displayName);
                prop.floatValue = (currentMode + 1) % 3;
            }
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_AlphaMode")
        {
            DrawAlphaModeButton(prop, dict, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_TransparentZWrite")
        {
            // 只有在半透明模式下才显示“半透明深度写入”。
            // 裁剪模式下该选项不会生效，隐藏可以避免误解。
            if (GetFloatFromDict(dict, "_AlphaMode", 0f) <= 0.5f)
            {
                EditorGUI.indentLevel = prevIndent;
                return;
            }

            DrawTransparentZWriteButton(prop, dict, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_TransparentAlphaClip")
        {
            // 只有在半透明模式下才显示“半透明保留 Alpha 裁切”。
            // 裁剪模式本身已经固定执行 AlphaClip，不需要额外开关。
            if (GetFloatFromDict(dict, "_AlphaMode", 0f) <= 0.5f)
            {
                EditorGUI.indentLevel = prevIndent;
                return;
            }

            DrawToggleButton(propName, prop, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_DiffuseAlphaUseVertexColor")
        {
            if (GetFloatFromDict(dict, "_AlphaMode", 0f) <= 0.5f)
            {
                EditorGUI.indentLevel = prevIndent;
                return;
            }

            DrawToggleButton(propName, prop, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_UseCustomRenderQueue")
        {
            DrawCustomRenderQueueToggle(prop, dict, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (propName == "_CustomRenderQueue")
        {
            // 只有开启自定义渲染队列时才显示具体数值，避免和自动透明模式混淆。
            if (GetFloatFromDict(dict, "_UseCustomRenderQueue", 0f) <= 0.5f)
            {
                EditorGUI.indentLevel = prevIndent;
                return;
            }

            DrawRenderQueueField(prop, dict, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (IsUVSelectProperty(propName))
        {
            DrawUVSelectPopup(prop, editor);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (IsParticleCustomDataProperty(propName))
        {
            DrawCenteredPopup(prop, editor, particleCustomDataOptions);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (IsParticleCustomChannelProperty(propName))
        {
            DrawCenteredPopup(prop, editor, particleCustomChannelOptions);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }

        if (prop.type == MaterialProperty.PropType.Texture)
        {
            editor.SetDefaultGUIWidths();
            editor.TextureProperty(prop, prop.displayName);
            DrawBeginnerTipForProperty(propName);
            EditorGUI.indentLevel = prevIndent;
            return;
        }
        if (prop.type == MaterialProperty.PropType.Float && prop.flags == MaterialProperty.PropFlags.None)
        {
            if (IsToggleProperty(propName))
            {
                DrawToggleButton(propName, prop, editor);
                DrawBeginnerTipForProperty(propName);
                EditorGUI.indentLevel = prevIndent;
                return;
            }
        }
        editor.ShaderProperty(prop, prop.displayName);
        DrawBeginnerTipForProperty(propName);
        EditorGUI.indentLevel = prevIndent;
    }

    private bool IsToggleProperty(string name) => toggleDisplayNames.ContainsKey(name);

    private bool IsUVSelectProperty(string name) => uvSelectPropertyNames.Contains(name);

    private bool IsParticleCustomDataProperty(string name) => particleCustomDataPropertyNames.Contains(name);

    private bool IsParticleCustomChannelProperty(string name) => particleCustomChannelPropertyNames.Contains(name);


    private static readonly Dictionary<string, string> moduleTips = new Dictionary<string, string>
    {
        { "角色基础", "角色最核心的颜色、明暗、高光和透明裁切设置。通常先调整基础贴图、基础颜色、阴影强度和高光，再根据需要打开下面的细分功能。（二次元角色一般不需要高光度和光泽度）" },
        { "溶解", "控制模型按照溶解贴图逐渐出现或消失。溶解阈值越高，被裁掉的区域越多；开启粒子 Custom 后可以用粒子生命周期控制溶解进度。" },
        { "描边", "给模型外轮廓增加线条，常用于二次元角色、卡通边缘或受击强调。描边粗细通常不要过大，否则容易穿插或边缘膨胀明显。" },
        { "结冰", "在角色表面叠加冰层、冰边缘、冰法线和冰反射效果。适合做冻结、冰覆盖、冰碎裂前的状态变化。" },
        { "曲面细分", "通过 Tessellation 增加模型表面细分，主要用于需要更细腻位移或边缘的效果。会增加性能消耗，移动端或大量角色时谨慎开启。" }
    };

    private static readonly Dictionary<string, string> subModuleTips = new Dictionary<string, string>
    {
        { "光照遮罩贴图", "光影分区图（比如鬼灭模型的 Ls 贴图），会和光照方向贴图、实时阴影、暗部色统一合成。白色区域保留基础暗部并继续随光照变深，深灰高光区直接保持基础贴图颜色/高光保护，中灰或浅灰区域正常跟随光照变化。" },
        { "暗部色贴图", "用贴图作为统一阴影区域的目标颜色。最深阴影接近暗部色，中间阴影在暗部色和基础贴图之间保留层次，亮部保持基础贴图/LightDir。" },
        { "线条贴图", "在线条贴图指定的位置叠加线条颜色，适合做衣服花纹、面部线、局部墨线或贴图画出的装饰线。" },
        { "基础贴图叠色", "在基础贴图颜色上再叠加一张颜色贴图，用来增加局部颜色变化、渐变、花纹或额外色块。" },
        { "法线贴图", "改变光照计算时的表面方向，让平面看起来有凹凸细节。不会真正改变模型轮廓。开启后需要配合高光度和光泽度使用。" },
        { "粗糙度贴图", "控制高光扩散程度。粗糙度高，高光更散更柔；粗糙度低，高光更集中更亮。" },
        { "AO贴图", "环境遮蔽贴图，用来压暗缝隙、凹槽和遮挡区域，让模型层次更明显。" },
        { "金属度", "让材质表现更接近金属。金属区域通常高光更强、漫反射基础色更少。开启后需配合高光度和光泽度使用。" },
        { "位置边缘光", "根据模型位置或高度叠加边缘光，可做从脚到头的高亮、受击亮边、能量附着或角色边缘强调。" },
        { "菲涅尔效果", "根据视角在模型边缘产生高亮，越接近轮廓越明显。" },
        { "自发光", "让指定区域不依赖灯光直接发亮。" },
        { "附加溶解贴图", "在主溶解贴图之外再混合一张溶解图，用来增加方向趋势、渐变控制或更复杂的溶解形状。" },
        { "方向溶解", "用方向、高度或位置来影响溶解，让溶解从上到下、从下到上或沿某个方向推进。" },
        { "结冰溶解", "控制冰层自身的溶解消失或出现。" },
        { "结冰边缘扰动", "用噪波扰动冰层边缘，让结冰边界更自然，不会像直线或规则遮罩一样生硬。" }
    };

    private static readonly Dictionary<string, string> propertyTips = new Dictionary<string, string>
    {
        { "_CullMode", "控制模型显示哪一面。双面显示最不容易漏面，但性能略高；显示正面/背面适合特殊模型或描边需求。" },
        { "_AlphaMode", "切换材质的透明方式。不透明裁剪适合硬边镂空、头发卡片和需要写入深度的角色；半透明会使用基础颜色 Alpha 和基础贴图 Alpha 做柔和透明，但排序和穿插可能受透明渲染限制。" },
        { "_TransparentZWrite", "只在半透明模式下起作用。关闭时透明叠加更正常；写入时模型自身前后遮挡更稳定，但可能挡掉后方透明层。角色虚影、冰壳或半透明模型内部穿插明显时可以开启。" },
        { "_TransparentAlphaClip", "只在半透明模式下显示。开启后会先用基础贴图 Alpha 和 Alpha透明裁切阈值做硬裁切，再继续输出半透明；适合半透明模型仍需要镂空、硬边或去掉脏边的情况。" },
        { "_UseCustomRenderQueue", "开启后可以手动指定材质 Render Queue。关闭时会根据透明模式自动使用裁剪队列或半透明队列。" },
        { "_CustomRenderQueue", "材质实际渲染队列。数值越小越早渲染，越大越晚渲染；常用参考：AlphaTest 约 2450，Transparent 约 3000。" },
        { "_LightDir", "光照方向贴图，用来辅助控制明暗分布。" },
        { "_Diffuse", "模型的基础颜色贴图，通常是角色最主要的颜色来源。" },
        { "_DiffuseUseUV2", "选择基础贴图使用哪套 UV。普通模型需要模型本身有对应 UV；粒子系统需要对应 Vertex Stream。" },
        { "_SelfAlpha", "开启后会使用基础贴图 Alpha 参与透明裁切。" },
        { "_DiffuseUseParticleColor", "开启后基础颜色会乘顶点色。用于粒子系统时，Color over Lifetime 可以改变模型颜色。" },
        { "_DiffuseAlphaUseVertexColor", "只在半透明模式下显示。开启后，粒子系统顶点色的 Alpha 会参与模型最终透明度，适合让粒子的透明渐变控制模型淡入淡出。裁剪模式下隐藏，是为了避免和硬裁切混淆。" },
        { "_DiffuseCOloer", "基础颜色，会和基础贴图相乘。HDR 颜色可让整体更亮。" },
        { "_ReceiveShadowStrength", "控制实时阴影对角色的影响强度。" },
        { "_Cutoff", "透明裁切阈值。数值越高，Alpha 较低的区域越容易被裁掉。" },
        { "_Specular", "高光亮度基础值。" },
        { "_Gloss", "高光集中程度，数值越高高光越尖锐。" },
        { "_UseLineTex", "是否启用线条贴图。" },
        { "_LineTex", "黑白或灰度线条遮罩，用来决定哪些区域显示线条。" },
        { "_LineColor", "线条叠加颜色。使用 HDR 可以做发光线条。" },
        { "_UseNormalMap", "是否启用角色法线贴图。" },
        { "_CharNormalStrength", "法线凹凸强度。过高可能导致明暗变脏。" },
        { "_UseRoughness", "是否启用粗糙度贴图控制高光扩散。" },
        { "_RoughnessStrength", "粗糙度贴图影响强度。" },
        { "_UseAO", "是否启用 AO 环境遮蔽贴图。" },
        { "_AOStrength", "AO 压暗强度。过高会让模型显脏。" },
        { "_UseMetallic", "是否启用金属度流程。" },
        { "_Metallic", "金属度强度。适合武器、饰品、装甲，不建议整个人物大面积开高。" },
        { "_PosGamp", "是否开启位置边缘光。" },
        { "_GampUseVertexColor", "开启后位置边缘光会乘顶点色，粒子系统颜色也可以影响边缘光颜色。" },
        { "_GampIntensity", "位置边缘光的整体亮度倍率。" },
        { "_GampIntensityUseParticleCustom", "开启后位置边缘光强度由粒子 Custom 通道控制，适合做生命周期闪光。" },
        { "_GampIntensityCustomData", "选择读取 C1 还是 C2。对应粒子系统 Custom1 / Custom2。" },
        { "_GampIntensityCustomChannel", "选择读取 X/Y/Z/W 哪个通道。" },
        { "_HighLine", "位置边缘光的高度参考，影响边缘光渐变范围。" },
        { "_Frenel", "是否开启视角边缘高亮。" },
        { "_FrenelPower", "菲涅尔边缘强度或范围控制。" },
        { "_Light", "是否启用自发光。" },
        { "_LightUseVertexColor", "开启后自发光会乘顶点色，适合粒子颜色影响发光颜色。" },
        { "_LightMask", "自发光遮罩，白色区域发光，黑色区域不发光。" },
        { "_LightIntensity", "自发光整体强度。" },
        { "_LightIntensityUseParticleCustom", "开启后自发光强度由粒子 Custom 控制，适合出生爆亮或逐渐熄灭。" },
        { "_LightIntensityCustomData", "选择自发光强度读取 C1 还是 C2。" },
        { "_LightIntensityCustomChannel", "选择自发光强度读取 X/Y/Z/W 哪个通道。" },
        { "_UseNewDissolve", "是否启用溶解模块。" },
        { "_DissolveTex", "主溶解贴图，通常用噪波或黑白纹理控制溶解形状。" },
        { "_DissolveThreshold", "溶解进度。数值越大，模型被裁掉的区域通常越多。" },
        { "_DissolveUseParticleCustom", "开启后溶解阈值由粒子 Custom 控制，适合用生命周期做模型出现/消失。" },
        { "_DissolveThresholdCustomData", "选择溶解阈值读取 C1 还是 C2。" },
        { "_DissolveThresholdCustomChannel", "选择溶解阈值读取 X/Y/Z/W 哪个通道。" },
        { "_DissolveEdgeEnable", "是否启用溶解亮边。关闭后仍然可以正常溶解裁切，但不会额外叠加溶解边缘发光。" },
        { "_DissolveEdgeWidth", "溶解边缘宽度。越大，边缘带越宽。" },
        { "_DissolveEdgeIntensity", "溶解边缘亮度强度。" },
        { "_DissolveEdgeColor", "溶解边缘颜色。HDR 颜色可做发光边缘。" },
        { "_DissolveAlphaClip", "控制溶解区域是否参与透明裁切。" },
        { "_DissolveTex2", "附加溶解贴图，用于增加额外溶解趋势或方向。" },
        { "_UseDissolveTex2", "是否启用附加溶解贴图。" },
        { "_DissolveBlendMode", "控制主溶解贴图和附加溶解贴图的混合方式。" },
        { "_DissolveUseHeight", "开启后用方向/高度影响溶解进度。" },
        { "_DissolveHeightReverse", "反转方向溶解趋势。" },
        { "_EnableOutline", "是否启用描边 Pass。" },
        { "_OutLine", "描边粗细。数值越大，轮廓越粗。" },
        { "_OutLineMaskUseUV2", "描边遮罩使用的 UV。" },
        { "_OutLineColor", "描边颜色。" },
        { "_OutLineTex", "描边贴图或描边边缘控制贴图。" },
        { "_OutlineUseUV2", "描边贴图使用的 UV。" },
        { "_OutLinePower", "描边贴图/边缘的影响强度。" },
        { "_EnableIce", "是否启用结冰模块。" },
        { "_IceSurfaceTex", "冰层表面贴图，用来提供冰纹、霜面或晶体纹理。" },
        { "_IceColor", "冰层颜色。通常用偏蓝、青、白的 HDR/半透明颜色。" },
        { "_IceAlpha", "冰层覆盖透明度或混合强度。" },
        { "_IceProgress", "结冰覆盖进度。数值变化可以让冰层逐渐出现。" },
        { "_IceProgressUseParticleCustom", "开启后结冰过程由粒子 Custom 控制。" },
        { "_IceProgressCustomData", "选择结冰过程读取 C1 还是 C2。" },
        { "_IceProgressCustomChannel", "选择结冰过程读取 X/Y/Z/W 哪个通道。" },
        { "_IceProgressTex", "结冰过程贴图，用来决定冰从哪里开始覆盖。" },
        { "_IceDirectionToggle", "开启后结冰过程会受到方向/高度影响。" },
        { "_IceBumpTex", "冰面扰动贴图，用来让冰面有不规则纹理。" },
        { "_IceBumpStrength", "冰面扰动强度。" },
        { "_IceReflectTex", "冰反射贴图，用来模拟冰面的高亮和反光。" },
        { "_IceReflectStrength", "冰反射强度。" },
        { "_IceNormalTex", "冰层法线贴图，让冰面有细小凹凸。" },
        { "_IceNormalStrength", "冰层法线强度。" },
        { "_IceEdgeNoiseToggle", "是否启用冰边缘扰动。" },
        { "_IceEdgeNoiseStrength", "冰边缘噪波扰动强度。" },
        { "_IceEdgeNoiseSize", "冰边缘噪波大小。数值变化会影响噪波纹理缩放。" },
        { "_IceDissolveToggle", "是否启用冰层溶解。" },
        { "_IceDissolveTex", "冰层溶解贴图，用来控制冰碎裂/融化形状。" },
        { "_IceDissolveThreshold", "冰层溶解阈值。数值变化会让冰层逐渐消失。" },
        { "_IceDissolveThresholdUseParticleCustom", "开启后冰层溶解阈值由粒子 Custom 控制。" },
        { "_IceDissolveThresholdCustomData", "选择冰层溶解读取 C1 还是 C2。" },
        { "_IceDissolveThresholdCustomChannel", "选择冰层溶解读取 X/Y/Z/W 哪个通道。" },
        { "_IceDissolveReverse", "反转冰层溶解方向。" },
        { "_TessellationEnabled", "是否启用曲面细分。" },
        { "_EdgeLength", "细分边长控制。数值越小细分越密，性能消耗越高。" },
        { "_AOTex", "AO 环境遮蔽贴图。白色表示不压暗，黑色表示压暗，常用于衣缝、头发缝隙、装备凹槽等细节位置。" },
        { "_AOUseUV2", "选择 AO 贴图使用的 UV。默认使用该贴图自己的常规第一套 UV；UV2/UV3/UV4 需要模型或粒子流里确实提供对应 UV，否则可能采样异常。" },
        { "_CharNormalTex", "角色法线贴图。用于增加表面凹凸感，例如布料纹理、金属刻痕、冰面细节；它不会改变模型真实轮廓。" },
        { "_NormalUseUV2", "选择法线贴图使用的 UV。默认使用法线贴图自己的常规第一套 UV，不再跟随基础贴图；只有法线贴图专门画在其他 UV 套上时才切到 UV2/UV3/UV4。" },
        { "_RoughnessTex", "粗糙度贴图。一般越白表示越粗糙、高光越散；越黑表示越光滑、高光越集中。" },
        { "_RoughnessUseUV2", "选择粗糙度贴图使用的 UV。默认使用粗糙度贴图自己的常规第一套 UV，不再跟随基础贴图；特殊展开或粒子模型才需要切换。" },
        { "_MetallicTex", "金属度贴图。白色区域更像金属，黑色区域更像非金属；适合武器、饰品、甲片等局部材质。" },
        { "_MetallicUseUV2", "选择金属度贴图使用的 UV。默认使用金属度贴图自己的常规第一套 UV，不再跟随基础贴图；需要贴图和模型 UV 对应，否则金属区域会错位。" },
        { "_MetallicDiffuseAmount", "金属区域保留多少基础贴图颜色。数值越低越接近真实金属，颜色主要来自反射/高光；数值越高越保留贴图颜色。" },
        { "_MetallicSpecularMult", "金属区域高光强度倍率。数值越大金属高光越亮，过高可能会发白或过曝。" },
        { "_MetallicGlossMult", "金属区域光泽度倍率。数值越大高光越集中、越锐利；数值低则更柔。" },
        { "_UseLightMaskTex", "是否启用光影遮罩。开启后，白色区域作为基础阴影层；深灰高光区直接保持基础贴图颜色；中灰/浅灰正常跟随光照。" },
        { "_LightMaskTex", "光照遮罩贴图，通常是灰度图或 RGB 灰度图。白色区域作为基础常驻阴影层；深灰高光区会直接保持基础贴图颜色，不被光照方向贴图、实时阴影或暗部色贴图压暗；浅灰/中灰区域保持正常光影变化。" },
        { "_LightMaskUseUV2", "选择光照遮罩贴图使用的 UV。默认使用该贴图自己的常规第一套 UV，不再跟随基础贴图；如果遮罩图是单独展开的，就切到对应 UV2/UV3/UV4。" },
        { "_LightMaskStrength", "光照遮罩强度。0 表示光照遮罩贴图不影响阴影/高光保护；1 表示完整使用光照遮罩贴图。会同时控制白色固定阴影区和深灰高光保护区。" },
        { "_LightShadowStrength", "整体光照阴影强度。0 表示 光照方向贴图、Ls 遮罩和实时阴影基本不压暗；1 表示保持当前完整阴影；1~2 会在当前基础上继续压暗。用于整体控制角色阴影强弱，而不改变各贴图之间的融合逻辑。" },
        { "_DarkColorTex", "暗部色贴图。用颜色贴图替换或控制阴影区域颜色。" },
        { "_DarkColorUseUV2", "选择暗部色贴图使用的 UV。默认使用当前模型常规 UV；如果暗部色是单独展开的图，可以切到 UV2/UV3/UV4。" },
        { "_DarkColorStrength", "暗部色混合强度。控制暗部色贴图整体参与程度。0 表示不参与，1 表示按当前阴影区域完整参与；如果只想改变阴影压暗程度，优先调“暗部色深度”。" },
        { "_DarkColorDepth", "暗部色深度。控制暗部色作为阴影染色/压暗的深度：0 不压暗，1 正常使用暗部色染色，1~2 在此基础上继续压暗。最深处不会替换基础纹理，会保留基础贴图和基础贴图叠色的细节。" },
        { "_UseDarkColorTex", "是否启用暗部色贴图。开启后阴影区域可以使用专门的暗部颜色，而不是只把基础色简单乘黑。" },
        { "_LineTexUseUV2", "选择线条贴图使用的 UV。默认使用该贴图自己的常规第一套 UV；如果线条图是单独展开或粒子特殊 UV，需要切换。" },
        { "_UseDiffuseOverlayTex", "是否启用基础贴图叠色。开启后会在基础贴图上额外叠加一张颜色贴图，用于渐变、局部染色或花纹。" },
        { "_DiffuseOverlayTex", "基础贴图叠色。可以给角色增加额外颜色层，例如冰蓝渐变、受击变色、局部纹路或衣服花纹。" },
        { "_DiffuseOverlayStrength", "基础贴图叠色强度。0 表示不影响，1 表示完全按当前叠色逻辑参与混合。" },
        { "_DiffuseOverlayUseUV2", "选择基础贴图叠色使用的 UV。默认使用叠色贴图自己的常规第一套 UV；用于让叠色图和指定 UV 展开方式对应。" },
        { "_GampColor", "位置边缘光颜色。通常用于角色从脚到头的渐变高亮、能量附着、冰霜边缘或受击亮边。" },
        { "_GampOne", "反转位置边缘光方向。开启后原本从下往上的效果会变成相反方向。" },
        { "_Fresnecolor", "菲涅尔颜色。控制视角边缘高亮的颜色。" },
        { "_Lightcolor", "自发光颜色。控制发光区域的颜色和亮度，HDR 颜色可以产生更强的发光感。" },
        { "_LightFrenel", "是否让自发光带有菲涅尔边缘效果。开启后发光会更集中在模型轮廓边缘。" },
        { "_LightFresnelPower", "自发光菲涅尔范围/强度。数值越大边缘越集中，数值越小覆盖范围更宽。" },
        { "_2UV", "选择自发光遮罩或颜色相关贴图使用的 UV。默认使用常规 UV；切换 UV2/UV3/UV4 前要确认模型有对应 UV。" },
        { "_LMU_Speed", "自发光遮罩 U 方向流动速度。正数向一个方向流动，负数反向；0 表示不流动。" },
        { "_LMV_Spped", "自发光遮罩 V 方向流动速度。正负控制上下方向，和 U 速度组合可以做斜向能量流动。" },
        { "_DissolveUSpeed", "主溶解贴图 U 方向流动速度。用于让溶解噪波横向移动，增加动态感。" },
        { "_DissolveVSpeed", "主溶解贴图 V 方向流动速度。用于让溶解噪波纵向移动，可和 U 速度组合成斜向流动。" },
        { "_DissolveTexRot", "主溶解贴图旋转角度。用于调整噪波方向，让溶解纹理和角色或特效方向更匹配。" },
        { "_DissolveScreenUV", "使用屏幕空间 UV 采样溶解。开启后溶解纹理更像贴在屏幕上，不跟随模型 UV。" },
        { "_DissolveUseUV1", "选择主溶解贴图使用的 UV。默认保持原本逻辑；切换到其他 UV 可让溶解按不同展开方式发生。" },
        { "_DissolveEdgeHardness", "溶解边缘硬度。数值越高边缘越软，数值低则边缘更硬。" },
        { "_DissolveTex2UseUV2", "选择附加溶解贴图使用的 UV。默认使用附加溶解贴图自己的常规第一套 UV，不再跟随主溶解贴图或屏幕空间溶解；用于让第二张溶解图按指定 UV 展开。" },
        { "_DissolveBlendStrength", "附加溶解贴图混合强度。数值越高，第二张溶解图对最终溶解形状影响越明显。" },
        { "_DissolveTex2USpeed", "附加溶解贴图 U 方向流动速度。可让第二层噪波横向运动。" },
        { "_DissolveTex2VSpeed", "附加溶解贴图 V 方向流动速度。可让第二层噪波纵向运动。" },
        { "_DissolveTex2Rot", "附加溶解贴图旋转角度。用于单独调整第二张溶解图方向。" },
        { "_DissolveAngleX", "方向溶解的 X 轴旋转角度。用于改变溶解推进方向，不确定时保持 0。" },
        { "_DissolveAngleY", "方向溶解的 Y 轴旋转角度。" },
        { "_DissolveAngleZ", "方向溶解的 Z 轴旋转角度。" },
        { "_DissolveHeightBottom", "方向溶解底部范围。通常表示溶解方向的起点位置，和顶部范围一起决定推进区间。" },
        { "_DissolveHeightTop", "方向溶解顶部范围。通常表示溶解方向的终点位置，调节它会改变从下到上或从上到下的覆盖长度。" },
        { "_DissolveMainEdgeNoise", "是否启用主溶解边缘噪波扰动。开启后边缘会更破碎自然，而不是过于平滑。" },
        { "_DissolveEdgeNoiseStr", "主溶解边缘噪波强度。数值越大边缘越不规则，过高可能让边缘跳动或断裂明显。" },
        { "_IceSurfaceUseUV2", "选择冰层表面贴图使用的 UV。用于控制冰纹贴图贴在哪套 UV 上。" },
        { "_IceEdgeColor", "结冰边缘颜色。控制冰层推进边界或边缘发光的颜色，常用浅蓝、青色或白色 HDR。" },
        { "_IceEdgeWidth", "结冰边缘宽度。数值越大，冰层边界的过渡带越宽。" },
        { "_IceEdgeIntensity", "结冰边缘亮度强度。用于让冰边缘更亮或更像发光。" },
        { "_IceProgressTexToggle", "是否启用结冰过程贴图。开启后冰覆盖进度会受到该贴图控制，白色/黑色区域出现顺序不同。" },
        { "_IceProgressTexUseUV2", "选择结冰过程贴图使用的 UV。用于让冰覆盖的出现顺序对应指定 UV。" },
        { "_IceProgressTexRot", "结冰过程贴图旋转角度。用于调整冰覆盖纹理的方向。" },
        { "_IceEdgeBlur", "结冰边缘模糊程度。数值越大冰层过渡越柔，数值越小边界越硬。" },
        { "_IceAngleX", "结冰方向的 X 轴旋转角度。用于改变冰从哪个方向覆盖模型。" },
        { "_IceAngleY", "结冰方向的 Y 轴旋转角度。" },
        { "_IceAngleZ", "结冰方向的 Z 轴旋转角度。" },
        { "_IceHeight", "结冰高度范围。影响方向结冰从起点到终点的覆盖区间。" },
        { "_IceHeightReverse", "反转结冰高度方向。开启后结冰推进方向会反过来，例如从上到下变成从下到上。" },
        { "_IceEdgeNoiseUseUV2", "选择结冰边缘扰动噪波使用的 UV。默认跟随常规 UV，特殊噪波展开时可切换。" },
        { "_IceProgressTexNoiseStrength", "结冰过程贴图扰动强度。数值越大，冰覆盖推进边缘越不规则。" },
        { "_IceProgressTexNoiseSize", "结冰过程贴图扰动大小。用于控制扰动噪波的缩放，影响纹理颗粒大小。" },
        { "_IceUseUV2", "选择冰层溶解或冰相关采样使用的 UV。需要和冰贴图的绘制 UV 对应。" },
        { "_IceBumpUseUV2", "选择冰面扰动贴图使用的 UV。用于让冰面凹凸扰动和指定 UV 匹配。" },
        { "_IceNormalUseUV2", "选择冰层法线贴图使用的 UV。通常使用默认 UV，特殊冰法线展开时再切换。" },
    };

    private static readonly string[] uvSelectOptions = new string[] { "默认", "UV2", "UV3", "UV4" };

    private static readonly string[] particleCustomDataOptions = new string[] { "C1", "C2" };

    private static readonly string[] particleCustomChannelOptions = new string[] { "X", "Y", "Z", "W" };

    private static readonly HashSet<string> particleCustomDataPropertyNames = new HashSet<string>
    {
        "_DissolveThresholdCustomData",
        "_IceProgressCustomData",
        "_IceDissolveThresholdCustomData",
        "_LightIntensityCustomData",
        "_GampIntensityCustomData"
    };

    private static readonly HashSet<string> particleCustomChannelPropertyNames = new HashSet<string>
    {
        "_DissolveThresholdCustomChannel",
        "_IceProgressCustomChannel",
        "_IceDissolveThresholdCustomChannel",
        "_LightIntensityCustomChannel",
        "_GampIntensityCustomChannel"
    };

    private static readonly HashSet<string> uvSelectPropertyNames = new HashSet<string>
    {
        "_DiffuseUseUV2",
        "_LightMaskUseUV2",
        "_DarkColorUseUV2",
        "_LineTexUseUV2",
        "_DiffuseOverlayUseUV2",
        "_NormalUseUV2",
        "_RoughnessUseUV2",
        "_AOUseUV2",
        "_MetallicUseUV2",
        "_2UV",
        "_DissolveUseUV1",
        "_DissolveTex2UseUV2",
        "_OutLineMaskUseUV2",
        "_OutlineUseUV2",
        "_IceSurfaceUseUV2",
        "_IceProgressTexUseUV2",
        "_IceEdgeNoiseUseUV2",
        "_IceUseUV2",
        "_IceBumpUseUV2",
        "_IceNormalUseUV2"
    };

    private static readonly Dictionary<string, ToggleTexts> toggleDisplayNames = new Dictionary<string, ToggleTexts>
    {
        { "_UseNormalMap", new ToggleTexts("是", "否") },
        { "_UseMetallic", new ToggleTexts("是", "否") },
        { "_UseRoughness", new ToggleTexts("是", "否") },
        { "_UseAO", new ToggleTexts("是", "否") },
        { "_PosGamp", new ToggleTexts("是", "否") },
        { "_GampUseVertexColor", new ToggleTexts("是", "否") },
        { "_Frenel", new ToggleTexts("是", "否") },
        { "_Light", new ToggleTexts("是", "否") },
        { "_LightUseVertexColor", new ToggleTexts("是", "否") },
        { "_LightFrenel", new ToggleTexts("是", "否") },
        { "_UseNewDissolve", new ToggleTexts("是", "否") },
        { "_DissolveEdgeEnable", new ToggleTexts("是", "否") },
        { "_DissolveUseParticleCustom", new ToggleTexts("是", "否") },
        { "_IceProgressUseParticleCustom", new ToggleTexts("是", "否") },
        { "_IceDissolveThresholdUseParticleCustom", new ToggleTexts("是", "否") },
        { "_LightIntensityUseParticleCustom", new ToggleTexts("是", "否") },
        { "_GampIntensityUseParticleCustom", new ToggleTexts("是", "否") },
        { "_UseDissolveTex2", new ToggleTexts("是", "否") },
        { "_DissolveScreenUV", new ToggleTexts("是", "否") },
        { "_DissolveUseHeight", new ToggleTexts("是", "否") },
        { "_DissolveMainEdgeNoise", new ToggleTexts("是", "否") },
        { "_EnableOutline", new ToggleTexts("是", "否") },
        { "_EnableIce", new ToggleTexts("是", "否") },
        { "_IceProgressTexToggle", new ToggleTexts("是", "否") },
        { "_IceDirectionToggle", new ToggleTexts("是", "否") },
        { "_IceEdgeNoiseToggle", new ToggleTexts("是", "否") },
        { "_IceDissolveToggle", new ToggleTexts("是", "否") },
        { "_TessellationEnabled", new ToggleTexts("是", "否") },
        { "_GampOne", new ToggleTexts("是", "否") },
        { "_SelfAlpha", new ToggleTexts("是", "否") },
        { "_DiffuseUseParticleColor", new ToggleTexts("是", "否") },
        { "_DiffuseAlphaUseVertexColor", new ToggleTexts("是", "否") },
        { "_TransparentAlphaClip", new ToggleTexts("是", "否") },
        { "_DissolveHeightReverse", new ToggleTexts("是", "否") },
        { "_DissolveAlphaClip", new ToggleTexts("是", "否") },
        { "_IceHeightReverse", new ToggleTexts("是", "否") },
        { "_IceDissolveReverse", new ToggleTexts("是", "否") },
        { "_DissolveBlendMode", new ToggleTexts("开(叠加)", "关(相乘)") },
        { "_UseLightMaskTex", new ToggleTexts("是", "否") },
        { "_UseDarkColorTex", new ToggleTexts("是", "否") },
        { "_UseLineTex", new ToggleTexts("是", "否") },
        { "_UseDiffuseOverlayTex", new ToggleTexts("是", "否") },
    };


    private void DrawAlphaModeButton(MaterialProperty prop, Dictionary<string, MaterialProperty> dict, MaterialEditor editor)
    {
        string[] options = { "裁剪", "半透明" };
        int currentMode = Mathf.Clamp(Mathf.RoundToInt(prop.floatValue), 0, 1);
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        buttonRect.width = Mathf.Min(100f, buttonRect.width);

        if (centerButtonStyle == null)
        {
            centerButtonStyle = new GUIStyle(EditorStyles.miniButton);
            centerButtonStyle.alignment = TextAnchor.MiddleCenter;
        }

        if (GUI.Button(buttonRect, options[currentMode], centerButtonStyle))
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            currentMode = (currentMode + 1) % 2;
            prop.floatValue = currentMode;
            ApplyAlphaMode(editor, dict, currentMode);
            editor.PropertiesChanged();
        }
    }

    private void DrawTransparentZWriteButton(MaterialProperty prop, Dictionary<string, MaterialProperty> dict, MaterialEditor editor)
    {
        int alphaMode = GetFloatFromDict(dict, "_AlphaMode", 0f) > 0.5f ? 1 : 0;
        bool currentValue = prop.floatValue > 0.5f;
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        buttonRect.width = Mathf.Min(100f, buttonRect.width);

        if (centerButtonStyle == null)
        {
            centerButtonStyle = new GUIStyle(EditorStyles.miniButton);
            centerButtonStyle.alignment = TextAnchor.MiddleCenter;
        }

        string buttonText = currentValue ? "写入" : "关闭";
        EditorGUI.BeginDisabledGroup(alphaMode == 0);
        if (GUI.Button(buttonRect, buttonText, centerButtonStyle))
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            prop.floatValue = currentValue ? 0f : 1f;
            ApplyAlphaMode(editor, dict, alphaMode);
            editor.PropertiesChanged();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void DrawCustomRenderQueueToggle(MaterialProperty prop, Dictionary<string, MaterialProperty> dict, MaterialEditor editor)
    {
        bool currentValue = prop.floatValue > 0.5f;
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        buttonRect.width = Mathf.Min(100f, buttonRect.width);

        if (centerButtonStyle == null)
        {
            centerButtonStyle = new GUIStyle(EditorStyles.miniButton);
            centerButtonStyle.alignment = TextAnchor.MiddleCenter;
        }

        string buttonText = currentValue ? "自定义" : "自动";
        if (GUI.Button(buttonRect, buttonText, centerButtonStyle))
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            currentValue = !currentValue;
            prop.floatValue = currentValue ? 1f : 0f;

            if (currentValue && dict.TryGetValue("_CustomRenderQueue", out var queueProp))
            {
                int currentQueue = Mathf.RoundToInt(queueProp.floatValue);
                if (currentQueue <= 0) queueProp.floatValue = GetAutoRenderQueue(dict);
            }

            ApplyAlphaMode(editor, dict, GetFloatFromDict(dict, "_AlphaMode", 0f) > 0.5f ? 1 : 0);
            editor.PropertiesChanged();
        }
    }

    private void DrawRenderQueueField(MaterialProperty prop, Dictionary<string, MaterialProperty> dict, MaterialEditor editor)
    {
        int fallback = GetAutoRenderQueue(dict);
        int currentValue = GetCustomRenderQueue(dict, fallback);
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect fieldRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        fieldRect.width = Mathf.Min(100f, fieldRect.width);

        EditorGUI.BeginChangeCheck();
        int newValue = EditorGUI.IntField(fieldRect, currentValue);
        newValue = Mathf.Clamp(newValue, 0, 5000);
        if (EditorGUI.EndChangeCheck())
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            prop.floatValue = newValue;
            foreach (var obj in editor.targets)
            {
                Material mat = obj as Material;
                if (mat != null) mat.renderQueue = newValue;
            }
            editor.PropertiesChanged();
        }
    }

    private void SyncAlphaModeState(MaterialEditor editor, Dictionary<string, MaterialProperty> dict)
    {
        int mode = GetFloatFromDict(dict, "_AlphaMode", 0f) > 0.5f ? 1 : 0;
        ApplyAlphaMode(editor, dict, mode);
    }

    private void ApplyAlphaMode(MaterialEditor editor, Dictionary<string, MaterialProperty> dict, int mode)
    {
        float transparentZWrite = GetFloatFromDict(dict, "_TransparentZWrite", 0f) > 0.5f ? 1f : 0f;
        float zWrite = mode == 0 ? 1f : transparentZWrite;
        bool useCustomRenderQueue = GetFloatFromDict(dict, "_UseCustomRenderQueue", 0f) > 0.5f;
        int autoQueue = GetAutoRenderQueue(dict);
        int targetQueue = useCustomRenderQueue ? GetCustomRenderQueue(dict, autoQueue) : autoQueue;

        SetFloatIfExists(dict, "_SrcBlend", mode == 0 ? 1f : 5f);       // One / SrcAlpha
        SetFloatIfExists(dict, "_DstBlend", mode == 0 ? 0f : 10f);      // Zero / OneMinusSrcAlpha
        SetFloatIfExists(dict, "_ZWrite", zWrite);

        foreach (var obj in editor.targets)
        {
            Material mat = obj as Material;
            if (mat == null) continue;

            if (mode == 0)
            {
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            else
            {
                mat.SetOverrideTag("RenderType", "Transparent");
            }

            mat.renderQueue = targetQueue;
        }

        Material matSingle = editor.target as Material;
        if (matSingle != null) MaterialEditor.ApplyMaterialPropertyDrawers(matSingle);
    }

    private int GetAutoRenderQueue(Dictionary<string, MaterialProperty> dict)
    {
        int mode = GetFloatFromDict(dict, "_AlphaMode", 0f) > 0.5f ? 1 : 0;
        return mode == 0 ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private int GetCustomRenderQueue(Dictionary<string, MaterialProperty> dict, int fallback)
    {
        int queue = Mathf.RoundToInt(GetFloatFromDict(dict, "_CustomRenderQueue", fallback));
        if (queue <= 0) queue = fallback;
        return Mathf.Clamp(queue, 0, 5000);
    }

    private float GetFloatFromDict(Dictionary<string, MaterialProperty> dict, string name, float defaultValue)
    {
        if (dict.TryGetValue(name, out var prop)) return prop.floatValue;
        return defaultValue;
    }

    private void SetFloatIfExists(Dictionary<string, MaterialProperty> dict, string name, float value)
    {
        if (dict.TryGetValue(name, out var prop)) prop.floatValue = value;
    }

    private void DrawUVSelectPopup(MaterialProperty prop, MaterialEditor editor)
    {
        DrawCenteredPopup(prop, editor, uvSelectOptions);
    }

    private void DrawCenteredPopup(MaterialProperty prop, MaterialEditor editor, string[] options)
    {
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect popupRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        popupRect.width = Mathf.Min(120f, popupRect.width);

        int currentValue = Mathf.Clamp(Mathf.RoundToInt(prop.floatValue), 0, options.Length - 1);
        EditorGUI.BeginChangeCheck();
        int newValue = EditorGUI.Popup(popupRect, currentValue, options, GetCenteredPopupStyle());
        if (EditorGUI.EndChangeCheck())
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            prop.floatValue = newValue;
            Material mat = editor.target as Material;
            if (mat != null) MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            editor.PropertiesChanged();
        }
    }

    private void DrawToggleButton(string propName, MaterialProperty prop, MaterialEditor editor)
    {
        ToggleTexts texts;
        if (!toggleDisplayNames.TryGetValue(propName, out texts)) texts = new ToggleTexts("是", "否");
        Rect rect = EditorGUILayout.GetControlRect(true, 18f);
        Rect buttonRect = EditorGUI.PrefixLabel(rect, new GUIContent(prop.displayName));
        buttonRect.width = Mathf.Min(100f, buttonRect.width);
        bool currentValue = prop.floatValue > 0.5f;
        string buttonText = currentValue ? texts.on : texts.off;
        if (GUI.Button(buttonRect, buttonText, EditorStyles.miniButton))
        {
            editor.RegisterPropertyChangeUndo(prop.displayName);
            prop.floatValue = currentValue ? 0f : 1f;
            Material mat = editor.target as Material;
            if (mat != null) MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            editor.PropertiesChanged();
        }
    }
}
#endif
