using System;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldSpotKind { Loot, Rescue }

    /// <summary>Sahada aranabilir nokta (ganimet kasası) ya da mahsur kalan kazazede.</summary>
    public class FieldSpot
    {
        readonly Transform body;
        readonly Renderer renderer;
        readonly Color baseColor;
        bool dirty = true;

        public FieldSpot(Transform body, FieldSpotKind kind, float duration, string label)
        {
            this.body = body;
            Kind = kind;
            Duration = duration;
            Label = label;
            renderer = body.GetComponent<Renderer>();
            baseColor = kind == FieldSpotKind.Rescue ? new Color(0.4f, 0.85f, 0.95f) : new Color(0.55f, 0.38f, 0.2f);
        }

        public FieldSpotKind Kind { get; }
        public float Duration { get; }
        public string Label { get; }
        public bool Done { get; private set; }
        public float X => body.position.x;
        public Vector3 Position => body.position;

        public void MarkDone()
        {
            Done = true;
            dirty = true;
        }

        public void Refresh(Action<Renderer, Color> tint)
        {
            if (!dirty) return;
            dirty = false;
            tint(renderer, Done ? baseColor * 0.3f : baseColor);
        }
    }
}
