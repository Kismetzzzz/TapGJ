using System;
using UnityEngine;
using UnityEditor;


//创建一个GUI类
public class PostGUI : ShaderGUI
{
    public GUILayoutOption[] shortButtonStyle = new GUILayoutOption[] { GUILayout.Width(100) };

    public GUIStyle style = new GUIStyle() ;

    static bool Foldout(bool display, string title)
    {




        var style = new GUIStyle("ShurikenModuleTitle");
        style.font = new GUIStyle(EditorStyles.boldLabel).font;
        style.border = new RectOffset(15, 7, 4, 4);
        style.fixedHeight = 22;
        style.contentOffset = new Vector2(20f, -2f);
        style.fontSize = 11;
        style.normal.textColor = new Color(0.7f, 0.8f, 0.9f);


        var rect = GUILayoutUtility.GetRect(16f, 25f, style);
        GUI.Box(rect, title, style);

        var e = Event.current;

        var toggleRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
        if (e.type == EventType.Repaint)
        {
            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
        }

        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
        {
            display = !display;
            e.Use();
        }

        return display;
    }

    static bool Foldouts(bool display, string title)
    {



        var style = new GUIStyle("ShurikenModuleTitle");
        style.font = new GUIStyle(EditorStyles.boldLabel).font;
        style.border = new RectOffset(15, 7, 4, 4);
        style.fixedHeight = 18;
        style.contentOffset = new Vector2(30f, -2f);
        style.fontSize = 10;
        style.normal.textColor = new Color(0.75f, 0.75f, 0.75f);


        var rect = GUILayoutUtility.GetRect(16f, 15f, style);
        GUI.Box(rect, title, style);

        var e = Event.current;

        var toggleRect = new Rect(rect.x + 15f, rect.y + 2f, 13f, 13f);
        if (e.type == EventType.Repaint)
        {
            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
        }

        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
        {
            display = !display;
            e.Use();
        }

        return display;
    }


    static bool _MainColor = true;

    static bool _Base = true;

    static bool _Texxx = false;

    static bool _tips = false;

    static bool _Thanks = false;

    static bool _honglan = false;

    static bool _UVDistort = false;

    static bool _Black = false;

    static bool _Texx = false;

    static bool _Logox = false;

    static bool _Logoxx = false;
    static bool _zhenping = false;
    MaterialEditor m_MaterialEditor;


    MaterialProperty ColorStyle = null;
    MaterialProperty centerU = null;
    MaterialProperty centerV = null;
    MaterialProperty Color1 = null;
    MaterialProperty Color2 = null;
    MaterialProperty LineTilingU = null;
    MaterialProperty LineTilingV = null;
    MaterialProperty LineUVScale = null;
    MaterialProperty LineUVScaleK = null;
    MaterialProperty LineColorScale = null;
    MaterialProperty LineOffset = null;
    MaterialProperty BlurFactor = null;
    MaterialProperty BlurFactorK = null;
    MaterialProperty Soft = null;
    MaterialProperty StepFactor = null;
    MaterialProperty StepFactorK = null;
    MaterialProperty RedBlueFactor = null;
    MaterialProperty RedBlueFactorK = null;
    MaterialProperty Tex = null;
    MaterialProperty TexRotator = null;
    MaterialProperty TexAlpha = null;
    MaterialProperty VignettePower = null;
    MaterialProperty VignetteScale = null;
    MaterialProperty MainAlpha = null;
    MaterialProperty MainAlphaK = null;
    MaterialProperty IfMainAlpha = null;
    MaterialProperty IfStepFactor = null;
    MaterialProperty IfLineUVScale = null;
    MaterialProperty IfBlurFactor = null;
    MaterialProperty IfRedBlueFactor = null;
    MaterialProperty Logo = null;
    MaterialProperty LogoAR = null;
    MaterialProperty LogoAlpha = null;
    MaterialProperty zhenfu = null;
    MaterialProperty zhenfuK = null;
    MaterialProperty Ifzhenfu = null;
    MaterialProperty zhenpin = null;
    MaterialProperty zhenpinK = null;
    MaterialProperty Ifzhenpin = null;
    MaterialProperty IfVignettePower = null;
    MaterialProperty VignettePowerK = null;
    MaterialProperty IfVignetteScale = null;
    MaterialProperty VignetteScaleK = null;

    MaterialProperty TexAR = null;

