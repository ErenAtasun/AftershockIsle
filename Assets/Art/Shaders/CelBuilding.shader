Shader "Aftershock/CelBuilding"
{
    Properties
    {
        _BaseColor ("Ana Renk", Color) = (0.58, 0.57, 0.55, 1)
        _PatchColor ("Leke Rengi (Noise)", Color) = (0.48, 0.47, 0.45, 1)
        _NoiseScale ("Leke Ölçeği", Float) = 1.8
        _NoiseThreshold ("Leke Eşiği (0-1, Yüksek = Az Leke)", Range(0, 1)) = 0.74
        _NoiseStrength ("Leke Görünürlüğü (0-1)", Range(0, 1)) = 1.0
        _CornerAO ("Oda Köşe Kararması (AO)", Range(0, 1)) = 0.35
        _MinAmbient ("Karanlık Tema Alt Işık Limiti", Range(0.0, 0.85)) = 0.06
        _PointLightSoftness ("Lamba Işığı Gücü", Range(0.2, 3.0)) = 1.3
        _SunStrength ("Pencereden Giren Gün Işığı Gücü", Range(0, 2)) = 1.0
        _ToonLightDir ("Çizim Işığı Yönü (Blender -0.3,-0.8,0.5)", Vector) = (-0.3, 0.5, -0.8, 0)
        _ToonAmount ("Çizim Tonu Etkisi (0 = yok)", Range(0, 1)) = 0.45
        _Brightness ("Genel Parlaklık", Range(0, 2)) = 1.0
        [Toggle] _UseBasementDamp ("Bodrum Yeşil Rutubet Şeridi", Float) = 0
        _DampColor ("Rutubet Yeşili", Color) = (0.35, 0.40, 0.27, 1)
        _DampBorderColor ("Rutubet Üst Gri Geçiş", Color) = (0.46, 0.48, 0.45, 1)
        _DampHeight ("Rutubet Yüksekliği (Dünya Y)", Float) = 0.98
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            // PC renderer Forward+ kullanıyor: lambalar ancak kümelenmiş ışık döngüsüyle okunur.
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PatchColor;
                float _NoiseScale;
                float _NoiseThreshold;
                float _NoiseStrength;
                float _CornerAO;
                float _MinAmbient;
                float _PointLightSoftness;
                float _SunStrength;
                float4 _ToonLightDir;
                float _ToonAmount;
                float _Brightness;
                float _UseBasementDamp;
                float4 _DampColor;
                float4 _DampBorderColor;
                float _DampHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                return lerp(
                    lerp(lerp(hash31(i + float3(0,0,0)), hash31(i + float3(1,0,0)), f.x),
                         lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x),
                         lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0;
                v += 0.60 * valueNoise(p);
                v += 0.28 * valueNoise(p * 2.15 + 11.3);
                v += 0.12 * valueNoise(p * 4.50 + 27.1);
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = posInputs.positionCS;
                o.positionWS = posInputs.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.normalWS);
                float3 wp = input.positionWS;

                // 1. Prosedürel sıva/beton/pas lekeleri
                float n = fbm(wp * _NoiseScale);
                float patchMask = smoothstep(_NoiseThreshold - 0.02, _NoiseThreshold + 0.02, n) * _NoiseStrength;
                float3 col = lerp(_BaseColor.rgb, _PatchColor.rgb, patchMask);

                // 2. Bodrum duvarı alt yeşil rutubet şeridi + dalgalı sınır + gri geçiş kuşağı
                if (_UseBasementDamp > 0.5 && wp.y < (_DampHeight + 0.45))
                {
                    float wave = (n - 0.5) * 0.24 + sin(wp.x * 1.9) * 0.07 + cos(wp.x * 4.3) * 0.04;
                    float h = wp.y - (_DampHeight + wave);

                    if (h < 0.0)
                    {
                        float3 dampDark = _DampColor.rgb * 0.82;
                        col = lerp(_DampColor.rgb, dampDark, patchMask * 0.85);
                        float lineMask = 1.0 - smoothstep(0.0, 0.035, abs(h));
                        col *= (1.0 - lineMask * 0.35);
                    }
                    else if (h < 0.18)
                    {
                        col = _DampBorderColor.rgb;
                        float topEdge = 1.0 - smoothstep(0.0, 0.03, abs(h - 0.18));
                        col *= (1.0 - topEdge * 0.25);
                    }
                }

                // 3. Oda içi köşe/kenar kararması (AO)
                if (_CornerAO > 0.001)
                {
                    float floorLocalY = frac(max(0.0, wp.y) / 3.5) * 3.5;
                    float distToFloorOrCeil = min(floorLocalY, 3.5 - floorLocalY);
                    float verticalCorner = 1.0 - smoothstep(0.0, 0.55, distToFloorOrCeil);

                    float distToBackWall = abs(2.0 - wp.z);
                    float backCorner = 1.0 - smoothstep(0.0, 0.65, distToBackWall);

                    float distToSideWall = min(abs(wp.x - 9.0), abs(wp.x + 9.0));
                    float sideCorner = 1.0 - smoothstep(0.0, 0.65, distToSideWall);

                    float isHorizontalSurface = step(0.5, abs(N.y));
                    float aoFactor = isHorizontalSurface > 0.5
                        ? max(backCorner, sideCorner * 0.8)
                        : max(verticalCorner, sideCorner * 0.85);

                    aoFactor *= (0.85 + 0.30 * n);
                    col *= (1.0 - saturate(aoFactor * _CornerAO));
                }

                // 4. This War of Mine tarzı ışık: ortam neredeyse karanlık; pencerelerden giren gölgeli soğuk gün ışığı
                //    ve odalardaki sarkan ampullerin ışık havuzları sahneyi taşır. Şalter inince lambalar söner.
                float4 shadowCoord = TransformWorldToShadowCoord(wp);
                Light mainLight = GetMainLight(shadowCoord);
                float sunNdotL = saturate(dot(N, mainLight.direction));
                float3 sun = mainLight.color * (sunNdotL * mainLight.shadowAttenuation * _SunStrength);

                float3 ambient = max(SampleSH(N), float3(_MinAmbient, _MinAmbient, _MinAmbient));

                float3 lamps = float3(0.0, 0.0, 0.0);
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = wp;
                inputData.normalWS = N;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light lamp = GetAdditionalLight(lightIndex, wp, half4(1, 1, 1, 1));
                    // Duvarlar ampule yan baksa da hafif aydınlansın (wrap), gölge ve mesafe sönümü korunur.
                    float wrap = saturate(dot(N, lamp.direction) * 0.7 + 0.3);
                    lamps += lamp.color * (lamp.distanceAttenuation * lamp.shadowAttenuation * wrap * _PointLightSoftness);
                LIGHT_LOOP_END
                #endif

                float3 totalLight = ambient + sun + lamps;
                // Ampulün dibindeki duvar bembeyaz patlamasın (yumuşak tavan).
                totalLight = totalLight / (1.0 + totalLight * 0.18);

                // 5. Blender "ElCizimi" form tonu: sabit çizim ışığına göre üç bant, hafifçe karışır.
                float lam = dot(N, normalize(_ToonLightDir.xyz)) * 0.5 + 0.5;
                float tone = lam < 0.38 ? 0.6 : (lam < 0.62 ? 0.82 : 1.0);
                tone = lerp(1.0, tone, _ToonAmount);

                float3 finalCol = col * totalLight * tone * _Brightness;
                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
