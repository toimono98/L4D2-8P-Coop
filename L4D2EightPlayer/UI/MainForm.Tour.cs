using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.UI
{
    /// <summary>The first-run tour: a few stops over the real window, from picking a server type to letting friends in.</summary>
    public sealed partial class MainForm
    {
        /// <summary>--tour (the Setup's "Show me around"): start the tour once the window is up.</summary>
        public static bool TourRequested;
        /// <summary>--no-tour: the Setup's box was unticked, so don't start it on a first run either.</summary>
        public static bool TourDeclined;
        /// <summary>No settings file yet: someone opening the Manager for the first time.</summary>
        readonly bool firstRun = !File.Exists(Paths.SettingsFile);
        Tour tour;
        Control modeRow, routeRow;

        async Task MaybeStartTour()
        {
            if (Preview || TourDeclined || !(TourRequested || (firstRun && !settings.TourSeen))) return;
            await Task.Delay(700);   // let the first layout and the startup checks settle
            if (!IsDisposed) StartTour();
        }

        void StartTour()
        {
            if (tour != null && tour.Running) return;
            settings.TourSeen = true;
            if (!Preview) settings.Save();
            tour = BuildTour();
            tour.Start();
        }

        static Control CardOf(Control c) { while (c != null && !(c is Card)) c = c.Parent; return c; }

        internal Tour BuildTour()
        {
            void Tab(int i) => tabs.SelectedIndex = i;
            return new Tour(this, new[]
            {
                new TourStep
                {
                    Before = () => Tab(0),
                    Title = "Welcome to the safe room",
                    Body = "This quick tour shows the few things you need to get 8 survivors into one game. " +
                           "Next (or the → key) moves on and Esc skips it.",
                },
                new TourStep
                {
                    Before = () => Tab(0),
                    Targets = () => new[] { modeRow },
                    Title = "How you host",
                    Body = "Dedicated server is the one to pick: it runs as its own program, so your game stays VAC-secure and up to 16 can play. " +
                           "Host from my game skips the ~10 GB download, but tops out at 8 and turns VAC off in your game while you host.",
                },
                new TourStep
                {
                    Before = () => Tab(0),
                    Targets = () => new Control[] { btnInstall.Parent },
                    Title = "Get the server and mods",
                    Body = "Download & install fetches the server and every ticked mod in the list above, backing up anything it changes. " +
                           "Required items stay ticked; highlight a row to read what it does. Check for updates every so often.",
                },
                new TourStep
                {
                    Before = () => Tab(1),
                    Targets = () => new Control[] { tileProblems.Parent },
                    Title = "Check your mods",
                    Body = "Scan now looks through your add-ons and plugins for anything that breaks with more than 4 survivors. " +
                           "Red is a problem: pick it in the list and the button on the right fixes it. New mods are checked as soon as they appear.",
                },
                new TourStep
                {
                    Before = () => Tab(2),
                    Targets = () => new[] { CardOf(chkFill), CardOf(numPlayers), CardOf(cmbItemsMode), CardOf(cmbInfectedMode) },
                    Title = "Set it up for your group",
                    Body = "Bot fill keeps every empty slot filled with a bot that a friend takes over when they join. " +
                           "More items and Harder infected scale the supplies and the infected to the number of survivors; Auto suits most groups.",
                },
                new TourStep
                {
                    Before = () => Tab(2),
                    Targets = () => new Control[] { btnStart.Parent.Parent.Parent },
                    Title = "Start and invite",
                    Body = "Start server brings it up, and the survivor cards go live as people join. " +
                           "Copy the line under Friends join with and send it: friends paste it into the Join app or open it in Steam.",
                },
                new TourStep
                {
                    Before = () => Tab(3),
                    Targets = () => new[] { routeRow },
                    Title = "Let friends in",
                    Body = "Friends outside your home need a way through your router. This tab checks each step of the way; Open port automatically does the router part for you, and Test from outside proves it works. " +
                           "Take this tour again any time from TOUR at the top right.",
                },
            });
        }
    }
}