    public void FindProperties(MaterialProperty[] props)
    {


        ColorStyle = FindProperty("_ColorStyle", props);
        centerU = FindProperty("_centerU", props);
        centerV = FindProperty("_centerV", props);
        Color1 = FindProperty("_Color1", props);
        Color2 = FindProperty("_Color2", props);
        LineTilingU = FindProperty("_LineTilingU", props);
        LineTilingV = FindProperty("_LineTilingV", props);
        LineUVScale = FindProperty("_LineUVScale", props);
        LineUVScaleK = FindProperty("_LineUVScaleK", props);
        IfLineUVScale = FindProperty("_IfLineUVScale", props);
        LineColorScale = FindProperty("_LineColorScale", props);
        LineOffset = FindProperty("_LineOffset", props);
        BlurFactor = FindProperty("_BlurFactor", props);
        BlurFactorK = FindProperty("_BlurFactorK", props);
        IfBlurFactor = FindProperty("_IfBlurFactor", props);
        Soft = FindProperty("_Soft", props);
        StepFactor = FindProperty("_StepFactor", props);
        StepFactorK = FindProperty("_StepFactorK", props);
        IfStepFactor = FindProperty("_IfStepFactor", props);
        RedBlueFactor = FindProperty("_RedBlueFactor", props);
        RedBlueFactorK = FindProperty("_RedBlueFactorK", props);
        IfRedBlueFactor = FindProperty("_IfRedBlueFactor", props);
        Tex = FindProperty("_Tex", props);
        TexRotator = FindProperty("_TexRotator", props);
        TexAlpha = FindProperty("_TexAlpha", props);
        VignettePower = FindProperty("_VignettePower", props);
        VignetteScale = FindProperty("_VignetteScale", props);
        MainAlpha = FindProperty("_MainAlpha", props);
        MainAlphaK = FindProperty("_MainAlphaK", props);
        IfMainAlpha = FindProperty("_IfMainAlpha", props);
        LogoAR = FindProperty("_LogoAR", props);
        Logo = FindProperty("_Logo", props);
        LogoAlpha= FindProperty("_LogoAlpha", props);
        zhenfu = FindProperty("_zhenfu", props);
        zhenfuK = FindProperty("_zhenfuK", props);
        Ifzhenfu = FindProperty("_Ifzhenfu", props);
        zhenpin = FindProperty("_zhenpin", props);
        zhenpinK = FindProperty("_zhenpinK", props);
        Ifzhenpin = FindProperty("_Ifzhenpin", props);
        IfVignettePower = FindProperty("_IfVignettePower", props);
        VignettePowerK = FindProperty("_VignettePowerK", props);
        VignetteScaleK = FindProperty("_VignetteScaleK", props);
        IfVignetteScale = FindProperty("_IfVignetteScale", props);
        TexAR = FindProperty("_TexAR", props);
    }

    //将上面定义的属性显示在面板上
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
    {
      

        FindProperties(props);

        m_MaterialEditor = materialEditor;

        Material material = materialEditor.target as Material;


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        _MainColor = Foldout(_MainColor, "颜色模式(ColorStyle)");

        if (_MainColor)
        {
            EditorGUI.indentLevel++;


            m_MaterialEditor.ShaderProperty(ColorStyle, "颜色模式");



            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                style.wordWrap = true;
                GUILayout.Label("*Normal为正常色模式，BlackWhite为黑白色模式，ReCover为反色模式", style);
                EditorGUILayout.EndVertical();
            }


            GUILayout.Space(5);



            if (material.GetFloat("_ColorStyle") == 1)
            {



                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUILayout.Space(5);

                m_MaterialEditor.ShaderProperty(Color1, "颜色1");
                m_MaterialEditor.ShaderProperty(Color2, "颜色2");
                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*两种色调，可以任意更换配色，黑白，黑红等等", style);
                    EditorGUILayout.EndVertical();
                }
                GUILayout.Space(5);

                m_MaterialEditor.ShaderProperty(Soft, "像素软硬程度");
                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*整体颜色的渐变梯度，值越大，梯度越缓，渐变越明显", style);
                    EditorGUILayout.EndVertical();
                }

                GUILayout.Space(5);









                EditorGUILayout.BeginVertical(EditorStyles.helpBox);


                EditorGUILayout.BeginHorizontal();


                EditorGUILayout.PrefixLabel("像素剔除程度K帧");
                if (material.GetFloat("_IfStepFactor") == 0)
                {
                    if (GUILayout.Button("已关闭", shortButtonStyle))
                    {
                        material.SetFloat("_IfStepFactor", 1);

                    }
                }
                else

                {
                    if (GUILayout.Button("已开启", shortButtonStyle))
                    {
                        material.SetFloat("_IfStepFactor", 0);

                    }
                }

                EditorGUILayout.EndHorizontal();



