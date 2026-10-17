using UnityEngine;

namespace AftershockIsle.Effects
{
    /// <summary>
    /// Yangın odağının kodla kurulan sürekli partikül görseli: alev dilleri, uçuşan kıvılcımlar ve yükselen duman.
    /// FireSpot her karede yoğunluğu (0–1) bildirir; partikül miktarı ve alevin boyu ona göre büyür.
    /// </summary>
    public class FireParticles : MonoBehaviour
    {
        static Texture2D softDot;

        ParticleSystem flames;
        ParticleSystem embers;
        ParticleSystem smoke;
        bool burning;

        /// <summary>Verilen odağın altına partikülleri kurar. Malzeme yoksa EffectsPlayer'ınkini, o da yoksa URP partikül shader'ını kullanır.</summary>
        public static FireParticles Create(Transform parent)
        {
            var go = new GameObject("Alev Partikülleri");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<FireParticles>();
            fx.Build();
            return fx;
        }

        public void Set(bool isBurning, float intensity)
        {
            float k = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(intensity));
            SetRate(flames, 46f * k);
            SetRate(embers, 9f * k);
            SetRate(smoke, 7f * k);

            var main = flames.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.55f * k, 1.0f * k);
            var shape = flames.shape;
            shape.radius = 0.5f * k;

            if (isBurning == burning) return;
            burning = isBurning;
            foreach (var ps in new[] { flames, embers, smoke })
            {
                if (isBurning) ps.Play();
                else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        static void SetRate(ParticleSystem ps, float rate)
        {
            var emission = ps.emission;
            emission.rateOverTime = rate;
        }

        void Build()
        {
            var material = CreateMaterial();

            flames = CreateSystem("Alev", material, new Vector3(0f, 0.05f, 0f));
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.15f;
            main.maxParticles = 120;
            Cone(flames, 10f, 0.5f);
            SizeOverLife(flames, 1f, 0.15f);
            ColorOverLife(flames,
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.35f),
                        new GradientColorKey(new Color(0.75f, 0.12f, 0.04f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });

            embers = CreateSystem("Kıvılcım", material, new Vector3(0f, 0.3f, 0f));
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 40;
            Cone(embers, 22f, 0.4f);
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 1.5f;
            ColorOverLife(embers,
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });

            smoke = CreateSystem("Duman", material, new Vector3(0f, 1.1f, 0f));
            main = smoke.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 40;
            Cone(smoke, 15f, 0.35f);
            SizeOverLife(smoke, 0.6f, 2.4f);
            ColorOverLife(smoke,
                new[] { new GradientColorKey(new Color(0.22f, 0.2f, 0.19f), 0f), new GradientColorKey(new Color(0.1f, 0.1f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0f, 1f) });
            // Duman alevin arkasında kalsın.
            smoke.GetComponent<ParticleSystemRenderer>().sortingFudge = 10f;
        }

        ParticleSystem CreateSystem(string name, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPosition;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = Color.white;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f); // koni yukarı baksın
        }

        static void SizeOverLife(ParticleSystem ps, float start, float end)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, start, 1f, end));
        }

        static void ColorOverLife(ParticleSystem ps, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            col.color = gradient;
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

            material.name = "Fire_Particle";
            var dot = SoftDot();
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", dot);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", dot);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }

        /// <summary>Kenarları yumuşak beyaz daire; kare partikül yerine alev/duman lekesi görünümü verir.</summary>
        static Texture2D SoftDot()
        {
            if (softDot != null) return softDot;

            const int size = 64;
            softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftDot", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - r);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * (3f - 2f * a) * 255f));
            }
            softDot.SetPixels32(pixels);
            softDot.Apply(false, true);
            return softDot;
        }
    }
}
