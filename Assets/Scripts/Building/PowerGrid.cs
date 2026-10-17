using AftershockIsle.Core;
using UnityEngine;

namespace AftershockIsle.Building
{
    public readonly struct PowerChanged
    {
        public readonly bool IsOn;
        public PowerChanged(bool isOn) => IsOn = isOn;
    }

    /// <summary>
    /// Binanın elektriği (ana şalter). Açıkken iç lambalar yanar; bodrumdaki su elektriklenebilir.
    /// Telsizden sinyal göndermek ve elektrikli pompa elektrik ister; kesikken karakterler karanlıkta strese girer.
    /// </summary>
    public class PowerGrid : MonoBehaviour
    {
        [SerializeField] bool startsOn = true;
        [SerializeField] Light[] interiorLights;

        public static PowerGrid Instance { get; private set; }
        public static bool IsOn => Instance != null && Instance.on;

        bool on;

        void Awake()
        {
            Instance = this;
            on = startsOn;
            ApplyLights();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPower(bool value)
        {
            if (value == on) return;

            on = value;
            ApplyLights();
            EventBus.Publish(new PowerChanged(on));
        }

        void ApplyLights()
        {
            foreach (var light in interiorLights)
                if (light != null) light.enabled = on;
        }
    }
}
