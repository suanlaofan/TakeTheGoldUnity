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
        private static StacklineProfileData lastSavedProfile;
        private static string lastSavedJson;

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
            string persistedJson = PlayerPrefs.GetString(ProfileKey, string.Empty);
            if (persistedLanguage == normalized.language && persistedJson == lastSavedJson &&
                ProfilesMatch(normalized, lastSavedProfile))
                return;

            string json = JsonUtility.ToJson(normalized);
            bool languageChanged = persistedLanguage != normalized.language;
            bool profileChanged = persistedJson != json;
            if (languageChanged)
                PlayerPrefs.SetString(LanguageKey, normalized.language);
            if (profileChanged)
                PlayerPrefs.SetString(ProfileKey, json);
            // Real score/resource/settings changes still flush synchronously. No pending
            // transaction is left behind for pause, quit, or an unexpected process stop.
            if (languageChanged || profileChanged)
                PlayerPrefs.Save();
            RememberSavedProfile(normalized, json);
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
            string persistedLanguage = PlayerPrefs.GetString(LanguageKey, string.Empty);
            string persistedJson = PlayerPrefs.GetString(ProfileKey, string.Empty);
            if (persistedLanguage == normalizedCode && persistedJson == lastSavedJson &&
                lastSavedProfile != null && lastSavedProfile.language == normalizedCode)
                return;

            bool changed = persistedLanguage != normalizedCode;
            if (changed)
                PlayerPrefs.SetString(LanguageKey, normalizedCode);

            if (PlayerPrefs.HasKey(ProfileKey))
            {
                try
                {
                    StacklineProfileData profile = JsonUtility.FromJson<StacklineProfileData>(persistedJson);
                    profile = Normalize(profile);
                    profile.language = normalizedCode;
                    string json = JsonUtility.ToJson(profile);
                    if (json != persistedJson)
                    {
                        PlayerPrefs.SetString(ProfileKey, json);
                        changed = true;
                    }
                    RememberSavedProfile(profile, json);
                }
                catch (ArgumentException)
                {
                    // Keep the dedicated language preference even if the main profile is corrupt.
                    lastSavedProfile = null;
                    lastSavedJson = null;
                }
            }
            else
            {
                lastSavedProfile = null;
                lastSavedJson = null;
            }
            if (changed)
                PlayerPrefs.Save();
        }

        private static bool ProfilesMatch(StacklineProfileData current, StacklineProfileData saved)
        {
            // Compare every persisted field, including list contents: the controller mutates
            // a single profile in place, so reference equality cannot detect new rewards.
            if (saved == null || current.schemaVersion != saved.schemaVersion ||
                current.bestScore != saved.bestScore || current.gems != saved.gems ||
                current.lives != saved.lives || current.stars != saved.stars ||
                current.selectedTheme != saved.selectedTheme || current.unlockedThemeMask != saved.unlockedThemeMask ||
                current.adFree != saved.adFree || current.doubleGems != saved.doubleGems ||
                current.soundEnabled != saved.soundEnabled || current.hapticsEnabled != saved.hapticsEnabled ||
                current.language != saved.language || current.challengeClaims != saved.challengeClaims ||
                current.runCount != saved.runCount || current.dailyBonusDay != saved.dailyBonusDay ||
                current.recentScores.Count != saved.recentScores.Count)
                return false;
            for (int index = 0; index < current.recentScores.Count; index++)
                if (current.recentScores[index] != saved.recentScores[index])
                    return false;
            return true;
        }

        private static void RememberSavedProfile(StacklineProfileData profile, string json)
        {
            if (lastSavedProfile == null)
                lastSavedProfile = new StacklineProfileData();
            // Keep this copy/comparison pair aligned with StacklineProfileData when adding
            // schema fields. The snapshot must never share the caller's mutable score list.
            lastSavedProfile.schemaVersion = profile.schemaVersion;
            lastSavedProfile.bestScore = profile.bestScore;
            lastSavedProfile.gems = profile.gems;
            lastSavedProfile.lives = profile.lives;
            lastSavedProfile.stars = profile.stars;
            lastSavedProfile.selectedTheme = profile.selectedTheme;
            lastSavedProfile.unlockedThemeMask = profile.unlockedThemeMask;
            lastSavedProfile.adFree = profile.adFree;
            lastSavedProfile.doubleGems = profile.doubleGems;
            lastSavedProfile.soundEnabled = profile.soundEnabled;
            lastSavedProfile.hapticsEnabled = profile.hapticsEnabled;
            lastSavedProfile.language = profile.language;
            lastSavedProfile.challengeClaims = profile.challengeClaims;
            lastSavedProfile.runCount = profile.runCount;
            lastSavedProfile.dailyBonusDay = profile.dailyBonusDay;
            lastSavedProfile.recentScores.Clear();
            lastSavedProfile.recentScores.AddRange(profile.recentScores);
            lastSavedJson = json;
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
