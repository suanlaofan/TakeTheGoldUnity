using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wukong.StacklineClassic
{
    [Serializable]
    public sealed class StacklineProfileData
    {
        public int schemaVersion = StacklineProfileStore.CurrentSchemaVersion;
        public int bestScore;
        public int gems = 2;
        public int lives;
        public int stars;
        public int selectedTheme;
        public int unlockedThemeMask = 1;
        public bool adFree;
        public bool doubleGems;
        public bool soundEnabled = true;
        public bool hapticsEnabled = true;
        public string language = StacklineProfileStore.DefaultLanguageCode;
        public int challengeClaims;
        public int runCount;
        public string dailyBonusDay = string.Empty;
        public List<int> recentScores = new List<int>();
    }

    public static class StacklineProfileStore
    {
        public const int CurrentSchemaVersion = 3;
        public const int RecentScoreLimit = 8;
        public const string ProfileKey = "Wukong.StacklineClassic.Profile.V3";
        public const string DefaultLanguageCode = StacklineLocalization.ChineseCode;
        public const string LanguageKey = "Wukong.StacklineClassic.Language";

        private const string LegacyBestScoreKey = "Wukong.StacklineClassic.Best";
        private const string LegacyGemsKey = "Wukong.StacklineClassic.Gems";
        private const string LegacyLivesKey = "Wukong.StacklineClassic.Lives";

        public static StacklineProfileData Load()
        {
            StacklineProfileData profile = new StacklineProfileData();

            if (PlayerPrefs.HasKey(ProfileKey))
            {
                string json = PlayerPrefs.GetString(ProfileKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try
                    {
                        JsonUtility.FromJsonOverwrite(json, profile);
                    }
                    catch (ArgumentException)
                    {
                        profile = new StacklineProfileData();
                    }
                }
            }
            else
            {
                MigrateLegacyProfile(profile);
            }

            if (PlayerPrefs.HasKey(LanguageKey))
                profile.language = PlayerPrefs.GetString(LanguageKey, DefaultLanguageCode);

            profile = Normalize(profile);
            Save(profile);
            return profile;
        }

        public static void Save(StacklineProfileData profile)
        {
            StacklineProfileData normalized = Normalize(profile);
            string persistedLanguage = PlayerPrefs.GetString(LanguageKey, string.Empty);
            if (!string.IsNullOrEmpty(persistedLanguage))
                normalized.language = StacklineLocalization.NormalizeCode(persistedLanguage);
            PlayerPrefs.SetString(LanguageKey, normalized.language);
            PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(normalized));
            PlayerPrefs.Save();
        }

        public static string LoadLanguageCode()
        {
            if (PlayerPrefs.HasKey(LanguageKey))
                return StacklineLocalization.NormalizeCode(PlayerPrefs.GetString(LanguageKey, DefaultLanguageCode));

            if (PlayerPrefs.HasKey(ProfileKey))
            {
                try
                {
                    StacklineProfileData profile = JsonUtility.FromJson<StacklineProfileData>(
                        PlayerPrefs.GetString(ProfileKey, string.Empty));
                    if (profile != null)
                        return StacklineLocalization.NormalizeCode(profile.language);
                }
                catch (ArgumentException)
                {
                    // A corrupt profile falls back to the default language without blocking the HUD.
                }
            }
            return DefaultLanguageCode;
        }

        public static void SaveLanguageCode(string languageCode)
        {
            string normalizedCode = StacklineLocalization.NormalizeCode(languageCode);
            PlayerPrefs.SetString(LanguageKey, normalizedCode);

            if (PlayerPrefs.HasKey(ProfileKey))
            {
                try
                {
                    StacklineProfileData profile = JsonUtility.FromJson<StacklineProfileData>(
                        PlayerPrefs.GetString(ProfileKey, string.Empty));
                    profile = Normalize(profile);
                    profile.language = normalizedCode;
                    PlayerPrefs.SetString(ProfileKey, JsonUtility.ToJson(profile));
                }
                catch (ArgumentException)
                {
                    // Keep the dedicated language preference even if the main profile is corrupt.
                }
            }
            PlayerPrefs.Save();
        }

        public static StacklineProfileData Normalize(StacklineProfileData profile)
        {
            if (profile == null)
                profile = new StacklineProfileData();

            profile.schemaVersion = CurrentSchemaVersion;
            profile.bestScore = Mathf.Max(0, profile.bestScore);
            profile.gems = Mathf.Max(0, profile.gems);
            profile.lives = Mathf.Max(0, profile.lives);
            profile.stars = Mathf.Max(0, profile.stars);
            profile.runCount = Mathf.Max(0, profile.runCount);
            profile.language = StacklineLocalization.NormalizeCode(profile.language);

            profile.unlockedThemeMask &= int.MaxValue;
            profile.unlockedThemeMask |= 1;
            profile.selectedTheme = Mathf.Clamp(profile.selectedTheme, 0, 30);
            if ((profile.unlockedThemeMask & (1 << profile.selectedTheme)) == 0)
                profile.selectedTheme = 0;

            profile.challengeClaims &= int.MaxValue;
            if (profile.dailyBonusDay == null)
                profile.dailyBonusDay = string.Empty;

            if (profile.recentScores == null)
                profile.recentScores = new List<int>();

            for (int index = profile.recentScores.Count - 1; index >= 0; index--)
            {
                if (profile.recentScores[index] < 0)
                    profile.recentScores[index] = 0;
            }

            if (profile.recentScores.Count > RecentScoreLimit)
            {
                profile.recentScores.RemoveRange(
                    RecentScoreLimit,
                    profile.recentScores.Count - RecentScoreLimit);
            }

            return profile;
        }

        private static void MigrateLegacyProfile(StacklineProfileData profile)
        {
            if (PlayerPrefs.HasKey(LegacyBestScoreKey))
                profile.bestScore = PlayerPrefs.GetInt(LegacyBestScoreKey, 0);
            if (PlayerPrefs.HasKey(LegacyGemsKey))
                profile.gems = PlayerPrefs.GetInt(LegacyGemsKey, 2);
            if (PlayerPrefs.HasKey(LegacyLivesKey))
                profile.lives = PlayerPrefs.GetInt(LegacyLivesKey, 0);
        }
    }
}
