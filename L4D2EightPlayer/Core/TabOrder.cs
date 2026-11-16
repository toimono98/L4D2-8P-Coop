using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The order of the tabs along the top, one list for each side (tabs.json). Each tab keeps its own page; only
    /// where it sits in the row changes. A stored list is checked against the tabs there are, so a tab added in a
    /// later version shows up (at the end) and one taken away is dropped.
    /// </summary>
    public static class TabOrder
    {
        public enum Side { Host, Join }

        /// <summary>The tabs as the pages are built (their numbers are MainForm.Tab* / JoinForm.Tab*).</summary>
        public static string[] Keys(Side side) => side == Side.Host
            ? new[] { "setup", "mods", "skins", "play", "settings" }
            : new[] { "join", "mods", "skins", "settings" };

        /// <summary>The order a new user sees: play first, then skins and mods, settings last.</summary>
        public static string[] Default(Side side) => side == Side.Host
            ? new[] { "play", "skins", "mods", "setup", "settings" }
            : new[] { "join", "skins", "mods", "settings" };

        public sealed class Stored
        {
            public List<string> Host { get; set; }
            public List<string> Join { get; set; }
        }

        static string FilePath => Path.Combine(Paths.AppData, "tabs.json");

        static Stored Load() { try { return Json.Load<Stored>(FilePath); } catch { return new Stored(); } }

        /// <summary>The tabs in their order along the row, as page numbers.</summary>
        public static int[] Get(Side side)
        {
            var s = Load();
            return Indexes(side, side == Side.Host ? s.Host : s.Join);
        }

        /// <summary>A list of names made whole: unknown ones and repeats dropped, missing tabs added where the default puts them.</summary>
        public static int[] Indexes(Side side, IEnumerable<string> names)
        {
            var keys = Keys(side);
            var order = new List<int>();
            foreach (var n in names ?? Enumerable.Empty<string>())
            {
                int i = Array.FindIndex(keys, k => string.Equals(k, n, StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && !order.Contains(i)) order.Add(i);
            }
            foreach (var k in Default(side))
            {
                int i = Array.IndexOf(keys, k);
                if (!order.Contains(i)) order.Add(i);
            }
            return order.ToArray();
        }

        /// <summary>The tab's name as the row shows it ("Play").</summary>
        public static string Title(Side side, int i) { var k = Keys(side)[i]; return char.ToUpperInvariant(k[0]) + k.Substring(1); }

        public static bool IsDefault(Side side, int[] order) => order.SequenceEqual(Indexes(side, null));

        /// <summary>Remembers the order (null goes back to the default).</summary>
        public static void Save(Side side, int[] order)
        {
            if (Settings.ReadOnly) return;
            var s = Load();
            var names = order == null || IsDefault(side, order) ? null : order.Select(i => Keys(side)[i]).ToList();
            if (side == Side.Host) s.Host = names; else s.Join = names;
            try { Json.Save(FilePath, s); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SafeFile.Note("Saving the tab order", ex); }
        }
    }
}
