namespace Wukong.StacklineClassic
{
    public enum StacklineLanguage
    {
        Chinese,
        English,
    }

    public enum StacklineText
    {
        GameTitle,
        TapToStart,
        NewRecord,
        Best,
        Skin,
        Leaderboard,
        Bonus,
        AdMultiplierIcon,
        Lives,
        ContinuePrompt,
        ThreeSecondRescue,
        UseLife,
        NoLives,
        EndRun,
        Rescue,
        Perfect,
        Settings,
        Sound,
        Haptics,
        Language,
        Chinese,
        English,
        On,
        Off,
        RulesDescription,
        Challenges,
        Claimed,
        ClaimStar,
        Locked,
        Skins,
        Selected,
        Owned,
        AdBonus,
        AdFreeSummary,
        OfflineDemoSummary,
        Active,
        UnlockDouble,
        DailyClaimed,
        ClaimDaily,
        AdDisclaimer,
        OneLife,
        ThreeLives,
        LifeDescription,
    }

    public static class StacklineLocalization
    {
        public const string ChineseCode = "zh-CN";
        public const string EnglishCode = "en";

        public static StacklineLanguage FromCode(string code)
        {
            return NormalizeCode(code) == EnglishCode ? StacklineLanguage.English : StacklineLanguage.Chinese;
        }

        public static string ToCode(StacklineLanguage language)
        {
            return language == StacklineLanguage.English ? EnglishCode : ChineseCode;
        }

        public static string NormalizeCode(string code)
        {
            return string.Equals(code, EnglishCode, System.StringComparison.OrdinalIgnoreCase)
                ? EnglishCode
                : ChineseCode;
        }

