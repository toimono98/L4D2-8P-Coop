using System;
using System.IO;
using System.Text;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// --server-sync-test &lt;report&gt;: GameLocator.CompareServer on a throwaway game and server: the same files, the game
    /// patched without a new version number (as in November 2026), the server ahead of the game, and a missing file.
    /// </summary>
    static class ServerSyncTest
    {
        public static int Run(string report)
        {
            var log = new StringBuilder();
            bool ok = true;
            void Check(string what, bool good) { log.AppendLine((good ? "ok   " : "FAIL ") + what); ok &= good; }
            var root = Path.Combine(Path.GetTempPath(), "l4d2-8p-server-sync");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                var game = Path.Combine(root, "game");
                var server = Path.Combine(root, "server");
                void Put(string dir, string rel, string text, DateTime when)
                {
                    var p = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllText(p, text);
                    File.SetLastWriteTimeUtc(p, when);
                }
                var oct = new DateTime(2026, 10, 23, 17, 31, 0, DateTimeKind.Utc);
                var nov = new DateTime(2026, 11, 24, 21, 22, 0, DateTimeKind.Utc);
                Put(game, "left4dead2.exe", "exe", oct);
                Put(server, "srcds.exe", "exe", oct);
                foreach (var d in new[] { game, server })
                {
                    Put(d, @"left4dead2\bin\server.dll", "server code A", oct);
                    Put(d, @"bin\engine.dll", "engine code A", oct);
                }
                Check("the same files: Same", GameLocator.CompareServer(game, server) == GameLocator.ServerSync.Same);

                // Steam patches the game: one file changes (same length, different bytes), the version number doesn't
                Put(game, @"left4dead2\bin\server.dll", "server code B", nov);
                Check("the game patched, the server not: ServerBehind", GameLocator.CompareServer(game, server) == GameLocator.ServerSync.ServerBehind);

                // SteamCMD brings the server level
                Put(server, @"left4dead2\bin\server.dll", "server code B", nov.AddMinutes(9));
                Check("the server updated to the same files: Same", GameLocator.CompareServer(game, server) == GameLocator.ServerSync.Same);

                // the server patched before Steam got round to the game
                Put(server, @"bin\engine.dll", "engine code C, longer", nov.AddHours(2));
                Check("the server ahead of the game: GameBehind", GameLocator.CompareServer(game, server) == GameLocator.ServerSync.GameBehind);

                File.Delete(Path.Combine(server, @"bin\engine.dll"));
                Check("a file missing: Unknown", GameLocator.CompareServer(game, server) == GameLocator.ServerSync.Unknown);
                Check("no server folder: Unknown", GameLocator.CompareServer(game, Path.Combine(root, "nowhere")) == GameLocator.ServerSync.Unknown);
            }
            catch (Exception ex) { Check("no exception: " + ex, false); }
            finally { try { Directory.Delete(root, true); } catch { } }
            File.WriteAllText(report, log.ToString());
            return ok ? 0 : 1;
        }
    }
}