                if (material.GetFloat("_IfStepFactor") == 0) { 


                    m_MaterialEditor.ShaderProperty(StepFactor, "像素剔除程度");





                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*整体颜色的剔除最小值，小于该值都会被剔除成为黑色", style);
                    EditorGUILayout.EndVertical();
                }
                GUILayout.Space(5);



                }


                EditorGUILayout.EndVertical();








                m_MaterialEditor.ShaderProperty(LineColorScale, "附加线段强度");
                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*增加一些额外的风格化线条，增加冲击感，线条的密度和放射中心同径向模糊一致，可以在径向模糊里设置", style);
                    EditorGUILayout.EndVertical();
                }
                GUILayout.Space(5);
                EditorGUILayout.EndVertical();


            }






            EditorGUILayout.BeginVertical(EditorStyles.helpBox);



            EditorGUILayout.BeginHorizontal();


            EditorGUILayout.PrefixLabel("总透明度K帧");
            if (material.GetFloat("_IfMainAlpha") == 0)
            {
                if (GUILayout.Button("已关闭", shortButtonStyle))
                {
                    material.SetFloat("_IfMainAlpha", 1);

                }
            }
            else

            {
                if (GUILayout.Button("已开启", shortButtonStyle))
                {
                    material.SetFloat("_IfMainAlpha", 0);

                }
            }

            EditorGUILayout.EndHorizontal();

            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*想要对整体AlphaK帧做动画，需要开启此选项，开启后下方总透明度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的MainAplha控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
                EditorGUILayout.EndVertical();
            }








            if (material.GetFloat("_IfMainAlpha") == 0)
            {

                m_MaterialEditor.ShaderProperty(MainAlpha, "总透明度");
                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*黑白闪，反色，径向模糊，红蓝偏移的混合效果的整体透明度", style);
                    EditorGUILayout.EndVertical();
                }
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);



            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _UVDistort = Foldout(_UVDistort, "径向模糊(RadialBlur)");

        if (_UVDistort)
        {
            EditorGUI.indentLevel++;


            UVDistort(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _honglan = Foldout(_honglan, "红蓝色散(Chromatic)");

        if (_honglan)
        {
            EditorGUI.indentLevel++;


            honglangui(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();




        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _zhenping = Foldout(_zhenping, "震屏(ScreenShake)");

        if (_zhenping)
        {
            EditorGUI.indentLevel++;


            zhenpinggui(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();






        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        _Black = Foldout(_Black, "黑边框(Vignette)");

        if (_Black)
        {
            EditorGUI.indentLevel++;


            Black(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _Texx = Foldout(_Texx, "肌理图(Texture)");

        if (_Texx)
        {
            EditorGUI.indentLevel++;


            Textures(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _Logox = Foldout(_Logox, "水印(Logo)");

        if (_Logox)
        {
            EditorGUI.indentLevel++;


            Logogui(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        _Base = Foldout(_Base, "后期综合设置(PostComprehensiveSettings)");

        if (_Base)
        {
            EditorGUI.indentLevel++;


            Base(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _Thanks = Foldout(_Thanks, "说明(Illustrate)");

        if (_Thanks)
        {
            EditorGUI.indentLevel++;


            Thanks(material);

            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();











    }



    void UVDistort(Material material)

        {


        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        m_MaterialEditor.ShaderProperty(centerU, "中心点U");


        m_MaterialEditor.ShaderProperty(centerV, "中心点V");
        EditorGUILayout.EndVertical();
        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*径向中心点，同时控制UV拉伸，红蓝偏移，径向模糊，附加线条的中心位置", style);
            EditorGUILayout.EndVertical();
        }
        GUILayout.Space(5);

      



        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("径向模糊强度K帧");
        if (material.GetFloat("_IfBlurFactor") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_IfBlurFactor", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_IfBlurFactor", 0);

            }
          
        }

        EditorGUILayout.EndHorizontal();
        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对径向模糊强度K帧做动画，需要开启此选项，开启后下方径向模糊强度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的BlurFactor控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }

        if (material.GetFloat("_IfBlurFactor") == 0)
        { 

            m_MaterialEditor.ShaderProperty(BlurFactor, "径向模糊强度");

        }
        EditorGUILayout.EndVertical();



        GUILayout.Space(5);

   



        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("UV拉伸强度K帧");
        if (material.GetFloat("_IfLineUVScale") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_IfLineUVScale", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_IfLineUVScale", 0);

            }

        }

        EditorGUILayout.EndHorizontal();

        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对UV拉伸强度K帧做动画，需要开启此选项，开启后下方UV拉伸强度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的LineUVScale控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }


        if (material.GetFloat("_IfLineUVScale") == 0)
        {
            m_MaterialEditor.ShaderProperty(LineUVScale, "UV拉伸强度");
            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*UV放射状拉伸程度", style);
                EditorGUILayout.EndVertical();
            }
        }

        EditorGUILayout.EndVertical();
          GUILayout.Space(5);





        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        m_MaterialEditor.ShaderProperty(LineTilingU, "线段纵向重铺");

        m_MaterialEditor.ShaderProperty(LineTilingV, "线段横向重铺");
        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*放射线段的重铺度，纵向为从中心往边缘方向的重铺度，横向为环形方向重铺度", style);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndVertical();


        GUILayout.Space(5);









        m_MaterialEditor.ShaderProperty(LineOffset, "线段偏移");
        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*放射线段的偏移值，可以调整来选一个漂亮的线段状态", style);
            EditorGUILayout.EndVertical();
        }

        GUILayout.Space(5);





   







        }


    void honglangui(Material material)
    {

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("色散强度K帧");
        if (material.GetFloat("_IfRedBlueFactor") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_IfRedBlueFactor", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_IfRedBlueFactor", 0);

            }

        }

        EditorGUILayout.EndHorizontal();


        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对色散强度K帧做动画，需要开启此选项，开启后下方色散强度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的Chromatic控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }

        if (material.GetFloat("_IfRedBlueFactor") == 0)
        {

            m_MaterialEditor.ShaderProperty(RedBlueFactor, "色散强度");
            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*放射状红蓝偏移程度", style);
                EditorGUILayout.EndVertical();
            }
        }

        EditorGUILayout.EndVertical();



        GUILayout.Space(5);

    }
    void zhenpinggui(Material material)
    {

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);



        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("振频K帧");
        if (material.GetFloat("_Ifzhenpin") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_Ifzhenpin", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_Ifzhenpin", 0);

            }
        }

        EditorGUILayout.EndHorizontal();
        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对振频K帧做动画，需要开启此选项，开启后下方振频数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的Frequency控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }


        if (material.GetFloat("_Ifzhenpin") == 0)
        {

            m_MaterialEditor.ShaderProperty(zhenpin, "振频");
            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*屏幕震动的频率，值越大震动的频率越大", style);
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(5);





        EditorGUILayout.BeginVertical(EditorStyles.helpBox);



        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("振幅K帧");
        if (material.GetFloat("_Ifzhenfu") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_Ifzhenfu", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_Ifzhenfu", 0);

            }
        }

        EditorGUILayout.EndHorizontal();

        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对振幅K帧做动画，需要开启此选项，开启后下方振幅数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的Amplitude控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }

        if (material.GetFloat("_Ifzhenfu") == 0)
        {

            m_MaterialEditor.ShaderProperty(zhenfu, "振幅");
            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*屏幕震动的幅度，值越大震动的幅度越大", style);
                EditorGUILayout.EndVertical();
            }
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(5);

    }



    void Black(Material material)

        {
           

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);



        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("黑边框宽度K帧");
        if (material.GetFloat("_IfVignettePower") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_IfVignettePower", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_IfVignettePower", 0);

            }
        }

        EditorGUILayout.EndHorizontal();

        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对黑边框宽度K帧做动画，需要开启此选项，开启后下方黑边框宽度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的VignettePower控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }

        if (material.GetFloat("_IfVignettePower") == 0)
        {

            m_MaterialEditor.ShaderProperty(VignettePower, "黑边框宽度");
    
        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(5);



        EditorGUILayout.BeginVertical(EditorStyles.helpBox);



        EditorGUILayout.BeginHorizontal();


        EditorGUILayout.PrefixLabel("黑边框强度K帧");
        if (material.GetFloat("_IfVignetteScale") == 0)
        {
            if (GUILayout.Button("已关闭", shortButtonStyle))
            {
                material.SetFloat("_IfVignetteScale", 1);

            }
        }
        else

        {
            if (GUILayout.Button("已开启", shortButtonStyle))
            {
                material.SetFloat("_IfVignetteScale", 0);

            }
        }

        EditorGUILayout.EndHorizontal();

        if (_tips == true)
        {

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            style.fontSize = 10;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("*想要对黑边框强度K帧做动画，需要开启此选项，开启后下方黑边框强度数值将失效并隐藏，此数值将由上方脚本PandaPostProcess内的VignetteScale控制，K帧也是需要K脚本里的数值，材质球上的参数不支持K帧", style);
            EditorGUILayout.EndVertical();
        }

        if (material.GetFloat("_IfVignetteScale") == 0)
        {

            m_MaterialEditor.ShaderProperty(VignetteScale, "黑边框强度");

        }
        EditorGUILayout.EndVertical();
        GUILayout.Space(5);




    }

        void Textures(Material material)

        {
          
            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("贴图"), Tex);
            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*可以用来制作屏幕肌理", style);
                EditorGUILayout.EndVertical();
            }


            if (Tex.textureValue != null)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _Texxx = Foldouts(_Texxx, "贴图设置");

                if (_Texxx)
                {
                    EditorGUI.indentLevel++;


                m_MaterialEditor.ShaderProperty(TexAR, "使用R通道");
                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*勾选后使用R通道为数据通道，不勾选使用A通道为数据通道，图片有A通道不要勾选！", style);
                    EditorGUILayout.EndVertical();
                }

                m_MaterialEditor.ShaderProperty(TexRotator, "贴图旋转");
                    if (_tips == true)
                    {

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        style.fontSize = 10;
                        style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                        GUILayout.Label("*贴图旋转，处理贴图朝向，省去复制贴图改变朝向", style);
                        EditorGUILayout.EndVertical();
                    }





                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*展开后可以对贴图属性做一些设置，包含通道选择，旋转", style);
                    EditorGUILayout.EndVertical();
                }

                m_MaterialEditor.TextureScaleOffsetProperty(Tex);

            
                GUILayout.Space(5);

                m_MaterialEditor.ShaderProperty(TexAlpha, "贴图透明度");
                GUILayout.Space(5);

            }

        if (Tex.textureValue == null)
        {
            material.SetFloat("_TexAlpha", 0);

        }


    }


        void Logogui(Material material)
        {

            m_MaterialEditor.TexturePropertySingleLine(new GUIContent("贴图"), Logo);

            if (Logo.textureValue != null)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                _Logoxx = Foldouts(_Logoxx, "贴图设置");

                if (_Logoxx)
                {
                    EditorGUI.indentLevel++;




                    m_MaterialEditor.ShaderProperty(LogoAR, "使用R通道");
                    if (_tips == true)
                    {

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        style.fontSize = 10;
                        style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                        GUILayout.Label("*勾选使用R通道作为透明通道通道，不勾选使用A通道作为透明通道", style);
                        EditorGUILayout.EndVertical();
                    }





                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();

                if (_tips == true)
                {

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    style.fontSize = 10;
                    style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                    GUILayout.Label("*展开后可以对贴图属性做一些设置，包含通道选择", style);
                    EditorGUILayout.EndVertical();
                }

         


                m_MaterialEditor.TextureScaleOffsetProperty(Logo);
                GUILayout.Space(5);
                m_MaterialEditor.ShaderProperty(LogoAlpha, "贴图透明度");
                GUILayout.Space(5);
            }



        if (Logo.textureValue == null)
        {
            material.SetFloat("_LogoAlpha", 0);

        }


    }


        void Base(Material material)

        {
    
           
          





          







            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();


            EditorGUILayout.PrefixLabel("开启初学者模式");
            if (_tips == false)
            {
                if (GUILayout.Button("已关闭", shortButtonStyle))
                {
                    _tips = true;

                }
            }
            else

            {
                if (GUILayout.Button("已开启", shortButtonStyle))
                {
                    _tips = false;

                }
            }

            EditorGUILayout.EndHorizontal();

            if (_tips == true)
            {

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                style.fontSize = 10;
                style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
                GUILayout.Label("*开启后会显示每一个变量的详细功能信息，适合新使用材质的初学者", style);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }


        void Thanks(Material material)

        {

            style.fontSize = 12;
            style.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            style.wordWrap = true;


            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
           
            GUILayout.Label("MainAlpha对应总透明度", style);
        GUILayout.Space(5);
        GUILayout.Label("BlurFactor对应径向模糊强度", style);
        GUILayout.Space(5);
        GUILayout.Label("LineUVScale对应UV拉伸强度", style);
        GUILayout.Space(5);
        GUILayout.Label("Chromatic对应色散强度", style);
        GUILayout.Space(5);
        GUILayout.Label("Frequency对应振频", style);
        GUILayout.Space(5);
        GUILayout.Label("Amplitude对应振幅", style);
        GUILayout.Space(5);
        GUILayout.Label("VignettePower对应黑角宽度", style);
        GUILayout.Space(5);
        GUILayout.Label("VignetteScale对应黑角强度", style);
        GUILayout.Space(5);
        EditorGUILayout.EndVertical();
        GUILayout.Label(" 本材质由油腻联盟坏熊猫制作，欢迎使用，特别感谢油腻联盟帮助测试", style);
        GUILayout.Space(5);
        }

    }

