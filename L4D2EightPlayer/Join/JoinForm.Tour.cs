using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.UI.Themes;

namespace L4D2EightPlayer.Join
{
    /// <summary>The first-run tour: paste the invite, check the server, join, and where the rest lives.</summary>
    public sealed partial class JoinForm
    {
        /// <summary>--tour (the Setup's "Show me around"): start the tour once the window is up.</summary>
        public static bool TourRequested;
        /// <summary>--no-tour: the Setup's box was unticked, so don't start it on a first run either.</summary>
        public static bool TourDeclined;
        /// <summary>Set by the screenshot check: no tour on its own and nothing saved.</summary>
        public static bool Preview;
        Tour tour;

        void UI.IAppSide.RaiseShown() => OnShown(System.EventArgs.Empty);

        async Task MaybeStartTour()
        {
            if (Preview || TourDeclined || !(TourRequested || (firstRun && !settings.TourSeen))) return;
            await Task.Delay(500);   // let the first layout settle
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

        internal Tour BuildTour() => new Tour(this, new[]
        {
            new TourStep
            {
                Targets = () => new Control[] { txtTarget.Parent, txtPassword.Parent },
                Title = "Paste the invite",
                Body = "Your host sends you a line like steam://connect/1.2.3.4:27015. Paste it here, add the password if they gave you one, " +
                       "and click Check server. You don't need any mods to join.",
            },
            new TourStep
            {
                Targets = () => new Control[] { serverCard.Parent },
                Title = "Check, then join",
                Body = "You'll see the server's name, map, players and ping before you go in. If the host plays a custom campaign you don't have, " +
                       "a link opens its Workshop page. Join game starts Left 4 Dead 2 and connects you, and once you're in, a menu lets you pick your survivor from the ones still free (!char in chat opens it again).",
            },
            new TourStep
            {
                Targets = () => new Control[] { lnkLan.Parent, lvRecent.Parent.Parent },
                Title = "Recent groups",
                Body = "Every server you join is kept here: double-click one to check it again. Find games on my network looks for games on your home network.",
            },
            new TourStep
            {
                Targets = () => new Control[] { lnkHud.Parent },
                Title = "The HUD add-on",
                Body = "If your host turns on the 8-Player HUD, this small add-on shows everyone's health bars and items. " +
                       "Change game folder helps if Left 4 Dead 2 wasn't found.",
            },
            new TourStep
            {
                Targets = () => new Control[] { header },
                Title = "Host or join",
                Body = "Hosting a game yourself? Click HOST at the top: the same app sets up and runs the server, and JOIN brings you back here. " +
                       "It opens on whichever you used last. TOUR shows this tour again.",
            },
        });
    }
}
