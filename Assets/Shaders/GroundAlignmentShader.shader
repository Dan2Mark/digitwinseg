Shader "Custom/BuildingAlignmentShader"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
        }


        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"


            struct appdata
            {
                float4 vertex : POSITION;
            };


            struct v2f
            {
                float4 pos : SV_POSITION;
            };


            v2f vert(appdata v)
            {
                v2f o;
                o.pos =
                UnityObjectToClipPos(v.vertex);

                return o;
            }


            fixed4 frag(v2f i):SV_Target
            {
                return fixed4(2, 0, 0, 1);
            }

            ENDCG
        }
    }
}