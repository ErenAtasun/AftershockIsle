Shader "Aftershock/InteractOutline"
{
    // Etkileşilebilir eşyanın etrafındaki beyazımsı kontur: ters çevrilmiş kabuk (yalnızca arka yüzler), köşe noktaları
    // ekranda normal yönünde sabit piksel kadar itilir. Eşyanın kendisi önde kaldığı için yalnızca dış hattı görünür.
    Properties
    {
        _Color ("Renk", Color) = (0.95, 0.95, 0.9, 1)
        _Width ("Kalınlık (piksel)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "InteractOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float4 cs = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                float len = length(normalCS);
                float2 dir = len > 1e-5 ? normalCS / len : float2(0, 0);
                // Piksel → klip uzayı: 2 / ekran boyutu, perspektif bölmesine karşı w ile çarpılır.
                cs.xy += dir * (_Width * 2.0 / _ScreenParams.xy) * cs.w;
                o.positionCS = cs;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(_Color);
            }
            ENDHLSL
        }
    }
}
