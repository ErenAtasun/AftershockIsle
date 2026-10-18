using System;
using AftershockIsle.Economy;
using UnityEngine;

namespace AftershockIsle.Expedition
{
    public enum FieldSpotKind { Loot, Rescue }

    /// <summary>Noktayı açmak için gereken alet: kilitli kasa Levye, gömülü sandık Kazma ister (karşılığında daha bol ganimet).</summary>
    public enum FieldSpotLock { None, Locked, Buried }

    /// <summary>
    /// Sahada aranabilir nokta (dolap, kasa, moloz) ya da mahsur kalan kazazede. Aranınca içindeki ganimet ortaya çıkar
    /// (Pending); çantaya sığan kısmı alınır, sığmayan noktada bekler ve yer açılınca tekrar etkileşimle alınır.
    /// Boşalınca koyulaşır. Kâşifin çantadan bıraktığı eşya da aranmış bir nokta olarak yere konur.
    /// </summary>
    public class FieldSpot
    {
        readonly Transform body;
        Renderer[] renderers;
        Color baseColor;
        bool dirty = true;

        public FieldSpot(Transform body, FieldSpotKind kind, float duration, string label, FieldSpotLock lockType = FieldSpotLock.None)
        {
            this.body = body;
            Kind = kind;
            Duration = duration;
            Label = label;
            Lock = lockType;
            renderers = new[] { body.GetComponent<Renderer>() };
            baseColor = kind == FieldSpotKind.Rescue ? new Color(0.4f, 0.85f, 0.95f)
                : lockType == FieldSpotLock.Locked ? new Color(0.45f, 0.5f, 0.6f)
                : lockType == FieldSpotLock.Buried ? new Color(0.5f, 0.45f, 0.38f)
                : new Color(0.55f, 0.38f, 0.2f);
        }

        public FieldSpotKind Kind { get; }
        public FieldSpotLock Lock { get; }
        public float Duration { get; }
        public string Label { get; }
        /// <summary>Arandı (ganimet ortaya çıktı).</summary>
        public bool Searched { get; private set; }
        /// <summary>Aranmış ama çantaya sığmadığı için noktada bekleyen ganimet (amount 0: yok).</summary>
        public ResourceAmount Pending { get; private set; }
        public bool HasPending => Pending.amount > 0;
        /// <summary>Tamamen bitti: arandı ve içinde bir şey kalmadı.</summary>
        public bool Done { get; private set; }
        public float X => body.position.x;
        public Vector3 Position => body.position;
        public Transform Body => body;

        /// <summary>Kutunun yerine model giydirildi: renklendirme modelin renderer'larına (dokusunun üstüne) uygulanır.</summary>
        public void UseModel(Renderer[] modelRenderers)
        {
            if (modelRenderers == null || modelRenderers.Length == 0) return;
            renderers = modelRenderers;
            // Kilitli/gömülü noktalar model üstünde de ayırt edilsin diye hafif renkli kalır.
            baseColor = Lock == FieldSpotLock.Locked ? new Color(0.75f, 0.82f, 1f)
                : Lock == FieldSpotLock.Buried ? new Color(0.85f, 0.75f, 0.6f)
                : Color.white;
            dirty = true;
        }

        /// <summary>Arama bitti: ganimet ortaya çıkar (çantaya alınana kadar noktada bekler).</summary>
        public void Reveal(ResourceAmount loot)
        {
            Searched = true;
            SetPending(loot);
        }

        public void SetPending(ResourceAmount loot)
        {
            Pending = loot.amount > 0 ? loot : default;
            Done = Searched && !HasPending;
            dirty = true;
        }

        public void MarkDone()
        {
            Searched = true;
            Pending = default;
            Done = true;
            dirty = true;
        }

        public void Refresh(Action<Renderer, Color> tint)
        {
            if (!dirty) return;
            dirty = false;
            var color = Done ? baseColor * 0.35f : HasPending ? Color.Lerp(baseColor, new Color(1f, 0.9f, 0.4f), 0.5f) : baseColor;
            color.a = 1f;
            foreach (var renderer in renderers)
                if (renderer != null) tint(renderer, color);
        }
    }
}
