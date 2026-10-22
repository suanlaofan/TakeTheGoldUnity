namespace Wukong.StacklineClassic
{
    public enum StacklineLanguage
    {
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
        TempleChallenge,
        RunComplete,
        RunHeight,
        BestHeight,
        CurrentHeight,
        StartRun,
        PlayAgain,
        StackHigher,
        PrecisionReward,
        TryAgain,
        PlacementHint,
    }

    public static class StacklineLocalization
    {
        public const string EnglishCode = "en";

        // The game ships English only; any language code from an older save normalizes to English.
        public static StacklineLanguage FromCode(string code)
        {
            return StacklineLanguage.English;
        }

        public static string ToCode(StacklineLanguage language)
        {
            return EnglishCode;
        }

        public static string NormalizeCode(string code)
        {
            return EnglishCode;
        }

        public static string Get(StacklineLanguage language, StacklineText text)
        {
            switch (text)
            {
                case StacklineText.GameTitle: return "TAKE THE GOLD";
                case StacklineText.TempleChallenge: return "THE GOLDEN TEMPLE";
                case StacklineText.RunComplete: return "RUN COMPLETE";
                case StacklineText.RunHeight: return "FLOORS STACKED";
                case StacklineText.BestHeight: return "PERSONAL BEST";
                case StacklineText.CurrentHeight: return "FLOORS STACKED";
                case StacklineText.StartRun: return "START STACKING";
                case StacklineText.PlayAgain: return "PLAY AGAIN";
                case StacklineText.StackHigher: return "ONE INGOT CLOSER TO THE TOP";
                case StacklineText.PrecisionReward: return "ALIGN. STACK. STRIKE GOLD.";
                case StacklineText.TryAgain: return "THE NEXT RECORD IS YOURS";
                case StacklineText.PlacementHint: return "Click or press the right trigger to drop an ingot.";
                case StacklineText.TapToStart: return "TAP TO TAKE GOLD";
                case StacklineText.NewRecord: return "NEW RECORD";
                case StacklineText.Best: return "BEST";
                case StacklineText.Skin: return "VAULT";
                case StacklineText.Leaderboard: return "LEADERBOARD";
                case StacklineText.Bonus: return "BONUS";
                case StacklineText.AdMultiplierIcon: return "AD\nx2";
                case StacklineText.Lives: return "LIVES";
                case StacklineText.ContinuePrompt: return "CONTINUE?";
                case StacklineText.ThreeSecondRescue: return "3 SEC RESCUE";
                case StacklineText.UseLife: return "USE LIFE";
                case StacklineText.NoLives: return "NO LIVES";
                case StacklineText.EndRun: return "END RUN";
                case StacklineText.Rescue: return "RESCUE";
                case StacklineText.Perfect: return "PERFECT";
                case StacklineText.Settings: return "SETTINGS";
                case StacklineText.Sound: return "SOUND";
                case StacklineText.Haptics: return "HAPTICS";
                case StacklineText.On: return "ON";
                case StacklineText.Off: return "OFF";
                case StacklineText.RulesDescription:
                    return "Align each ingot and keep the overlap. Perfect streaks earn more gold.";
                case StacklineText.Challenges: return "CHALLENGES";
                case StacklineText.Claimed: return "CLAIMED";
                case StacklineText.ClaimStar: return "CLAIM  +1 STAR";
                case StacklineText.Locked: return "LOCKED";
                case StacklineText.Skins: return "GOLD VAULT";
                case StacklineText.Selected: return "SELECTED";
                case StacklineText.Owned: return "OWNED";
                case StacklineText.AdBonus: return "AD BONUS";
                case StacklineText.AdFreeSummary:
                    return "AD-FREE ACTIVE\nHEIGHT REWARDS x2";
                case StacklineText.OfflineDemoSummary:
                    return "OFFLINE DEMO\nNO NETWORK OR PURCHASE SDK";
                case StacklineText.Active: return "ACTIVE";
                case StacklineText.UnlockDouble: return "UNLOCK x2  \u25c6 5";
                case StacklineText.DailyClaimed: return "DAILY BONUS CLAIMED";
                case StacklineText.ClaimDaily: return "CLAIM DAILY  +2 \u25c6";
                case StacklineText.AdDisclaimer:
                    return "This is a deterministic local simulation. It does not contact an ad or payment service.";
                case StacklineText.OneLife: return "1 LIFE     \u25c6 3";
                case StacklineText.ThreeLives: return "3 LIVES    \u25c6 8";
                case StacklineText.LifeDescription:
                    return "A life restores the last safe floor after a miss.";
                default: return string.Empty;
            }
        }

        public static string LocalizeChallenge(string source, StacklineLanguage language)
        {
            return source;
        }

        public static string LocalizeTheme(string source, StacklineLanguage language)
        {
            return source;
        }

        public static string LocalizeLeaderboard(string source, StacklineLanguage language)
        {
            return source;
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
                return "+" + amount + (amount == "1" ? " LIFE" : " LIVES");
            }

            return source;
        }
    }
}
