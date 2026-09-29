Shader "Hodba/Field/Footprint"
{
 Properties { _BaseMap("Impression RGBA",2D)="white"{} _BumpMap("Impression normal",2D)="bump"{} _BaseColor("Tint",Color)=(1,1,1,0.85) }
 SubShader { Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent"}
 Pass { Tags {"LightMode"="UniversalForward"} Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Offset -1,-1
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap); TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseColor; float4 _BaseMap_ST;
 CBUFFER_END
 struct A {float4 p:POSITION;float2 uv:TEXCOORD0;float3 n:NORMAL;float4 t:TANGENT;};
 struct V {float4 p:SV_POSITION;float2 uv:TEXCOORD0;float3 ws:TEXCOORD1;float3 n:TEXCOORD2;float3 t:TEXCOORD3;float3 b:TEXCOORD4;};
 V vert(A a){V o;o.ws=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.ws);o.uv=a.uv;VertexNormalInputs n=GetVertexNormalInputs(a.n,a.t);o.n=n.normalWS;o.t=n.tangentWS;o.b=n.bitangentWS;return o;}
 half4 frag(V i):SV_Target {half4 c=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;half3 nt=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv));half3 n=normalize(i.t*nt.x+i.b*nt.y+i.n*nt.z);Light l=GetMainLight(TransformWorldToShadowCoord(i.ws));c.rgb*=SampleSH(n)+l.color*saturate(dot(n,l.direction))*l.shadowAttenuation;return c;}
 ENDHLSL
 }}
}
