Shader "TapGJ/CardEffectComposite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Rendered effect", 2D) = "white" {}
        _CardOpacity ("Rendered card opacity", 2D) = "black" {}
        _ZTest ("Depth test", Float) = 4
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        ZWrite Off
        ZTest [_ZTest]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float4 local : TEXCOORD1; fixed4 color : COLOR; };
            sampler2D _MainTex, _CardOpacity;
            float4 _ClipRect;
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                half4 color = tex2D(_MainTex, i.uv);
                // 原卡面的单粒子使用 SrcAlpha 混合；还原 alpha，火星保留加法颜色。
                color.a = sqrt(saturate(tex2D(_CardOpacity, i.uv).a));
                color.rgb *= i.color.rgb * i.color.a;
                color.a *= i.color.a;
                #ifdef UNITY_UI_CLIP_RECT
                    color *= UnityGet2DClipping(i.local.xy, _ClipRect);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
