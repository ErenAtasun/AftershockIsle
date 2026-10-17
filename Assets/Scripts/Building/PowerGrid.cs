using System.Collections.Generic;
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
        /// <summary>Ana şalter açıkken ya da Yedek Jeneratör kuruluyken pompa ve telsiz çalışır.</summary>
        public static bool HasEquipmentPower => IsOn || AftershockIsle.Crafting.PlaceableStructure.HasAny(AftershockIsle.Crafting.RecipeId.BackupGenerator);

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

        // Sahne kurucunun bağlamadığı, çalışma anında eklenen lambalar (ModelManager'ın oda lambaları).
        static readonly List<Light> extraLights = new List<Light>();

        /// <summary>Lambayı şaltere bağlar: şalter inince söner. PowerGrid henüz yoksa kurulunca uygulanır.</summary>
        public static void RegisterLight(Light light)
        {
            if (light == null || extraLights.Contains(light)) return;
            extraLights.Add(light);
            if (Instance != null) light.enabled = Instance.on;
        }

        void ApplyLights()
        {
            foreach (var light in interiorLights)
                if (light != null) light.enabled = on;

            extraLights.RemoveAll(l => l == null);
            foreach (var light in extraLights) light.enabled = on;
        }
    }
}
