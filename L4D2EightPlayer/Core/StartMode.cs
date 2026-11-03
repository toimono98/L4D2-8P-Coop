using System;
using System.IO;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Which side the app opens on: "host" (set up and run the server) or "join" (join a friend's).
    /// The Setup writes the path the user picked; the app writes it again whenever they switch, so it
    /// opens where they left off. Kept in %LOCALAPPDATA%\L4D2-8P-Manager\mode.txt.
    /// </summary>
    public static class StartMode
    {
        public const string Host = "host", Join = "join";
        static string FilePath => Path.Combine(Paths.AppData, "mode.txt");

        /// <summary>The saved mode, or null when nothing has been chosen yet.</summary>
        public static string Load()
        {
            try { return Normal(File.ReadAllText(FilePath)); }
            catch { return null; }
        }

        public static void Save(string mode)
        {
            mode = Normal(mode);
            if (mode == null) return;
            try { File.WriteAllText(FilePath, mode); } catch { /* the app still opens on the default */ }
        }

        static string Normal(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            return s == Host || s == Join ? s : null;
        }
    }
}