        public static string Get(StacklineLanguage language, StacklineText text)
        {
            bool chinese = language == StacklineLanguage.Chinese;
            switch (text)
            {
                case StacklineText.GameTitle: return Pick(chinese, "\u53d6\u8d70\u9ec4\u91d1", "TAKE THE GOLD");
                case StacklineText.TapToStart: return Pick(chinese, "\u70b9\u51fb\u53d6\u8d70\u9ec4\u91d1", "TAP TO TAKE GOLD");
                case StacklineText.NewRecord: return Pick(chinese, "\u65b0\u7eaa\u5f55", "NEW RECORD");
                case StacklineText.Best: return Pick(chinese, "\u6700\u4f73", "BEST");
                case StacklineText.Skin: return Pick(chinese, "\u91d1\u5e93", "VAULT");
                case StacklineText.Leaderboard: return Pick(chinese, "\u6392\u884c\u699c", "LEADERBOARD");
                case StacklineText.Bonus: return Pick(chinese, "\u5956\u52b1", "BONUS");
                case StacklineText.AdMultiplierIcon: return Pick(chinese, "\u5956\u52b1\nx2", "AD\nx2");
                case StacklineText.Lives: return Pick(chinese, "\u751f\u547d", "LIVES");
                case StacklineText.ContinuePrompt: return Pick(chinese, "\u7ee7\u7eed\uff1f", "CONTINUE?");
                case StacklineText.ThreeSecondRescue: return Pick(chinese, "3 \u79d2\u6551\u63f4", "3 SEC RESCUE");
                case StacklineText.UseLife: return Pick(chinese, "\u4f7f\u7528\u751f\u547d", "USE LIFE");
                case StacklineText.NoLives: return Pick(chinese, "\u65e0\u53ef\u7528\u751f\u547d", "NO LIVES");
                case StacklineText.EndRun: return Pick(chinese, "\u7ed3\u675f\u672c\u5c40", "END RUN");
                case StacklineText.Rescue: return Pick(chinese, "\u6551\u63f4", "RESCUE");
                case StacklineText.Perfect: return Pick(chinese, "\u5b8c\u7f8e", "PERFECT");
                case StacklineText.Settings: return Pick(chinese, "\u8bbe\u7f6e", "SETTINGS");
                case StacklineText.Sound: return Pick(chinese, "\u58f0\u97f3", "SOUND");
                case StacklineText.Haptics: return Pick(chinese, "\u9707\u52a8", "HAPTICS");
                case StacklineText.Language: return Pick(chinese, "\u8bed\u8a00", "LANGUAGE");
                case StacklineText.Chinese: return "\u4e2d\u6587";
                case StacklineText.English: return "ENGLISH";
                case StacklineText.On: return Pick(chinese, "\u5f00", "ON");
                case StacklineText.Off: return Pick(chinese, "\u5173", "OFF");
                case StacklineText.RulesDescription:
                    return Pick(chinese,
                        "\u5bf9\u9f50\u91d1\u7816\uff0c\u4fdd\u4f4f\u91cd\u53e0\u90e8\u5206\u3002\u8fde\u7eed\u5b8c\u7f8e\u5bf9\u9f50\u53ef\u83b7\u5f97\u66f4\u591a\u9ec4\u91d1\u3002",
                        "Align each ingot and keep the overlap. Perfect streaks earn more gold.");
                case StacklineText.Challenges: return Pick(chinese, "\u6311\u6218", "CHALLENGES");
                case StacklineText.Claimed: return Pick(chinese, "\u5df2\u9886\u53d6", "CLAIMED");
                case StacklineText.ClaimStar: return Pick(chinese, "\u9886\u53d6  +1 \u661f\u661f", "CLAIM  +1 STAR");
                case StacklineText.Locked: return Pick(chinese, "\u672a\u5b8c\u6210", "LOCKED");
                case StacklineText.Skins: return Pick(chinese, "\u9ec4\u91d1\u6536\u85cf", "GOLD VAULT");
                case StacklineText.Selected: return Pick(chinese, "\u5df2\u9009\u62e9", "SELECTED");
                case StacklineText.Owned: return Pick(chinese, "\u5df2\u62e5\u6709", "OWNED");
                case StacklineText.AdBonus: return Pick(chinese, "\u5956\u52b1", "AD BONUS");
                case StacklineText.AdFreeSummary:
                    return Pick(chinese, "\u53bb\u5e7f\u544a\u5df2\u542f\u7528\n\u9ad8\u5ea6\u5956\u52b1 x2", "AD-FREE ACTIVE\nHEIGHT REWARDS x2");
                case StacklineText.OfflineDemoSummary:
                    return Pick(chinese, "\u79bb\u7ebf\u6f14\u793a\n\u4e0d\u8fde\u63a5\u5e7f\u544a\u6216\u8d2d\u4e70\u670d\u52a1", "OFFLINE DEMO\nNO NETWORK OR PURCHASE SDK");
                case StacklineText.Active: return Pick(chinese, "\u5df2\u542f\u7528", "ACTIVE");
                case StacklineText.UnlockDouble: return Pick(chinese, "\u89e3\u9501 x2  \u25c6 5", "UNLOCK x2  \u25c6 5");
                case StacklineText.DailyClaimed: return Pick(chinese, "\u4eca\u65e5\u5956\u52b1\u5df2\u9886\u53d6", "DAILY BONUS CLAIMED");
                case StacklineText.ClaimDaily: return Pick(chinese, "\u9886\u53d6\u4eca\u65e5\u5956\u52b1  +2 \u25c6", "CLAIM DAILY  +2 \u25c6");
                case StacklineText.AdDisclaimer:
                    return Pick(chinese,
                        "\u8fd9\u662f\u786e\u5b9a\u6027\u7684\u672c\u5730\u6a21\u62df\uff0c\u4e0d\u4f1a\u8fde\u63a5\u5e7f\u544a\u6216\u652f\u4ed8\u670d\u52a1\u3002",
                        "This is a deterministic local simulation. It does not contact an ad or payment service.");
                case StacklineText.OneLife: return Pick(chinese, "1 \u751f\u547d     \u25c6 3", "1 LIFE     \u25c6 3");
                case StacklineText.ThreeLives: return Pick(chinese, "3 \u751f\u547d     \u25c6 8", "3 LIVES    \u25c6 8");
                case StacklineText.LifeDescription:
                    return Pick(chinese,
                        "\u5931\u8bef\u540e\uff0c\u751f\u547d\u53ef\u6062\u590d\u5230\u4e0a\u4e00\u4e2a\u5b89\u5168\u5c42\u3002",
                        "A life restores the last safe floor after a miss.");
                default: return string.Empty;
            }
        }

