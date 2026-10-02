Shader "Hodba/GreatWall"
{
    Properties
    {
        _BaseColor ("Weathered sandstone", Color) = (0.42,0.36,0.28,1)
        _BlockSize ("Block width / height in metres", Vector) = (8,4,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "HodbaAtmosphere.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BlockSize;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 masonry : TEXCOORD2;
            };
            float HashBlock(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            Varyings vert(Attributes v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.masonry = v.uv;
                // UV coordinates remain in metres when the floating origin moves.
                if (abs(v.normalOS.x) > 0.5 || abs(v.normalOS.y) > 0.5)
                    o.masonry.x += GetObjectToWorldMatrix()._m23 + _HodbaOriginMod.y;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.masonry / max(_BlockSize.xy, float2(0.1,0.1));
                p.x += fmod(floor(p.y),2.0) * 0.5;
                float2 cell = floor(p), f = frac(p);
                float2 edge = min(f,1-f) * _BlockSize.xy;
                float distanceToJoint = min(edge.x,edge.y);
                float aa = max(fwidth(distanceToJoint),0.01);
                half face = smoothstep(0.035-aa,0.11+aa,distanceToJoint);
                // Courses fade to their average at distance, preventing moire on kilometre faces.
                float detail = saturate(1.0-max(fwidth(p.x),fwidth(p.y)));
                half block = lerp(1,lerp(0.84,1.15,HashBlock(cell)),detail);
                half joint = lerp(0.97,lerp(0.46,1,face),detail);
                half weather = 0.88 + 0.12*sin(i.masonry.x*0.043+sin(i.masonry.y*0.017))
                    + 0.09*sin(i.masonry.y*0.061+i.masonry.x*0.003);
                // Broad mineral stains remain readable above the dust at kilometre distances.
                weather *= 0.88 + 0.12*sin(i.masonry.x*0.011 + sin(i.masonry.y*0.0018)*3);
                half sand = saturate(1-i.positionWS.y/65.0)*0.12;
                half3 albedo = _BaseColor.rgb * block * joint * weather + sand*half3(0.22,0.17,0.1);
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetCameraPositionWS()-i.positionWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diffuse = HodbaRoughDiffuse(n,light.direction,view,0.85,0.03);
                half shade = light.shadowAttenuation * light.distanceAttenuation * HodbaDustShadow(i.positionWS)
                    * HodbaWallLightVisibility(i.positionWS+n*0.2,light.direction);
                half3 ambient=HodbaWallAmbientLight(SampleSH(n),i.positionWS,n,light.direction);
                half3 color = albedo*(light.color*diffuse*shade+ambient);
                return half4(HodbaApplyFog(color,i.positionWS),1);
            }
            ENDHLSL
        }
        UsePass "Hodba/Rock/DepthOnly"
        UsePass "Hodba/Rock/ShadowCaster"
    }
}
