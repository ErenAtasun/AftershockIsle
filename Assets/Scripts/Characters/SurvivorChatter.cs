using System.Collections.Generic;
using AftershockIsle.Building;
using AftershockIsle.Crisis;
using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Kazazedelerin başının üstünde durum, moral ve rol konuşma balonları çıkarır.
    /// Açlık, stres, fobi, nöbet görevi veya iş tamamlama anlarında konuşurlar.
    /// </summary>
    [RequireComponent(typeof(Survivor))]
    public class SurvivorChatter : MonoBehaviour
    {
        Survivor survivor;
        string activeText;
        float remainingTime;
        float totalDuration = 3.5f;
        Color bubbleColor = Color.white;
        float ambientTimer;
        float stateCheckTimer;

        public bool HasActiveBubble => !string.IsNullOrEmpty(activeText) && remainingTime > 0f;
        public string ActiveText => activeText;
        public float RemainingTime => remainingTime;
        public float TotalDuration => totalDuration;
        public Color BubbleColor => bubbleColor;

        void Awake()
        {
            survivor = GetComponent<Survivor>();
            ambientTimer = Random.Range(10f, 25f);
            stateCheckTimer = Random.Range(3f, 8f);
        }

        void Update()
        {
            if (survivor == null || survivor.IsDead)
            {
                activeText = null;
                return;
            }

            float dt = Time.deltaTime;
            if (remainingTime > 0f)
            {
                remainingTime -= dt;
                if (remainingTime <= 0f) activeText = null;
            }

            // Durum kontrolleri (açlık, stres, fobi, nöbet)
            stateCheckTimer -= dt;
            if (stateCheckTimer <= 0f)
            {
                stateCheckTimer = Random.Range(8f, 15f);
                CheckStateChatter();
            }

            // Genel ortam sohbeti
            ambientTimer -= dt;
            if (ambientTimer <= 0f)
            {
                ambientTimer = Random.Range(35f, 65f);
                CheckAmbientChatter();
            }
        }

        public void Say(string text, float duration = 3.5f, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            activeText = text;
            totalDuration = duration;
            remainingTime = duration;
            bubbleColor = color ?? Color.white;
        }

        public void SayIfQuiet(string text, float duration = 3.5f, Color? color = null)
        {
            if (HasActiveBubble) return;
            Say(text, duration, color);
        }

        void CheckStateChatter()
        {
            if (HasActiveBubble) return;

            // 1. Gece nöbetçisi
            if (NightDefenseSystem.Guard == survivor)
            {
                string[] guardLines =
                {
                    "Gözüm kapıda, tetikteyim...",
                    "Dışarıdan sesler geliyor...",
                    "Kimse bu kapıdan izinsiz geçemez.",
                    "Hava buz gibi ama nöbeti bırakamam."
                };
                Say(guardLines[Random.Range(0, guardLines.Length)], 4f, new Color(1f, 0.9f, 0.45f));
                return;
            }

            // 2. Fobiler
            if (survivor.HasTrait(TraitId.Pyrophobic))
            {
                var fire = FireSpot.ForFloor(survivor.Mover.CurrentFloor);
                if (fire != null && fire.IsBurning)
                {
                    Say("Ateş! Yangını söndürün, nefes alamıyorum!", 4f, new Color(1f, 0.45f, 0.35f));
                    return;
                }
            }

            if (survivor.HasTrait(TraitId.Claustrophobic))
            {
                bool darkOrBasement = survivor.Mover.CurrentFloor == 0 || !PowerGrid.IsOn;
                if (darkOrBasement && Random.value < 0.4f)
                {
                    Say("Burası çok basık ve karanlık... Boğuluyorum!", 4f, new Color(0.9f, 0.6f, 1f));
                    return;
                }
            }

            // 3. Şiddetli açlık
            if (survivor.Condition.Hunger >= 2)
            {
                string[] hungryLines =
                {
                    "Açlıktan başım dönüyor...",
                    "Bir parça erzak olsa keşke...",
                    "Midem kazınıyor, gücüm kalmadı..."
                };
                Say(hungryLines[Random.Range(0, hungryLines.Length)], 3.8f, new Color(1f, 0.65f, 0.4f));
                return;
            }

            // 4. Aşırı stres
            if (survivor.Condition.Stress >= 70f && Random.value < 0.45f)
            {
                string[] stressLines =
                {
                    "Bu bina üstümüze yıkılacak!",
                    "Artık dayanamıyorum...",
                    "Daha ne kadar dayanabiliriz?"
                };
                Say(stressLines[Random.Range(0, stressLines.Length)], 4f, new Color(1f, 0.4f, 0.4f));
            }
        }

        void CheckAmbientChatter()
        {
            if (HasActiveBubble) return;

            // Sakin zaman düşünceleri
            if (survivor.HasTrait(TraitId.Optimist))
            {
                string[] optLines =
                {
                    "Bunu da atlatacağız, pes etmek yok.",
                    "Sığınak ayakta kaldığı sürece umut var.",
                    "Birbirimize kenetlenmeliyiz."
                };
                Say(optLines[Random.Range(0, optLines.Length)], 3.5f, new Color(0.6f, 1f, 0.65f));
                return;
            }

            if (survivor.HasTrait(TraitId.Miner) && survivor.Tasks.State == TaskState.Working)
            {
                Say("Enkazı temizlemek benim işim!", 3f, new Color(0.95f, 0.85f, 0.5f));
                return;
            }

            if (survivor.HasTrait(TraitId.Carpenter) && survivor.Tasks.State == TaskState.Working)
            {
                Say("Sağlam çakalım ki bir daha sarsıntıda oynamasın.", 3f, new Color(0.85f, 0.95f, 0.6f));
                return;
            }

            string[] generalLines =
            {
                "Radyodan iyi bir haber gelse bari...",
                "Bugün artçı sarsıntı olmasa keşke.",
                "Erzakları idareli kullanmalıyız.",
                "Herkes sağ salim kurtulacak."
            };
            Say(generalLines[Random.Range(0, generalLines.Length)], 3.5f, new Color(0.9f, 0.9f, 0.92f));
        }

        public void OnTaskCompleted(string actionName)
        {
            if (HasActiveBubble) return;
            string text = actionName switch
            {
                var s when s.Contains("Tamir") || s.Contains("Kolon") => "Kolon sağlama alındı!",
                var s when s.Contains("Moloz") || s.Contains("Kaz") => "Moloz temizlendi, yol açık.",
                var s when s.Contains("Tulumba") || s.Contains("Su") => "Suyu tahliye ettim.",
                var s when s.Contains("Yangın") || s.Contains("Söndür") => "Alevleri bastırdım!",
                _ => "İş tamamlandı."
            };
            Say(text, 3f, new Color(0.5f, 1f, 0.6f));
        }

        public void OnSelected()
        {
            string[] ackLines =
            {
                "Emret.",
                "Buradayım, ne yapayım?",
                "Hazırım.",
                "Dinliyorum."
            };
            Say(ackLines[Random.Range(0, ackLines.Length)], 2.5f, new Color(1f, 0.95f, 0.7f));
        }
    }
}
