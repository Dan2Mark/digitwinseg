Shader "Custom/Alignment/ClassID"
{
    Properties
    {
        _ClassID ("Class ID", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ClassID"

            Tags
            {
                "LightMode"="UniversalForward"
            }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask R

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _ClassID;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                output.positionHCS =
                    TransformObjectToHClip(input.positionOS.xyz);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_ClassID, 0, 0, 1);
            }

            ENDHLSL
        }
    }
}