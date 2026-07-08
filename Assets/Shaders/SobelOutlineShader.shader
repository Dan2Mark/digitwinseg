Shader "Custom/SobelOutline"
{
    SubShader
    {
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float _Thickness;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                float2 texel = _Thickness / _ScreenParams.xy;

                float3 tl = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(-1, 1)).rgb;
                float3 t  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(0, 1)).rgb;
                float3 tr = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(1, 1)).rgb;

                float3 l  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(-1, 0)).rgb;
                float3 r  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(1, 0)).rgb;

                float3 bl = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(-1, -1)).rgb;
                float3 b  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(0, -1)).rgb;
                float3 br = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + texel * float2(1, -1)).rgb;

                float3 gx = -tl - 2*l - bl + tr + 2*r + br;
                float3 gy = -tl - 2*t - tr + bl + 2*b + br;

                float edge = length(gx + gy);

                return float4(edge, edge, edge, 1);
            }
            ENDHLSL
        }
    }
}
