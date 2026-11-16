using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;

namespace L4D2EightPlayer.UI
{
    /// <summary>
    /// The loading screen at startup (the Safe room wall design): shown before the window is built, moved on by
    /// each side's real startup steps (Step), and closed once the window is ready (Finish). It never holds the app
    /// up: slow parts (a first picture build, an update download) get a short wait and carry on behind it, and it
    /// closes on its own after 25 seconds whatever happens. Tests and screenshot checks never show it.
    /// </summary>
    static class Startup
    {
        public static readonly string[] HostSteps =
        {
            "Reading the component list", "Finding Left 4 Dead 2", "Looking for the dedicated server", "Loading custom campaigns",
            "Checking your mods", "Reading your skins", "Checking for updates", "Ready",
        };
        public static readonly string[] JoinSteps =
        {
            "Finding Left 4 Dead 2", "Checking your add-ons", "Reading your mods", "Reading your skins", "Checking for updates", "Ready",
        };

        static SplashScreen splash;
        static TaskCompletionSource<bool> done = Completed();
        static Timer safety;

        static TaskCompletionSource<bool> Completed() { var t = new TaskCompletionSource<bool>(); t.SetResult(true); return t; }

        /// <summary>Whether the loading screen is up (the startup steps report to it).</summary>
        public static bool Active => splash != null;
        /// <summary>Done once the loading screen has gone (the first-run tour waits for it).</summary>
        public static Task Finished => done.Task;

        /// <summary>Shows the loading screen for the side the app opens on. Call before the window is built.</summary>
        public static void Begin(string mode)
        {
            try
            {
                bool join = mode == StartMode.Join;
                splash = new SplashScreen(SplashStyle.SafeRoomWall) { Steps = join ? JoinSteps : HostSteps, Side = join ? "JOIN" : "HOST", TopMost = true };
                done = new TaskCompletionSource<bool>();
                splash.Show();
                splash.Update();   // drawn now: the window is built next, before the message loop runs
                safety = new Timer { Interval = 25000 };
                safety.Tick += (s, e) => Finish();
                safety.Start();
            }
            catch { splash = null; done.TrySetResult(true); }   // no loading screen is better than no app
        }

        /// <summary>Step i is under way (the ones before it are done).</summary>
        public static void Step(int i)
        {
            if (splash == null || splash.IsDisposed) return;
            splash.Set(i);
            splash.Update();
        }

        /// <summary>Everything's ready: the wall fills up, then fades out over the window.</summary>
        public static async void Finish()
        {
            var s = splash;
            splash = null;
            safety?.Stop();
            safety?.Dispose();
            safety = null;
            if (s == null || s.IsDisposed) { done.TrySetResult(true); return; }
            try
            {
                s.Set(s.Steps.Length);
                s.Update();
                await Task.Delay(450);   // long enough to see it ready
                for (double o = 1; o > 0 && !s.IsDisposed; o -= 0.2) { s.Opacity = o; await Task.Delay(25); }
                s.Close();
                s.Dispose();
            }
            catch { }
            done.TrySetResult(true);
            try { AppWindow.Instance?.Activate(); } catch { }
        }

        /// <summary>Waits for a task, but no longer than the loading screen should (the rest carries on behind it).</summary>
        public static async Task Briefly(Task t, int ms)
        {
            if (t == null) return;
            if (!Active) { await t; return; }
            await Task.WhenAny(t, Task.Delay(ms));
        }
    }
}
