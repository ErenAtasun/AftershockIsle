using System;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldSpotKind { Loot, Rescue }

    /// <summary>Sahada aranabilir nokta (dolap, kasa, moloz) ya da mahsur kalan kazazede. Arandıktan sonra koyulaşır.</summary>
    public class FieldSpot
    {
        readonly Transform body;
        Renderer[] renderers;
        Color baseColor;
        bool dirty = true;

        public FieldSpot(Transform body, FieldSpotKind kind, float duration, string label)
        {
            this.body = body;
            Kind = kind;
            Duration = duration;
            Label = label;
            renderers = new[] { body.GetComponent<Renderer>() };
            baseColor = kind == FieldSpotKind.Rescue ? new Color(0.4f, 0.85f, 0.95f) : new Color(0.55f, 0.38f, 0.2f);
        }

        public FieldSpotKind Kind { get; }
        public float Duration { get; }
        public string Label { get; }
        public bool Done { get; private set; }
        public float X => body.position.x;
        public Vector3 Position => body.position;

        /// <summary>Kutunun yerine model giydirildi: renklendirme modelin renderer'larına (dokusunun üstüne) uygulanır.</summary>
        public void UseModel(Renderer[] modelRenderers)
        {
            if (modelRenderers == null || modelRenderers.Length == 0) return;
            renderers = modelRenderers;
            baseColor = Color.white;
            dirty = true;
        }

        public void MarkDone()
        {
            Done = true;
            dirty = true;
        }

        public void Refresh(Action<Renderer, Color> tint)
        {
            if (!dirty) return;
            dirty = false;
            var color = Done ? baseColor * 0.35f : baseColor;
            color.a = 1f;
            foreach (var renderer in renderers)
                if (renderer != null) tint(renderer, color);
        }
    }
}
