using UnityEngine;

namespace AftershockIsle.Core
{
    public enum GameModeType { Campaign, Endless }

    /// <summary>
    /// Oyun modu. Kampanya: hedef 10. günden önce çatıdan tahliye (en hızlı tahliye rekor olarak saklanır).
    /// Sonsuz: tahliye yok; telsiz sinyali birkaç günde bir erzak yardımı getirir, afetler ve baskınlar gün geçtikçe
    /// sertleşir, amaç olabildiğince uzun hayatta kalmak (en uzun gün rekor olarak saklanır).
    /// Mod ana menüde seçilir ve kayda yazılır.
    /// </summary>
    public static class GameMode
    {
        public const int CampaignTargetDay = 10;
        const string CampaignBestKey = "Aftershock.CampaignBestDay";
        const string EndlessBestKey = "Aftershock.EndlessBestDay";

        public static GameModeType Current { get; set; } = GameModeType.Campaign;
        public static bool IsEndless => Current == GameModeType.Endless;

        public static string DisplayName => IsEndless ? "Sonsuz Mod" : "Kampanya";

        /// <summary>Kampanyada en az günde tahliye (0: henüz yok).</summary>
        public static int CampaignBest => ReadInt(CampaignBestKey);

        /// <summary>Sonsuz modda en uzun hayatta kalınan gün (0: henüz yok).</summary>
        public static int EndlessBest => ReadInt(EndlessBestKey);

        /// <summary>Kampanya zaferini kaydeder; yeni rekorsa true.</summary>
        public static bool RecordCampaignWin(int day)
        {
            int best = CampaignBest;
            if (best > 0 && day >= best) return false;
            WriteInt(CampaignBestKey, day);
            return true;
        }

        /// <summary>Sonsuz modda ulaşılan günü kaydeder; yeni rekorsa true.</summary>
        public static bool RecordEndlessDay(int day)
        {
            if (day <= EndlessBest) return false;
            WriteInt(EndlessBestKey, day);
            return true;
        }

        static int ReadInt(string key)
        {
            try { return PlayerPrefs.GetInt(key, 0); }
            catch { return 0; }
        }

        static void WriteInt(string key, int value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value);
                PlayerPrefs.Save();
            }
            catch { }
        }
    }
}
