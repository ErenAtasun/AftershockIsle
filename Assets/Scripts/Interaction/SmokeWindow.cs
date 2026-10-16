using System.Collections.Generic;
using AftershockIsle.Characters;
using UnityEngine;

namespace AftershockIsle.Interaction
{
    /// <summary>Katın dış penceresi. Çekiçle kırılınca o kattaki duman hızla tahliye olur.</summary>
    public class SmokeWindow : Interactable
    {
        [SerializeField] GameObject glass;

        static readonly List<SmokeWindow> all = new List<SmokeWindow>();

        public bool IsBroken { get; private set; }

        public override string ActionLabel => "Pencereyi Kır";
        public override bool AllowedInSmoke => true;
        public override string StatusText => IsBroken ? "Kırık — duman dışarı çıkıyor." : "Kırılırsa duman tahliye olur.";

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        public static SmokeWindow ForFloor(int floor)
        {
            foreach (var window in all)
                if (window.Floor == floor) return window;
            return null;
        }

        public override bool CanStart(Survivor worker, out string reason)
        {
            reason = IsBroken ? "Pencere zaten kırık." : null;
            return !IsBroken;
        }

        public override void Complete(Survivor worker)
        {
            IsBroken = true;
            if (glass != null) glass.SetActive(false);
        }
    }
}
