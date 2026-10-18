using AftershockIsle.Building;
using AftershockIsle.Core;
using AftershockIsle.Crisis;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Havanın sığınakta görünmesi ve duyulması. Ekran perdesi (filtre) kullanılmaz: yağış, binanın arkasında ve açık
    /// çatının üstünde gerçek partiküllerle yağar (kesit içine yağmaz), sis kümeleri binanın arkasında süzülür, fırtınada
    /// şimşek ayrı bir ışıkla sahneyi bir an aydınlatır ve gök gürler. Güneş ışığının rengi havaya göre hafifçe kayar
    /// (malzemelerin rengi korunur). Yağmur/rüzgâr sesleri sentezlenir; Resources/Audio/RainLoop, WindLoop, Thunder
    /// dosyaları varsa onlar kullanılır.
    /// </summary>
    public class WeatherVisuals : MonoBehaviour
    {
        const float BlendSpeed = 0.5f;

        BuildingLayout building;
        ParticleSystem backFall;
        ParticleSystem roofFall;
        ParticleSystem fogBanks;
        ParticleSystemRenderer backRenderer;
        ParticleSystemRenderer roofRenderer;
        Light lightning;
        Light sun;
        Color sunBase = Color.white;
        AudioSource rainSource;
        AudioSource windSource;
        AudioSource thunderSource;
        AudioClip thunderClip;

        WeatherType applied = (WeatherType)(-1);
        float nextLightning;
        float flashTime = -10f;
        float rainVolume;
        float windVolume;

        /// <summary>Şimşek parlaması (0–1); arka plan sisi de bununla bir an aydınlanır.</summary>
        public static float Flash01 { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Flash01 = 0f;
            if (FindFirstObjectByType<BuildingLayout>() == null || FindFirstObjectByType<WeatherVisuals>() != null) return;
            new GameObject("[Hava Görselleri]").AddComponent<WeatherVisuals>();
        }

        void Start()
        {
            building = FindFirstObjectByType<BuildingLayout>();
            if (building == null)
            {
                enabled = false;
                return;
            }

            var material = CreateMaterial();
            float backZ = building.WalkPlaneZ + building.Depth * 0.5f + building.WallThickness;
            float centerX = building.transform.position.x;
            float groundY = building.FloorY(1);
            float topY = building.TopY + 14f;

            // Binanın arkası: arka duvar ile uzak şehir arasında geniş bir yağış perdesi.
            backFall = CreateSystem("Yağış (Arka)", material, new Vector3(centerX, topY, backZ + 6f), new Vector3(90f, 0.2f, 10f), out backRenderer);
            // Açık çatı: kesitin içinde tek yağış yeri.
            float roofY = building.FloorY(building.RoofFloor);
            roofFall = CreateSystem("Yağış (Çatı)", material, new Vector3(centerX, roofY + 7f, building.WalkPlaneZ),
                new Vector3(building.Width + 1f, 0.2f, building.Depth + 0.4f), out roofRenderer);

            fogBanks = CreateSystem("Sis Kümeleri", material, new Vector3(centerX, groundY + 2.5f, backZ + 4f), new Vector3(70f, 3f, 6f), out var fogRenderer);
            fogRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            var fogMain = fogBanks.main;
            fogMain.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
            fogMain.startSize = new ParticleSystem.MinMaxCurve(6f, 11f);
            fogMain.maxParticles = 120;
            var fogVelocity = fogBanks.velocityOverLifetime;
            fogVelocity.enabled = true;
            fogVelocity.x = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            fogVelocity.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f);
            fogVelocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var fogFade = fogBanks.colorOverLifetime;
            fogFade.enabled = true;
            fogFade.color = new ParticleSystem.MinMaxGradient(FadeInOut());

            lightning = new GameObject("Şimşek").AddComponent<Light>();
            lightning.transform.SetParent(transform, false);
            lightning.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            lightning.type = LightType.Directional;
            lightning.color = new Color(0.82f, 0.88f, 1f);
            lightning.intensity = 0f;
            lightning.shadows = LightShadows.None;
            lightning.enabled = false;

            sun = FindSun();
            if (sun != null) sunBase = sun.color;

            rainSource = CreateSource("Yağmur Sesi", Resources.Load<AudioClip>("Audio/RainLoop") ?? SynthRain(4f));
            windSource = CreateSource("Rüzgâr Sesi", Resources.Load<AudioClip>("Audio/WindLoop") ?? SynthWind(6f));
            thunderSource = CreateSource("Gök Gürültüsü", null);
            thunderSource.loop = false;
            thunderClip = Resources.Load<AudioClip>("Audio/Thunder") ?? SynthThunder(3.2f);
        }

        void OnDestroy()
        {
            if (sun != null) sun.color = sunBase;
            Flash01 = 0f;
        }

        void Update()
        {
            if (building == null) return;

            var weather = WeatherSystem.Current;
            if (weather != applied) Apply(weather);

            float dt = Time.unscaledDeltaTime;
            float targetRain = weather == WeatherType.Storm ? 0.34f : weather == WeatherType.AcidRain ? 0.24f : 0f;
            float targetWind = weather == WeatherType.Storm ? 0.22f : weather == WeatherType.ColdWave ? 0.2f : weather == WeatherType.DenseFog ? 0.06f : 0f;
            // Duraklatınca sesler kısılır ama kesilmez.
            float pauseScale = Time.timeScale <= 0f ? 0.45f : 1f;
            rainVolume = Mathf.MoveTowards(rainVolume, targetRain * pauseScale, dt * 0.25f);
            windVolume = Mathf.MoveTowards(windVolume, targetWind * pauseScale, dt * 0.25f);
            SetLoop(rainSource, rainVolume);
            SetLoop(windSource, windVolume);

            UpdateSunTint(weather, dt);
            UpdateLightning(weather);
        }

        // ---------------------------------------------------------------- Hava değişimi

        void Apply(WeatherType weather)
        {
            applied = weather;

            switch (weather)
            {
                case WeatherType.AcidRain:
                    ConfigureFall(backFall, backRenderer, 900f, new Color(0.72f, 0.78f, 0.5f, 0.5f), 14f, 1.2f, false);
                    ConfigureFall(roofFall, roofRenderer, 160f, new Color(0.72f, 0.78f, 0.5f, 0.45f), 14f, 1.2f, false);
                    break;
                case WeatherType.Storm:
                    ConfigureFall(backFall, backRenderer, 1600f, new Color(0.75f, 0.8f, 0.9f, 0.55f), 20f, 6f, false);
                    ConfigureFall(roofFall, roofRenderer, 280f, new Color(0.75f, 0.8f, 0.9f, 0.5f), 20f, 6f, false);
                    break;
                case WeatherType.ColdWave:
                    ConfigureFall(backFall, backRenderer, 420f, new Color(0.95f, 0.97f, 1f, 0.8f), 1.6f, 0.8f, true);
                    ConfigureFall(roofFall, roofRenderer, 70f, new Color(0.95f, 0.97f, 1f, 0.75f), 1.6f, 0.8f, true);
                    break;
                default:
                    Stop(backFall);
                    Stop(roofFall);
                    break;
            }

            if (weather == WeatherType.DenseFog || weather == WeatherType.Storm)
            {
                var main = fogBanks.main;
                main.startColor = weather == WeatherType.DenseFog ? new Color(0.78f, 0.82f, 0.86f, 0.32f) : new Color(0.45f, 0.5f, 0.58f, 0.22f);
                var emission = fogBanks.emission;
                emission.rateOverTime = weather == WeatherType.DenseFog ? 6f : 2.5f;
                if (!fogBanks.isPlaying) fogBanks.Play();
            }
            else fogBanks.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            nextLightning = Time.time + Random.Range(4f, 9f);
        }

        static void ConfigureFall(ParticleSystem system, ParticleSystemRenderer renderer, float rate, Color color, float speed, float wind, bool snow)
        {
            var main = system.main;
            main.startColor = color;
            main.startSize = snow ? new ParticleSystem.MinMaxCurve(0.07f, 0.16f) : new ParticleSystem.MinMaxCurve(0.025f, 0.04f);
            main.startLifetime = snow ? new ParticleSystem.MinMaxCurve(9f, 13f) : new ParticleSystem.MinMaxCurve(1.6f, 2.2f);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.x = new ParticleSystem.MinMaxCurve(wind * 0.6f, wind);
            velocity.y = new ParticleSystem.MinMaxCurve(-speed * 1.1f, -speed * 0.9f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

            var noise = system.noise;
            noise.enabled = snow;
            noise.strength = 0.6f;
            noise.frequency = 0.4f;

            renderer.renderMode = snow ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = snow ? 0f : 0.045f;
            renderer.lengthScale = snow ? 1f : 2f;

            var emission = system.emission;
            emission.rateOverTime = rate;
            if (!system.isPlaying) system.Play();
        }

        static void Stop(ParticleSystem system)
        {
            if (system != null && system.isPlaying) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ---------------------------------------------------------------- Işık

        static Color SunTint(WeatherType weather) => weather switch
        {
            WeatherType.DenseFog => new Color(0.86f, 0.9f, 0.94f),
            WeatherType.AcidRain => new Color(0.86f, 0.92f, 0.72f),
            WeatherType.Storm => new Color(0.72f, 0.76f, 0.88f),
            WeatherType.ColdWave => new Color(0.8f, 0.9f, 1.05f),
            _ => Color.white,
        };

        void UpdateSunTint(WeatherType weather, float dt)
        {
            if (sun == null) return;
            sun.color = Color.Lerp(sun.color, sunBase * SunTint(weather), Mathf.Clamp01(dt * BlendSpeed));
        }

        void UpdateLightning(WeatherType weather)
        {
            if (weather == WeatherType.Storm && Time.time >= nextLightning && Time.timeScale > 0f)
            {
                nextLightning = Time.time + Random.Range(7f, 16f);
                flashTime = Time.time;
                float delay = Random.Range(0.25f, 1.4f);
                Invoke(nameof(PlayThunder), delay);
            }

            // Çift çakan kısa parlama.
            float t = Time.time - flashTime;
            float flash = t < 0f || t > 0.5f ? 0f
                : t < 0.06f ? t / 0.06f
                : t < 0.14f ? Mathf.Lerp(1f, 0.25f, (t - 0.06f) / 0.08f)
                : t < 0.2f ? Mathf.Lerp(0.25f, 0.8f, (t - 0.14f) / 0.06f)
                : Mathf.Lerp(0.8f, 0f, (t - 0.2f) / 0.3f);

            Flash01 = flash;
            lightning.enabled = flash > 0.001f;
            lightning.intensity = flash * 2.6f;
        }

        void PlayThunder()
        {
            if (thunderSource == null || thunderClip == null) return;
            thunderSource.pitch = Random.Range(0.85f, 1.1f);
            thunderSource.PlayOneShot(thunderClip, Random.Range(0.55f, 0.85f));
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            Light best = null;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional && (best == null || light.intensity > best.intensity)) best = light;
            return best;
        }

        // ---------------------------------------------------------------- Kurulum

        ParticleSystem CreateSystem(string name, Material material, Vector3 position, Vector3 boxSize, out ParticleSystemRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = position;

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            main.scalingMode = ParticleSystemScalingMode.Shape;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            var emission = system.emission;
            emission.rateOverTime = 0f;

            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        static Gradient FadeInOut()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            return gradient;
        }

        static Material CreateMaterial()
        {
            var source = EffectsPlayer.ParticleMaterial;
            Material material;
            if (source != null) material = new Material(source);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                material = new Material(shader);
            }

            material.name = "Weather_Particle";
            var dot = SoftDot();
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", dot);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", dot);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }

        static Texture2D SoftDot()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "WeatherDot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - r);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * (3f - 2f * a) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        // ---------------------------------------------------------------- Ses

        AudioSource CreateSource(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.clip = clip;
            return source;
        }

        static void SetLoop(AudioSource source, float volume)
        {
            if (source == null || source.clip == null) return;
            source.volume = volume;
            if (volume > 0.001f && !source.isPlaying) source.Play();
            else if (volume <= 0.001f && source.isPlaying) source.Stop();
        }

        const int SampleRate = 44100;

        static AudioClip SynthRain(float duration)
        {
            int samples = Mathf.RoundToInt(duration * SampleRate);
            var data = new float[samples];
            uint rng = 0x9E3779B9u;
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < samples; i++)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;
                lp += 0.35f * (white - lp);
                lp2 += 0.04f * (lp - lp2);
                float hiss = lp - lp2;
                // Ara sıra iri damla çıtırtısı.
                float drop = (rng & 0x3FF) == 0 ? white * 1.6f : 0f;
                float u = i / (float)samples;
                float edge = Mathf.Min(Mathf.Clamp01(u * 10f), Mathf.Clamp01((1f - u) * 10f));
                data[i] = Mathf.Clamp((hiss * 0.9f + lp2 * 0.5f + drop) * edge, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("RainLoop", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthWind(float duration)
        {
            int samples = Mathf.RoundToInt(duration * SampleRate);
            var data = new float[samples];
            uint rng = 0x1234567u;
            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < samples; i++)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;
                float u = i / (float)samples;
                // Yavaş uğultu ve rüzgâr dalgaları.
                float gust = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (u * 2f + 0.15f * Mathf.Sin(2f * Mathf.PI * u)));
                lp1 += (0.01f + 0.03f * gust) * (white - lp1);
                lp2 += 0.05f * (lp1 - lp2);
                float edge = Mathf.Min(Mathf.Clamp01(u * 10f), Mathf.Clamp01((1f - u) * 10f));
                data[i] = Mathf.Clamp(lp2 * 4.5f * gust * edge, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("WindLoop", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip SynthThunder(float duration)
        {
            int samples = Mathf.RoundToInt(duration * SampleRate);
            var data = new float[samples];
            uint rng = 0xDEADBEEFu;
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < samples; i++)
            {
                rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
                float white = (rng & 0xFFFF) / 32767.5f - 1f;
                float u = i / (float)samples;
                lp += 0.05f * (white - lp);
                lp2 += 0.02f * (lp - lp2);
                float crack = u < 0.04f ? white * (1f - u / 0.04f) * 0.6f : 0f;
                float roll = 0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 3.3f * u * duration * 0.4f);
                float env = Mathf.Clamp01(u * 25f) * Mathf.Exp(-u * 3.2f);
                data[i] = Mathf.Clamp((lp2 * 6f * roll + crack) * env, -0.95f, 0.95f);
            }
            var clip = AudioClip.Create("Thunder", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
