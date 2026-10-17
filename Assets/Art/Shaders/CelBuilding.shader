Shader "Aftershock/CelBuilding"
{
    Properties
    {
        _BaseColor ("Ana Renk", Color) = (0.58, 0.57, 0.55, 1)
        _PatchColor ("Leke Rengi (Noise)", Color) = (0.48, 0.47, 0.45, 1)
        _NoiseScale ("Leke Ölçeği", Float) = 1.8
        _NoiseThreshold ("Leke Eşiği (0-1, Yüksek = Az Leke)", Range(0, 1)) = 0.74
        _NoiseStrength ("Leke Görünürlüğü (0-1)", Range(0, 1)) = 1.0
        _CornerAO ("Oda Köşe Kararması (AO)", Range(0, 1)) = 0.32
        [Toggle] _UseBasementDamp ("Bodrum Yeşil Rutubet Şeridi", Float) = 0
        _DampColor ("Rutubet Yeşili", Color) = (0.35, 0.40, 0.27, 1)
        _DampBorderColor ("Rutubet Üst Gri Geçiş", Color) = (0.46, 0.48, 0.45, 1)
        _DampHeight ("Rutubet Yüksekliği (Dünya Y)", Float) = 0.98

        [Header(Blender ElCizimi Tonlari)]
        _ToonLightDir ("Çizim Işığı Yönü (Blender -0.3,-0.8,0.5)", Vector) = (-0.3, 0.5, -0.8, 0)
        _ShadowTone ("Gölge Tonu", Range(0, 1.5)) = 0.5
        _MidTone ("Orta Ton", Range(0, 1.5)) = 0.74
        _LitTone ("Aydınlık Ton", Range(0, 1.5)) = 0.92
        _ShadowStep ("Gölge Eşiği", Range(0, 1)) = 0.38
        _LitStep ("Aydınlık Eşiği", Range(0, 1)) = 0.62
        _Brightness ("Genel Parlaklık (Koyu Tema)", Range(0, 1.5)) = 0.82
        _SceneLightInfluence ("Sahne Işığı Etkisi (elektrik kesintisi vb.)", Range(0, 1)) = 0.25
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _PatchColor;
                float _NoiseScale;
                float _NoiseThreshold;
                float _NoiseStrength;
                float _CornerAO;
                float _UseBasementDamp;
                float4 _DampColor;
                float4 _DampBorderColor;
                float _DampHeight;
                float4 _ToonLightDir;
                float _ShadowTone;
                float _MidTone;
                float _LitTone;
                float _ShadowStep;
                float _LitStep;
                float _Brightness;
                float _SceneLightInfluence;
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

                // 1. Prosedürel sıva/beton/pas lekeleri (Blender Noise + ColorRamp)
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
                        // Yeşil ile gri sınır arasındaki ince koyu kontur çizgisi
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

                // 3. Oda içi köşe/kenar kararması (Blender'daki yumuşak iç köşe AO gölgeleri)
                if (_CornerAO > 0.001)
                {
                    // Her kat 3.5m yüksekliğinde (0..3.5, 3.5..7.0, 7.0..10.5, 10.5..14.0)
                    float floorLocalY = frac(max(0.0, wp.y) / 3.5) * 3.5;
                    float distToFloorOrCeil = min(floorLocalY, 3.5 - floorLocalY);
                    float verticalCorner = 1.0 - smoothstep(0.0, 0.55, distToFloorOrCeil);

                    // Arka duvara (z ~ +2.0) yakınlık
                    float distToBackWall = abs(2.0 - wp.z);
                    float backCorner = 1.0 - smoothstep(0.0, 0.65, distToBackWall);

                    // Dış yan duvarlara (x ~ +-9.0) yakınlık
                    float distToSideWall = min(abs(wp.x - 9.0), abs(wp.x + 9.0));
                    float sideCorner = 1.0 - smoothstep(0.0, 0.65, distToSideWall);

                    // Yüzey yönüne göre köşe AO birleşimi
                    float isHorizontalSurface = step(0.5, abs(N.y)); // Tavan veya Zemin
                    float aoFactor = isHorizontalSurface > 0.5
                        ? max(backCorner, sideCorner * 0.8)
                        : max(verticalCorner, sideCorner * 0.85);

                    // İnce kirli doku ile köşe gölgesini doğal kırp
                    aoFactor *= (0.85 + 0.30 * n);
                    col *= (1.0 - saturate(aoFactor * _CornerAO));
                }

                // 4. Blender "ElCizimi" grubu: sahneden bağımsız sabit çizim ışığı, keskin üç ton bandı.
                // Blender ışığı (-0.3,-0.8,0.5) Unity eksenlerinde (-0.3,0.5,-0.8): arka duvar aydınlık,
                // sol duvar orta, tavanın altı gölge tonunda kalır.
                float lam = dot(N, normalize(_ToonLightDir.xyz)) * 0.5 + 0.5;
                float tone = lam < _ShadowStep ? _ShadowTone : (lam < _LitStep ? _MidTone : _LitTone);

                // Sahne ışığı (şalter, gün döngüsü) yalnızca hafifçe karışır; çizim tonlarını bozmaz.
                Light mainLight = GetMainLight();
                float3 sceneLight = saturate(SampleSH(N) + mainLight.color * 0.35);
                float3 lightTint = lerp(float3(1.0, 1.0, 1.0), sceneLight, _SceneLightInfluence);

                float3 finalCol = col * tone * _Brightness * lightTint;
                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
