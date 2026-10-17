using System;
using System.Collections.Generic;
using UnityEngine;

namespace AftershockIsle.Characters
{
    public enum GunModelKind { None, Pistol, Smg, Shotgun, AssaultRifle, SniperRifle }

    /// <summary>
    /// Karakter modelinin çalışma anında kullandığı veriler: FBX prefabı, animasyon klipleri, malzemeler ve silah modelleri.
    /// Sahne kurucu (Aftershock → Greybox Sahnesini Oluştur) ve Varlıkları Yenile komutu Assets/Models/**/Char ve
    /// Assets/Low Poly Guns klasörlerinden otomatik doldurur.
    /// Kazazedeler, saha kâşifi, yoldaş ve insan düşmanlar aynı modeli kullanır (renkle ve silahla ayrılırlar).
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/Character Library", fileName = "CharacterLibrary")]
    public class CharacterLibrary : ScriptableObject
    {
        [Serializable]
        public struct GunVisualEntry
        {
            public string id;
            public GunModelKind kind;
            public GameObject prefab;
            public Material material;
            public float worldLength;
        }

        public GameObject prefab;

        [Header("Temel Hareket Animasyonları")]
        [Tooltip("Koşu döngüsü (rifle run vb.). Yoksa yürüyüş klibi kullanılır.")]
        public AnimationClip moveClip;
        [Tooltip("Yürüyüş döngüsü (walking.fbx). Sığınak içinde ve yavaş yürürken kullanılır.")]
        public AnimationClip walkClip;
        [Tooltip("Bekleme döngüsü (rifle aiming idle.fbx). Boşsa duran karakter hareket klibinin 'Bekleme Pozu' anında donar.")]
        public AnimationClip idleClip;
        [Range(0f, 1f)] public float idlePoseTime = 0.41f;
        [Tooltip("Koşu klibinin kendi hızı (m/sn).")]
        public float moveSpeedReference = 4.1f;
        [Tooltip("Yürüyüş klibinin kendi hızı (m/sn).")]
        public float walkSpeedReference = 1.7f;

        [Header("Nişan & Çatışma Animasyonları (Basic Shooter Pack)")]
        public AnimationClip walkBackClip;
        public AnimationClip runBackClip;
        public AnimationClip strafeLeftClip;
        public AnimationClip strafeRightClip;
        public AnimationClip fireClip;
        public AnimationClip reloadClip;
        public AnimationClip hitClip;
        public AnimationClip jumpClip;
        public AnimationClip tossClip;
        public AnimationClip turnLeftClip;
        public AnimationClip turnRightClip;

        [Header("Görünüm")]
        public Material bodyMaterial;
        public Material hairMaterial;
        [Tooltip("Dokular klasörde bulunduysa renk yalnızca hafifçe karıştırılır; yoksa model düz renkle boyanır.")]
        public bool hasTextures;
        [Tooltip("İkinci malzeme yuvasında (1002_Diffuse / body1) kendi dokusu varsa saç rengiyle karartılmaz.")]
        public bool hasSecondaryTexture;
        public Color hairColor = new Color(0.16f, 0.11f, 0.08f);
        [Tooltip("Model yanlış yöne bakıyorsa (kamerayı ya da gidilen yönü gösteriyorsa) buradan döndür.")]
        public float yawOffset;

        [Header("Silah Modelleri (Low Poly Guns Pack)")]
        public GunVisualEntry[] guns = Array.Empty<GunVisualEntry>();

        public bool TryGetGun(GunModelKind kind, int variant, out GunVisualEntry entry)
        {
            entry = default;
            if (kind == GunModelKind.None || guns == null || guns.Length == 0) return false;

            var matches = new List<GunVisualEntry>();
            foreach (var g in guns)
                if (g.kind == kind && g.prefab != null) matches.Add(g);

            if (matches.Count > 0)
            {
                entry = matches[Mathf.Abs(variant) % matches.Count];
                return true;
            }

            foreach (var g in guns)
            {
                if (g.prefab != null)
                {
                    entry = g;
                    return true;
                }
            }
            return false;
        }
    }
}
