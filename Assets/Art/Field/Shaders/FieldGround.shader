Shader "Hodba/Field/Ash Ground"
{
 Properties {
  _BaseMap("Ash albedo",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1)
  _AshNormal("Ash normal",2D)="bump"{} _AshRoughness("Ash roughness",2D)="white"{} _AshAO("Ash AO",2D)="white"{}
  _CrustMap("Crust albedo",2D)="white"{} _CrustNormal("Crust normal",2D)="bump"{} _CrustRoughness("Crust roughness",2D)="white"{} _CrustAO("Crust AO",2D)="white"{}
  _RippleNormal("Wind ripple normal",2D)="bump"{}
  _TileScale("Repeats per meter",Float)=0.5 _PatchScale("Large patch frequency",Float)=0.08 _RippleStrength("Ripple strength",Range(0,1))=0.5
  _Cutoff("Cutoff",Float)=0.5
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass {
   Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_fog
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
   TEXTURE2D(_AshNormal); TEXTURE2D(_AshRoughness); TEXTURE2D(_AshAO);
   TEXTURE2D(_CrustMap); TEXTURE2D(_CrustNormal); TEXTURE2D(_CrustRoughness); TEXTURE2D(_CrustAO); TEXTURE2D(_RippleNormal);
   CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor; float4 _BaseMap_ST; float _TileScale, _PatchScale, _RippleStrength, _Cutoff;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION; float3 normalOS:NORMAL;};
   struct Varyings {float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float fog:TEXCOORD1;};
   Varyings vert(Attributes v) { Varyings o; VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.fog=ComputeFogFactor(p.positionCS.z);return o;}
   float hash(float2 p) {return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p) {float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 frag(Varyings v):SV_Target {
    float2 uv=v.positionWS.xz*_TileScale;
    float n=noise(v.positionWS.xz*_PatchScale)*0.7+noise(v.positionWS.xz*_PatchScale*2.1)*0.3;
    half blend=smoothstep(0.42,0.64,n);
    half3 a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).rgb;
    float2 uv4=v.positionWS.xz*0.25;
    half3 c=SAMPLE_TEXTURE2D(_CrustMap,sampler_BaseMap,uv4).rgb;
    half3 na=UnpackNormal(SAMPLE_TEXTURE2D(_AshNormal,sampler_BaseMap,uv));
    half3 nc=UnpackNormal(SAMPLE_TEXTURE2D(_CrustNormal,sampler_BaseMap,uv4));
    half3 nr=UnpackNormal(SAMPLE_TEXTURE2D(_RippleNormal,sampler_BaseMap,uv4));
    half3 nt=normalize(lerp(na,nc,blend)+half3(nr.xy*_RippleStrength,0));
    half rough=lerp(SAMPLE_TEXTURE2D(_AshRoughness,sampler_BaseMap,uv).r,SAMPLE_TEXTURE2D(_CrustRoughness,sampler_BaseMap,uv4).r,blend);
    half ao=lerp(SAMPLE_TEXTURE2D(_AshAO,sampler_BaseMap,uv).r,SAMPLE_TEXTURE2D(_CrustAO,sampler_BaseMap,uv4).r,blend);
    SurfaceData s=(SurfaceData)0;s.albedo=lerp(a,c,blend)*_BaseColor.rgb;s.alpha=1;s.smoothness=1-rough;s.occlusion=ao;s.normalTS=nt;
    InputData d=(InputData)0;d.positionWS=v.positionWS;d.normalWS=normalize(half3(nt.x,nt.z,nt.y));d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(v.positionWS);d.shadowCoord=TransformWorldToShadowCoord(v.positionWS);d.bakedGI=SampleSH(d.normalWS);d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(v.positionCS);d.shadowMask=half4(1,1,1,1);
    half4 color=UniversalFragmentPBR(d,s);color.rgb=MixFog(color.rgb,v.fog);return color;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
