using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace L4D2EightPlayer.Core
{
    /// <summary>The color key for add-ons, safest first.</summary>
    public enum Rating { Safe, Stable, Moderate, Unstable, Unsafe }

    /// <summary>One add-on's rating, and why.</summary>
    public sealed class Rated
    {
        public Rating Rating;
        /// <summary>The add-ons that are on and really clash with it (different copies of the same files).</summary>
        public List<string> Conflicts = new List<string>();
        /// <summary>What it changes beyond looks: campaign, game mode, gameplay scripts, weapon stats, dialogue, animations.</summary>
        public List<string> Requirements = new List<string>();
        /// <summary>Why it can't be used, for Unsafe.</summary>
        public string Unusable;
        /// <summary>Whether it's on. One that's off is rated as it would be if it were turned on.</summary>
        public bool On = true;

        public string Name => Ratings.Name(Rating);
        /// <summary>"Moderate: conflicts with HUD Mod; changes gameplay scripts".</summary>
        public string Why
        {
            get
            {
                if (Rating == Rating.Unsafe) return Unusable ?? "can't be used right now";
                var parts = new List<string>();
                if (Conflicts.Count > 0) parts.Add((On ? "conflicts with " : "would conflict with ") + Join(Conflicts));
                if (Requirements.Count > 0) parts.Add("changes " + Join(Requirements));
                return parts.Count == 0 ? "no conflicts, and it only changes how things look or sound" : string.Join("; ", parts);
            }
        }

        static string Join(List<string> l) => l.Count <= 3 ? string.Join(", ", l) : string.Join(", ", l.Take(3)) + $" and {l.Count - 3} more";
    }

    /// <summary>
    /// The color key, from conflicts (other add-ons that are on and replace the same files with different copies)
    /// and requirements (what it changes beyond looks: a campaign, a game mode, gameplay scripts, weapon stats,
    /// dialogue, animations):
    ///   Safe (blue): no conflicts, no requirements.
    ///   Stable (green): no conflicts, at least one requirement.
    ///   Moderate (yellow): at least one conflict.
    ///   Unstable (orange): more than 2 conflicts, or more than 2 requirements.
    ///   Unsafe (red): can't be used right now (unreadable, or the compatibility check found a problem), whatever else.
    /// Harmless overlaps (the same copy of a file in both) aren't conflicts.
    /// </summary>
    public static class Ratings
    {
        public static string Name(Rating r) => r.ToString();

        public static string Meaning(Rating r)
        {
            switch (r)
            {
                case Rating.Safe: return "No conflicts, and it only changes how things look or sound.";
                case Rating.Stable: return "No conflicts. It changes gameplay or animations (a campaign, a game mode, scripts, weapon stats, dialogue).";
                case Rating.Moderate: return "Conflicts with at least one other add-on that's on: the game uses only one copy of the files they share.";
                case Rating.Unstable: return "More than 2 conflicts, or it changes more than 2 kinds of gameplay or animations.";
                default: return "Can't be used right now: it's unreadable, or the compatibility check found a problem with it.";
            }
        }

        /// <summary>What an add-on changes beyond looks. A campaign's own scripts and dialogue are part of the campaign.</summary>
        public static List<string> Requirements(Compat.AddonInfo info)
        {
            var r = new List<string>();
            if (info == null || info.Error != null) return r;
            if (info.Campaign) r.Add("a campaign");
            if (info.Mutation) r.Add("a game mode");
            if (info.Gameplay && !info.Campaign) r.Add("gameplay scripts");
            if (info.WeaponScripts) r.Add("weapon stats");
            if (info.Talker && !info.Campaign) r.Add("dialogue");
            if (info.Files.Any(IsAnimation)) r.Add("animations");
            return r;
        }

        static bool IsAnimation(string file) => file.StartsWith("models/") && Path.GetFileName(file).StartsWith("anim_", StringComparison.OrdinalIgnoreCase) && file.EndsWith(".mdl");

        /// <summary>
        /// Every add-on of a survey, by its path. unusable gives the compatibility check's reason an add-on can't be
        /// used (a problem it found), or null.
        /// </summary>
        public static Dictionary<string, Rated> Rate(Clashes.Survey survey, Func<string, string> unusable = null)
        {
            var result = new Dictionary<string, Rated>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in survey.Addons)
            {
                var r = new Rated
                {
                    Conflicts = survey.ConflictsOf(a).Select(o => o.Title).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                    Requirements = Requirements(a.Info),
                    On = a.Enabled,
                    Unusable = a.Info?.Error != null ? "the file can't be read (" + a.Info.Error + "); re-subscribe or delete it" : unusable?.Invoke(a.Path),
                };
                r.Rating = Classify(r.Conflicts.Count, r.Requirements.Count, r.Unusable != null);
                result[a.Path] = r;
            }
            return result;
        }

        public static Rating Classify(int conflicts, int requirements, bool unusable) =>
            unusable ? Rating.Unsafe
            : conflicts > 2 || requirements > 2 ? Rating.Unstable
            : conflicts >= 1 ? Rating.Moderate
            : requirements >= 1 ? Rating.Stable
            : Rating.Safe;
    }
}
