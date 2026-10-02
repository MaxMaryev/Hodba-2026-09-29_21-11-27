Shader "Hidden/Hodba/WallLightingProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../../Hodba/Client/Shaders/HodbaAtmosphere.hlsl"
            float4 _ProbePosition, _ProbeNormal, _ProbeEye;
            float _ProbeMode;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(float4 position : POSITION) { Varyings o; o.positionCS=TransformObjectToHClip(position.xyz); return o; }
            float4 frag(Varyings i) : SV_Target
            {
                float value = 1;
                #if defined(HODBA_WALL_LIGHTING)
                if (_ProbeMode < 0.5) value=HodbaWallLightVisibility(_ProbePosition.xyz,HodbaSunDirection());
                else if (_ProbeMode < 1.5) value=HodbaWallAmbientOcclusion(_ProbePosition.xyz,_ProbeNormal.xyz);
                else value=HodbaFogLightVisibility(_ProbeEye.xyz,_ProbePosition.xyz);
                #endif
                return float4(value,value,value,1);
            }
            ENDHLSL
        }
    }
}