        public static string LocalizeChallenge(string source, StacklineLanguage language)
        {
            if (language == StacklineLanguage.English || string.IsNullOrEmpty(source))
                return source;

            const string prefix = "REACH ";
            const string suffix = " FLOORS";
            if (source.StartsWith(prefix) && source.EndsWith(suffix))
            {
                string amount = source.Substring(prefix.Length, source.Length - prefix.Length - suffix.Length);
                return "\u8fbe\u5230 " + amount + " \u5c42";
            }
            return source;
        }

        public static string LocalizeTheme(string source, StacklineLanguage language)
        {
            if (language == StacklineLanguage.English || string.IsNullOrEmpty(source))
                return source;

            switch (source)
            {
                case "BULLION": return "\u6807\u51c6\u91d1";
                case "ANTIQUE": return "\u53e4\u91d1";
                case "ROYAL": return "\u7687\u5bb6\u91d1";
                case "SUNLIT": return "\u66dc\u65e5\u91d1";
                case "ROSE GOLD": return "\u73ab\u7470\u91d1";
                case "WHITE GOLD": return "\u767d\u91d1";
                default: return source;
            }
        }

        public static string LocalizeLeaderboard(string source, StacklineLanguage language)
        {
            if (language == StacklineLanguage.English || string.IsNullOrEmpty(source))
                return source;
            return source.Replace("GOLD HUNTER", "\u6dd8\u91d1\u5ba2").Replace("YOU", "\u4f60");
        }

        public static string LocalizeMessage(string source, StacklineLanguage language)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            const string singularLifeSuffix = " LIFE";
            const string pluralLifeSuffix = " LIVES";
            string lifeSuffix = source.EndsWith(pluralLifeSuffix) ? pluralLifeSuffix
                : source.EndsWith(singularLifeSuffix) ? singularLifeSuffix
                : null;
            if (source.StartsWith("+") && lifeSuffix != null)
            {
                string amount = source.Substring(1, source.Length - lifeSuffix.Length - 1);
                if (language == StacklineLanguage.Chinese)
                    return "+" + amount + " \u751f\u547d";
                return "+" + amount + (amount == "1" ? " LIFE" : " LIVES");
            }

            if (language == StacklineLanguage.English)
                return source;

            switch (source)
            {
                case "ALREADY CLAIMED": return "\u5df2\u9886\u53d6";
                case "KEEP STACKING": return "\u7ee7\u7eed\u5806\u53e0";
                case "+1 STAR": return "+1 \u661f\u661f";
                case "UNAVAILABLE": return "\u6682\u4e0d\u53ef\u7528";
                case "AD-FREE ACTIVE": return "\u53bb\u5e7f\u544a\u5df2\u542f\u7528";
                case "HEIGHT REWARDS x2": return "\u9ad8\u5ea6\u5956\u52b1 x2";
                case "COME BACK TOMORROW": return "\u660e\u5929\u518d\u6765";
                case "+2 GOLD": return "+2 \u9ec4\u91d1";
                case "NOT ENOUGH GOLD": return "\u9ec4\u91d1\u4e0d\u8db3";
            }

            const string needPrefix = "NEED ";
            const string goldSuffix = " GOLD";
            if (source.StartsWith(needPrefix) && source.EndsWith(goldSuffix))
            {
                string amount = source.Substring(needPrefix.Length, source.Length - needPrefix.Length - goldSuffix.Length);
                return "\u9700\u8981 " + amount + " \u9ec4\u91d1";
            }

            const string selectedSuffix = " SELECTED";
            if (source.EndsWith(selectedSuffix))
            {
                string theme = source.Substring(0, source.Length - selectedSuffix.Length);
                return LocalizeTheme(theme, language) + " " + Get(language, StacklineText.Selected);
            }

            return source;
        }

        private static string Pick(bool chinese, string chineseText, string englishText)
        {
            return chinese ? chineseText : englishText;
        }
    }
}
