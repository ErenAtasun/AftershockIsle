using AftershockIsle.Characters;
using AftershockIsle.Expedition;
using AftershockIsle.UI;
using UnityEngine;

namespace AftershockIsle.Core
{
    /// <summary>
    /// Oyunun çalışma anında bulduğu varlık kayıtları: karakter modeli, saha modelleri, arayüz görselleri.
    /// Assets/Resources/GameAssets.asset olarak durur; editör, Assets/Models altında bir dosya değişince bu kayıtları
    /// kendiliğinden yeniler (GameAssetsSync). Böylece yeni model ya da görsel eklemek için sahneyi yeniden kurmak gerekmez:
    /// sahnedeki referans boşsa ya da eskiyse sistemler buradan okur.
    /// </summary>
    public class GameAssets : ScriptableObject
    {
        public const string ResourcePath = "GameAssets";

        public CharacterLibrary characters;
        public FieldArtLibrary fieldArt;
        public UiSkin ui;

        static GameAssets cached;
        static bool searched;

        public static GameAssets Instance
        {
            get
            {
                if (cached == null && !searched)
                {
                    searched = true;
                    cached = Resources.Load<GameAssets>(ResourcePath);
                }
                return cached;
            }
        }

        public static CharacterLibrary Characters => Instance != null ? Instance.characters : null;
        public static FieldArtLibrary FieldArt => Instance != null ? Instance.fieldArt : null;
        public static UiSkin Ui => Instance != null ? Instance.ui : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            cached = null;
            searched = false;
        }
    }
}
