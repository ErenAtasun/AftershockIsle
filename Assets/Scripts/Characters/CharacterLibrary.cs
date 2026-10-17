using UnityEngine;

namespace AftershockIsle.Characters
{
    /// <summary>
    /// Karakter modelinin çalışma anında kullandığı veriler: FBX prefabı, animasyon klipleri ve malzemeler.
    /// Sahne kurucu (Aftershock → Greybox Sahnesini Oluştur) Assets/Models/**/Char klasöründen doldurur.
    /// Kazazedeler, saha kâşifi, yoldaş ve insan düşmanlar aynı modeli kullanır (renkle ayrılırlar).
    /// </summary>
    [CreateAssetMenu(menuName = "Aftershock/Character Library", fileName = "CharacterLibrary")]
    public class CharacterLibrary : ScriptableObject
    {
        public GameObject prefab;

        [Header("Animasyon")]
        [Tooltip("Hareket döngüsü (koşu ya da yürüyüş). Yoksa model sabit kalır.")]
        public AnimationClip moveClip;
        [Tooltip("Bekleme döngüsü. Boşsa duran karakter hareket klibinin 'Bekleme Pozu' anında donar.")]
        public AnimationClip idleClip;
        [Range(0f, 1f)] public float idlePoseTime = 0.41f;
        [Tooltip("Hareket klibinin kendi hızı (m/sn): oynatma hızı karakterin gerçek hızına bölünerek ayarlanır.")]
        public float moveSpeedReference = 4.1f;

        [Header("Görünüm")]
        public Material bodyMaterial;
        public Material hairMaterial;
        [Tooltip("Dokular klasörde bulunduysa renk yalnızca hafifçe karıştırılır; yoksa model düz renkle boyanır.")]
        public bool hasTextures;
        public Color hairColor = new Color(0.16f, 0.11f, 0.08f);
        [Tooltip("Model yanlış yöne bakıyorsa (kamerayı ya da gidilen yönü gösteriyorsa) buradan döndür.")]
        public float yawOffset;
    }
}
