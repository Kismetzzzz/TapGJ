// Made with Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "VFX/PandaPostV1.0"
{
	Properties
	{
		_MainTex ( "Screen", 2D ) = "black" {}
		_centerU("centerU", Range( 0 , 1)) = 0.5
		_centerV("centerV", Range( 0 , 1)) = 0.5
		[HDR]_Color1("Color1", Color) = (1,1,1,0)
		[HDR]_Color2("Color2", Color) = (0,0,0,0)
		_LineTilingU("LineTilingU", Range( 0 , 5)) = 2
		_LineTilingV("LineTilingV", Range( 1 , 20)) = 8
		_LineUVScale("LineUVScale", Range( 0 , 4)) = 0
		_LineUVScaleK("LineUVScaleK", Float) = 0
		_LineColorScale("LineColorScale", Range( -1 , 3)) = 0
		_BlurFactor("BlurFactor", Range( 0 , 1)) = 0
		_BlurFactorK("BlurFactorK", Float) = 0
		_Soft("Soft", Range( 0 , 1)) = 0.5
		_StepFactor("StepFactor", Range( 0 , 2)) = 0.6
		_StepFactorK("StepFactorK", Float) = 0
		_Logo("Logo", 2D) = "white" {}
		_Tex("Tex", 2D) = "white" {}
		_TexRotator("TexRotator", Range( 0 , 1)) = 0.075
		_TexAlpha("TexAlpha", Range( 0 , 1)) = 0.07
		_VignettePowerK("VignettePowerK", Float) = 1.5
		_VignettePower("VignettePower", Range( 1 , 3)) = 1.5
		_VignetteScale("VignetteScale", Range( 0 , 3)) = 1.5
		_VignetteScaleK("VignetteScaleK", Float) = 1.5
		_MainAlpha("MainAlpha", Range( 0 , 1)) = 1
		_MainAlphaK("MainAlphaK", Float) = 1
		[Toggle]_IfMainAlpha("IfMainAlpha", Float) = 0
		[Toggle]_IfStepFactor("IfStepFactor", Float) = 0
		[Toggle]_IfLineUVScale("IfLineUVScale", Float) = 0
		[Toggle]_IfBlurFactor("IfBlurFactor", Float) = 0
		_LogoAlpha("LogoAlpha", Range( 0 , 1)) = 0.2
		[HideInInspector]_Logo_ST("Logo_ST", Vector) = (3,3,0,0.1)
		[Toggle]_LogoAR("LogoAR", Float) = 0
		_LineOffset("LineOffset", Range( 0 , 5)) = 0
		_RedBlueFactorK("RedBlueFactorK", Float) = 0
		_RedBlueFactor("RedBlueFactor", Range( 0 , 1.5)) = 0
		[KeywordEnum(Normal,BlackWhiteFlash,ColorReverse)] _ColorStyle("ColorStyle", Float) = 0
		_zhenfuK("zhenfuK", Float) = 0
		_zhenfu("zhenfu", Range( 0 , 1)) = 0
		[Toggle]_IfRedBlueFactor("IfRedBlueFactor", Float) = 0
		_zhenpinK("zhenpinK", Float) = 0
		_zhenpin("zhenpin", Range( 0 , 1)) = 0
		[Toggle]_Ifzhenpin("Ifzhenpin", Float) = 0
		[Toggle]_Ifzhenfu("Ifzhenfu", Float) = 0
		[HideInInspector]_Tex_ST("Tex_ST", Vector) = (300,300,0,0)
		[Toggle]_IfVignetteScale("IfVignetteScale", Float) = 0
		[Toggle]_IfVignettePower("IfVignettePower", Float) = 0
		[Toggle]_TexAR("TexAR", Float) = 1
		[HideInInspector] _texcoord( "", 2D ) = "white" {}

	}

	SubShader
	{
		LOD 0

		
		
		ZTest Always
		Cull Off
		ZWrite Off

		
		Pass
		{ 
			CGPROGRAM 

			

			#pragma vertex vert_img_custom 
			#pragma fragment frag
			#pragma target 5.0
			#include "UnityCG.cginc"
			#include "UnityShaderVariables.cginc"
			#pragma multi_compile_local _COLORSTYLE_NORMAL _COLORSTYLE_BLACKWHITEFLASH _COLORSTYLE_COLORREVERSE


			struct appdata_img_custom
			{
				float4 vertex : POSITION;
				half2 texcoord : TEXCOORD0;
				
			};

			struct v2f_img_custom
			{
				float4 pos : SV_POSITION;
				half2 uv   : TEXCOORD0;
				half2 stereoUV : TEXCOORD2;
		#if UNITY_UV_STARTS_AT_TOP
				half4 uv2 : TEXCOORD1;
				half4 stereoUV2 : TEXCOORD3;
		#endif
				float4 ase_texcoord4 : TEXCOORD4;
			};

			uniform sampler2D _MainTex;
			uniform half4 _MainTex_TexelSize;
			uniform half4 _MainTex_ST;
			
			uniform float _IfVignettePower;
			uniform float _VignettePower;
			uniform float _VignettePowerK;
			uniform float _IfVignetteScale;
			uniform float _VignetteScale;
			uniform float _VignetteScaleK;
			uniform float _centerU;
			uniform float _centerV;
			uniform float _Ifzhenpin;
			uniform float _zhenpin;
			uniform float _zhenpinK;
			uniform float _Ifzhenfu;
			uniform float _zhenfu;
			uniform float _zhenfuK;
			uniform float _LineTilingU;
			uniform float _LineTilingV;
			uniform float _LineOffset;
			uniform float _IfLineUVScale;
			uniform float _LineUVScale;
			uniform float _LineUVScaleK;
			uniform float _IfRedBlueFactor;
			uniform float _RedBlueFactor;
			uniform float _RedBlueFactorK;
			uniform float _IfBlurFactor;
			uniform float _BlurFactor;
			uniform float _BlurFactorK;
			uniform float4 _Color1;
			uniform float4 _Color2;
			uniform float _IfStepFactor;
			uniform float _StepFactor;
			uniform float _StepFactorK;
			uniform float _Soft;
			uniform float _LineColorScale;
			uniform float _IfMainAlpha;
			uniform float _MainAlpha;
			uniform float _MainAlphaK;
			uniform float _TexAR;
			uniform sampler2D _Tex;
			uniform float4 _Tex_ST;
			uniform float _TexRotator;
			uniform float _TexAlpha;
			uniform float _LogoAR;
			uniform sampler2D _Logo;
			uniform float4 _Logo_ST;
			uniform float _LogoAlpha;
					float2 voronoihash531( float2 p )
					{
						
						p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
						return frac( sin( p ) *43758.5453);
					}
			
					float voronoi531( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
					{
						float2 n = floor( v );
						float2 f = frac( v );
						float F1 = 8.0;
						float F2 = 8.0; float2 mg = 0;
						for ( int j = -1; j <= 1; j++ )
						{
							for ( int i = -1; i <= 1; i++ )
						 	{
						 		float2 g = float2( i, j );
						 		float2 o = voronoihash531( n + g );
								o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
								float d = 0.5 * dot( r, r );
						 		if( d<F1 ) {
						 			F2 = F1;
						 			F1 = d; mg = g; mr = r; id = o;
						 		} else if( d<F2 ) {
						 			F2 = d;
						
						 		}
						 	}
						}
						return F1;
					}
			
					float2 voronoihash532( float2 p )
					{
						
						p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
						return frac( sin( p ) *43758.5453);
					}
			
					float voronoi532( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
					{
						float2 n = floor( v );
						float2 f = frac( v );
						float F1 = 8.0;
						float F2 = 8.0; float2 mg = 0;
						for ( int j = -1; j <= 1; j++ )
						{
							for ( int i = -1; i <= 1; i++ )
						 	{
						 		float2 g = float2( i, j );
						 		float2 o = voronoihash532( n + g );
								o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
								float d = 0.5 * dot( r, r );
						 		if( d<F1 ) {
						 			F2 = F1;
						 			F1 = d; mg = g; mr = r; id = o;
						 		} else if( d<F2 ) {
						 			F2 = d;
						
						 		}
						 	}
						}
						return F1;
					}
			
					float2 voronoihash654( float2 p )
					{
						
						p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
						return frac( sin( p ) *43758.5453);
					}
			
					float voronoi654( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
					{
						float2 n = floor( v );
						float2 f = frac( v );
						float F1 = 8.0;
						float F2 = 8.0; float2 mg = 0;
						for ( int j = -1; j <= 1; j++ )
						{
							for ( int i = -1; i <= 1; i++ )
						 	{
						 		float2 g = float2( i, j );
						 		float2 o = voronoihash654( n + g );
								o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
								float d = 0.5 * dot( r, r );
						 		if( d<F1 ) {
						 			F2 = F1;
						 			F1 = d; mg = g; mr = r; id = o;
						 		} else if( d<F2 ) {
						 			F2 = d;
						
						 		}
						 	}
						}
						return F1;
					}
			
					float2 voronoihash655( float2 p )
					{
						
						p = float2( dot( p, float2( 127.1, 311.7 ) ), dot( p, float2( 269.5, 183.3 ) ) );
						return frac( sin( p ) *43758.5453);
					}
			
					float voronoi655( float2 v, float time, inout float2 id, inout float2 mr, float smoothness, inout float2 smoothId )
					{
						float2 n = floor( v );
						float2 f = frac( v );
						float F1 = 8.0;
						float F2 = 8.0; float2 mg = 0;
						for ( int j = -1; j <= 1; j++ )
						{
							for ( int i = -1; i <= 1; i++ )
						 	{
						 		float2 g = float2( i, j );
						 		float2 o = voronoihash655( n + g );
								o = ( sin( time + o * 6.2831 ) * 0.5 + 0.5 ); float2 r = f - g - o;
								float d = 0.5 * dot( r, r );
						 		if( d<F1 ) {
						 			F2 = F1;
						 			F1 = d; mg = g; mr = r; id = o;
						 		} else if( d<F2 ) {
						 			F2 = d;
						
						 		}
						 	}
						}
						return F1;
					}
			


			v2f_img_custom vert_img_custom ( appdata_img_custom v  )
			{
				v2f_img_custom o;
				float4 ase_clipPos = UnityObjectToClipPos(v.vertex);
				float4 screenPos = ComputeScreenPos(ase_clipPos);
				o.ase_texcoord4 = screenPos;
				
				o.pos = UnityObjectToClipPos( v.vertex );
				o.uv = float4( v.texcoord.xy, 1, 1 );

				#if UNITY_UV_STARTS_AT_TOP
					o.uv2 = float4( v.texcoord.xy, 1, 1 );
					o.stereoUV2 = UnityStereoScreenSpaceUVAdjust ( o.uv2, _MainTex_ST );

					if ( _MainTex_TexelSize.y < 0.0 )
						o.uv.y = 1.0 - o.uv.y;
				#endif
				o.stereoUV = UnityStereoScreenSpaceUVAdjust ( o.uv, _MainTex_ST );
				return o;
			}

			half4 frag ( v2f_img_custom i ) : SV_Target
			{
				#ifdef UNITY_UV_STARTS_AT_TOP
					half2 uv = i.uv2;
					half2 stereoUV = i.stereoUV2;
				#else
					half2 uv = i.uv;
					half2 stereoUV = i.stereoUV;
				#endif	
				
				half4 finalColor;

				// ase common template code
				float2 texCoord326 = i.uv.xy * float2( 1,1 ) + float2( 0,0 );
				float sphere361 = length( ( texCoord326 + float2( -0.5,-0.5 ) ) );
				float Vignette340 = ( 1.0 - saturate( ( pow( sphere361 , ( _IfVignettePower == 0.0 ? _VignettePower : _VignettePowerK ) ) * ( _IfVignetteScale == 0.0 ? _VignetteScale : _VignetteScaleK ) ) ) );
				float2 uv_MainTex = i.uv.xy * _MainTex_ST.xy + _MainTex_ST.zw;
				float localMyCustomExpression5 = ( 0.0 );
				float4 screenPos = i.ase_texcoord4;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = ( UNITY_NEAR_CLIP_VALUE >= 0 ) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float temp_output_277_0 = ( 1.0 - 1.0 );
				float2 appendResult241 = (float2(temp_output_277_0 , temp_output_277_0));
				float2 appendResult17 = (float2(_centerU , _centerV));
				float2 temp_output_116_0 = ( 1.0 - appendResult17 );
				float temp_output_861_0 = (0.0 + (( _Ifzhenpin == 0.0 ? _zhenpin : _zhenpinK ) - 0.0) * (60.0 - 0.0) / (1.0 - 0.0));
				float mulTime789 = _Time.y * temp_output_861_0;
				float temp_output_833_0 = (0.0 + (( _Ifzhenfu == 0.0 ? _zhenfu : _zhenfuK ) - 0.0) * (0.05 - 0.0) / (1.0 - 0.0));
				float mulTime793 = _Time.y * ( temp_output_861_0 * 0.7 );
				float2 appendResult794 = (float2(( cos( mulTime789 ) * temp_output_833_0 ) , ( sin( mulTime793 ) * temp_output_833_0 )));
				float2 center279 = ( temp_output_116_0 + appendResult794 );
				float time531 = 0.2;
				float2 voronoiSmoothId531 = 0;
				float2 texCoord253 = i.uv.xy * float2( 1,1 ) + float2( 0,0 );
				float2 CenteredUV15_g14 = ( texCoord253 - center279 );
				float2 appendResult308 = (float2(_LineTilingU , _LineTilingV));
				float2 break291 = ( float2( 2,50 ) * appendResult308 );
				float2 break17_g14 = CenteredUV15_g14;
				float2 appendResult23_g14 = (float2(( length( CenteredUV15_g14 ) * break291.x * 2.0 ) , ( atan2( break17_g14.x , break17_g14.y ) * ( 1.0 / 6.28318548202515 ) * break291.y )));
				float2 appendResult537 = (float2(_LineOffset , 0.0));
				float2 temp_output_534_0 = ( appendResult23_g14 + appendResult537 );
				float2 coords531 = temp_output_534_0 * 0.7;
				float2 id531 = 0;
				float2 uv531 = 0;
				float voroi531 = voronoi531( coords531, time531, id531, uv531, 0, voronoiSmoothId531 );
				float time532 = 0.0;
				float2 voronoiSmoothId532 = 0;
				float2 CenteredUV15_g15 = ( texCoord253 - center279 );
				float2 break294 = ( appendResult308 * float2( 1,100 ) );
				float2 break17_g15 = CenteredUV15_g15;
				float2 appendResult23_g15 = (float2(( length( CenteredUV15_g15 ) * break294.x * 2.0 ) , ( atan2( break17_g15.x , break17_g15.y ) * ( 1.0 / 6.28318548202515 ) * break294.y )));
				float2 temp_output_539_0 = ( appendResult23_g15 + appendResult537 );
				float2 coords532 = temp_output_539_0 * 1.1;
				float2 id532 = 0;
				float2 uv532 = 0;
				float voroi532 = voronoi532( coords532, time532, id532, uv532, 0, voronoiSmoothId532 );
				float4 lerpResult250 = lerp( ase_screenPosNorm , ( ( ase_screenPosNorm * float4( appendResult241, 0.0 , 0.0 ) ) - float4( ( ( appendResult241 - ( center279 * float2( 2,2 ) ) ) / float2( 2,2 ) ), 0.0 , 0.0 ) ) , ( pow( ( 1.0 - sphere361 ) , 3.0 ) * ( ( voroi531 * voroi532 ) * ( _IfLineUVScale == 0.0 ? _LineUVScale : _LineUVScaleK ) ) ));
				float temp_output_745_0 = ( 1.0 + 0.0 );
				float2 appendResult747 = (float2(temp_output_745_0 , temp_output_745_0));
				float2 temp_output_775_0 = ( float2( 0.5,0.5 ) + appendResult794 );
				float4 UV283 = ( ( lerpResult250 * float4( appendResult747, 0.0 , 0.0 ) ) - float4( ( ( appendResult747 - ( ( 1.0 - temp_output_775_0 ) * float2( 2,2 ) ) ) / float2( 2,2 ) ), 0.0 , 0.0 ) );
				float2 uv5 = UV283.xy;
				float temp_output_834_0 = (0.0 + (( _IfRedBlueFactor == 0.0 ? _RedBlueFactor : _RedBlueFactorK ) - 0.0) * (0.1 - 0.0) / (1.0 - 0.0));
				float temp_output_567_0 = ( 1.0 + temp_output_834_0 );
				float2 appendResult547 = (float2(temp_output_567_0 , temp_output_567_0));
				float2 temp_output_553_0 = ( 1.0 - temp_output_775_0 );
				float2 _Vector0 = float2(2,2);
				float2 _Vector2 = float2(2,2);
				float4 UVR554 = ( ( lerpResult250 * float4( appendResult547, 0.0 , 0.0 ) ) - float4( ( ( appendResult547 - ( temp_output_553_0 * _Vector0 ) ) / _Vector2 ), 0.0 , 0.0 ) );
				float2 uvR5 = UVR554.xy;
				float temp_output_569_0 = ( 1.0 - temp_output_834_0 );
				float2 appendResult556 = (float2(temp_output_569_0 , temp_output_569_0));
				float4 UVB566 = ( ( lerpResult250 * float4( appendResult556, 0.0 , 0.0 ) ) - float4( ( ( appendResult556 - ( temp_output_553_0 * _Vector0 ) ) / _Vector2 ), 0.0 , 0.0 ) );
				float2 uvB5 = UVB566.xy;
				float temp_output_687_0 = ( temp_output_834_0 * 0.5 );
				float temp_output_668_0 = ( 1.0 + temp_output_687_0 );
				float2 appendResult674 = (float2(temp_output_668_0 , temp_output_668_0));
				float2 temp_output_671_0 = ( 1.0 - temp_output_775_0 );
				float2 _Vector3 = float2(2,2);
				float2 _Vector4 = float2(2,2);
				float4 UVR1686 = ( ( lerpResult250 * float4( appendResult674, 0.0 , 0.0 ) ) - float4( ( ( appendResult674 - ( temp_output_671_0 * _Vector3 ) ) / _Vector4 ), 0.0 , 0.0 ) );
				float2 uvR15 = UVR1686.xy;
				float temp_output_670_0 = ( 1.0 - temp_output_687_0 );
				float2 appendResult675 = (float2(temp_output_670_0 , temp_output_670_0));
				float4 UVB1682 = ( ( lerpResult250 * float4( appendResult675, 0.0 , 0.0 ) ) - float4( ( ( appendResult675 - ( temp_output_671_0 * _Vector3 ) ) / _Vector4 ), 0.0 , 0.0 ) );
				float2 uvB15 = UVB1682.xy;
				float3 fincolor5 = float3( 0,0,0 );
				float3 fincolorR5 = float3( 0,0,0 );
				float3 fincolorB5 = float3( 0,0,0 );
				float3 fincolorR15 = float3( 0,0,0 );
				float3 fincolorB15 = float3( 0,0,0 );
				int count5 = 30;
				sampler2D maintex5 = _MainTex;
				float blurfactor5 = ( (0.0 + (( _IfBlurFactor == 0.0 ? _BlurFactor : _BlurFactorK ) - 0.0) * (0.06 - 0.0) / (1.0 - 0.0)) * 0.01 );
				float2 blurcenter5 = temp_output_116_0;
				{
				float2 dir = uv5-blurcenter5;
				 for(int i =0;i<count5; i++)
				{
				uv5=uv5-blurfactor5*dir*i;
				fincolor5+=tex2D(maintex5,uv5);
				uvR5=uvR5-blurfactor5*dir*i;
				fincolorR5+=tex2D(maintex5,uvR5);
				uvB5=uvB5-blurfactor5*dir*i;
				fincolorB5+=tex2D(maintex5,uvB5);
				uvR15=uvR15-blurfactor5*dir*i;
				fincolorR15+=tex2D(maintex5,uvR15);
				uvB15=uvB15-blurfactor5*dir*i;
				fincolorB15+=tex2D(maintex5,uvB15);
				}
				fincolor5/=count5;
				fincolorR5/=count5;
				fincolorB5/=count5;
				fincolorR15/=count5;
				fincolorB15/=count5;
				}
				float3 blurR572 = fincolorR5;
				float temp_output_383_0 = ( _IfStepFactor == 0.0 ? _StepFactor : _StepFactorK );
				float3 temp_cast_19 = (temp_output_383_0).xxx;
				float temp_output_93_0 = ( temp_output_383_0 - _Soft );
				float3 temp_cast_20 = (temp_output_93_0).xxx;
				float time654 = 0.3;
				float2 voronoiSmoothId654 = 0;
				float2 coords654 = temp_output_534_0 * 0.5;
				float2 id654 = 0;
				float2 uv654 = 0;
				float voroi654 = voronoi654( coords654, time654, id654, uv654, 0, voronoiSmoothId654 );
				float time655 = 0.1;
				float2 voronoiSmoothId655 = 0;
				float2 coords655 = temp_output_539_0 * 1.0;
				float2 id655 = 0;
				float2 uv655 = 0;
				float voroi655 = voronoi655( coords655, time655, id655, uv655, 0, voronoiSmoothId655 );
				float linecolor299 = ( ( voroi654 * voroi655 ) * pow( sphere361 , 0.1 ) );
				float temp_output_310_0 = ( _LineColorScale * linecolor299 );
				float3 desaturateInitialColor584 = saturate( ( temp_output_310_0 + blurR572 ) );
				float desaturateDot584 = dot( desaturateInitialColor584, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar584 = lerp( desaturateInitialColor584, desaturateDot584.xxx, 1.0 );
				float3 smoothstepResult583 = smoothstep( temp_cast_19 , temp_cast_20 , desaturateVar584);
				float4 lerpResult587 = lerp( _Color1 , _Color2 , float4( smoothstepResult583 , 0.0 ));
				#if defined(_COLORSTYLE_NORMAL)
				float4 staticSwitch638 = float4( blurR572 , 0.0 );
				#elif defined(_COLORSTYLE_BLACKWHITEFLASH)
				float4 staticSwitch638 = saturate( lerpResult587 );
				#elif defined(_COLORSTYLE_COLORREVERSE)
				float4 staticSwitch638 = float4( ( 1.0 - saturate( blurR572 ) ) , 0.0 );
				#else
				float4 staticSwitch638 = float4( blurR572 , 0.0 );
				#endif
				float3 blur352 = fincolor5;
				float3 temp_cast_25 = (temp_output_383_0).xxx;
				float3 temp_cast_26 = (temp_output_93_0).xxx;
				float3 desaturateInitialColor198 = saturate( ( temp_output_310_0 + blur352 ) );
				float desaturateDot198 = dot( desaturateInitialColor198, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar198 = lerp( desaturateInitialColor198, desaturateDot198.xxx, 1.0 );
				float3 smoothstepResult91 = smoothstep( temp_cast_25 , temp_cast_26 , desaturateVar198);
				float4 lerpResult103 = lerp( _Color1 , _Color2 , float4( smoothstepResult91 , 0.0 ));
				#if defined(_COLORSTYLE_NORMAL)
				float4 staticSwitch637 = float4( blur352 , 0.0 );
				#elif defined(_COLORSTYLE_BLACKWHITEFLASH)
				float4 staticSwitch637 = saturate( lerpResult103 );
				#elif defined(_COLORSTYLE_COLORREVERSE)
				float4 staticSwitch637 = float4( ( 1.0 - saturate( blur352 ) ) , 0.0 );
				#else
				float4 staticSwitch637 = float4( blur352 , 0.0 );
				#endif
				float3 blurB574 = fincolorB5;
				float3 temp_cast_31 = (temp_output_383_0).xxx;
				float3 temp_cast_32 = (temp_output_93_0).xxx;
				float3 desaturateInitialColor589 = saturate( ( temp_output_310_0 + blurB574 ) );
				float desaturateDot589 = dot( desaturateInitialColor589, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar589 = lerp( desaturateInitialColor589, desaturateDot589.xxx, 1.0 );
				float3 smoothstepResult588 = smoothstep( temp_cast_31 , temp_cast_32 , desaturateVar589);
				float4 lerpResult592 = lerp( _Color1 , _Color2 , float4( smoothstepResult588 , 0.0 ));
				#if defined(_COLORSTYLE_NORMAL)
				float4 staticSwitch639 = float4( blurB574 , 0.0 );
				#elif defined(_COLORSTYLE_BLACKWHITEFLASH)
				float4 staticSwitch639 = saturate( lerpResult592 );
				#elif defined(_COLORSTYLE_COLORREVERSE)
				float4 staticSwitch639 = float4( ( 1.0 - saturate( blurB574 ) ) , 0.0 );
				#else
				float4 staticSwitch639 = float4( blurB574 , 0.0 );
				#endif
				float3 appendResult596 = (float3(staticSwitch638.r , staticSwitch637.g , staticSwitch639.b));
				float3 blurR1690 = fincolorR15;
				float3 temp_cast_37 = (temp_output_383_0).xxx;
				float3 temp_cast_38 = (temp_output_93_0).xxx;
				float3 desaturateInitialColor704 = saturate( ( temp_output_310_0 + blurR1690 ) );
				float desaturateDot704 = dot( desaturateInitialColor704, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar704 = lerp( desaturateInitialColor704, desaturateDot704.xxx, 1.0 );
				float3 smoothstepResult705 = smoothstep( temp_cast_37 , temp_cast_38 , desaturateVar704);
				float4 lerpResult706 = lerp( _Color1 , _Color2 , float4( smoothstepResult705 , 0.0 ));
				#if defined(_COLORSTYLE_NORMAL)
				float4 staticSwitch713 = float4( blurR1690 , 0.0 );
				#elif defined(_COLORSTYLE_BLACKWHITEFLASH)
				float4 staticSwitch713 = saturate( lerpResult706 );
				#elif defined(_COLORSTYLE_COLORREVERSE)
				float4 staticSwitch713 = float4( ( 1.0 - saturate( blurR1690 ) ) , 0.0 );
				#else
				float4 staticSwitch713 = float4( blurR1690 , 0.0 );
				#endif
				float3 blurB1691 = fincolorB15;
				float3 temp_cast_43 = (temp_output_383_0).xxx;
				float3 temp_cast_44 = (temp_output_93_0).xxx;
				float3 desaturateInitialColor699 = saturate( ( temp_output_310_0 + blurB1691 ) );
				float desaturateDot699 = dot( desaturateInitialColor699, float3( 0.299, 0.587, 0.114 ));
				float3 desaturateVar699 = lerp( desaturateInitialColor699, desaturateDot699.xxx, 1.0 );
				float3 smoothstepResult700 = smoothstep( temp_cast_43 , temp_cast_44 , desaturateVar699);
				float4 lerpResult701 = lerp( _Color1 , _Color2 , float4( smoothstepResult700 , 0.0 ));
				#if defined(_COLORSTYLE_NORMAL)
				float4 staticSwitch710 = float4( blurB1691 , 0.0 );
				#elif defined(_COLORSTYLE_BLACKWHITEFLASH)
				float4 staticSwitch710 = saturate( lerpResult701 );
				#elif defined(_COLORSTYLE_COLORREVERSE)
				float4 staticSwitch710 = float4( ( 1.0 - saturate( blurB1691 ) ) , 0.0 );
				#else
				float4 staticSwitch710 = float4( blurB1691 , 0.0 );
				#endif
				float3 appendResult716 = (float3(staticSwitch713.r , staticSwitch637.g , staticSwitch710.b));
				float3 lerpResult826 = lerp( appendResult596 , appendResult716 , 0.7);
				float4 lerpResult376 = lerp( tex2D( _MainTex, uv_MainTex ) , float4( lerpResult826 , 0.0 ) , ( _IfMainAlpha == 0.0 ? _MainAlpha : _MainAlphaK ));
				float2 texCoord325 = i.uv.xy * float2( 1,1 ) + float2( 0,0 );
				float2 appendResult839 = (float2(_Tex_ST.z , _Tex_ST.w));
				float cos324 = cos( ( _TexRotator * ( 2.0 * UNITY_PI ) ) );
				float sin324 = sin( ( _TexRotator * ( 2.0 * UNITY_PI ) ) );
				float2 rotator324 = mul( ( ( float4( texCoord325, 0.0 , 0.0 ) * _Tex_ST ) + float4( appendResult839, 0.0 , 0.0 ) ).xy - float2( 0,0 ) , float2x2( cos324 , -sin324 , sin324 , cos324 )) + float2( 0,0 );
				float4 tex2DNode320 = tex2D( _Tex, rotator324 );
				float Tex343 = ( _TexAR == 0.0 ? tex2DNode320.a : tex2DNode320.r );
				float4 temp_cast_51 = (Tex343).xxxx;
				float4 lerpResult318 = lerp( ( Vignette340 * lerpResult376 ) , temp_cast_51 , _TexAlpha);
				float2 texCoord403 = i.uv.xy * float2( 1,1 ) + float2( 0,0 );
				float2 appendResult409 = (float2(_Logo_ST.x , _Logo_ST.y));
				float2 appendResult412 = (float2(_Logo_ST.z , _Logo_ST.w));
				float2 break417 = ( ( texCoord403 * appendResult409 ) + appendResult412 );
				float2 appendResult419 = (float2(( break417.x - ( _Logo_ST.x - 1.0 ) ) , break417.y));
				float2 clampResult404 = clamp( appendResult419 , float2( 0,0 ) , float2( 1,1 ) );
				float4 tex2DNode400 = tex2D( _Logo, clampResult404 );
				float LogoAlpha405 = ( _LogoAR == 0.0 ? tex2DNode400.a : tex2DNode400.r );
				float4 temp_cast_52 = (LogoAlpha405).xxxx;
				float4 lerpResult421 = lerp( lerpResult318 , temp_cast_52 , saturate( ( LogoAlpha405 * _LogoAlpha ) ));
				

				finalColor = lerpResult421;

				return finalColor;
			} 
			ENDCG 
		}
	}
	CustomEditor "PostGUI"
	
	
}
