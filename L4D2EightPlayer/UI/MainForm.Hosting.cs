using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using L4D2EightPlayer.Core;
using L4D2EightPlayer.UI.Themes;
using static L4D2EightPlayer.UI.Themes.Ui;

namespace L4D2EightPlayer.UI
{
    /// <summary>Friends over the internet (on the Play tab): router port forwarding (UPnP), firewall rule, reachability and the invite.</summary>
    public sealed partial class MainForm
    {
        TextBox txtNetStatus, txtNetHelp;
        CheckBox chkAutoForward;
        GameButton btnNetCheck, btnForward, btnUnforward, btnFirewall, btnReach;
        HopCard hopPc, hopRouter, hopInternet, hopFriends;
        Connector linkPcRouter, linkRouterNet, linkNetFriends;
        Label lblVerdict;
        Upnp router;
        bool routerSearched;
        string routerExternal;

        const string MappingName = "L4D2 8-Player Co-op";

        /// <summary>The More settings fold on the Play tab, and the route check under it.</summary>
        Control moreSettings, netPanel;
        /// <summary>What the main network button does right now: open the port, test it, or open the router's page.</summary>
        Func<Task> netAction;

        /// <summary>
        /// "Can friends reach you?": the route from this PC to friends, one button for the next step (open the port,
        /// then test it), and the rest (firewall, closing the port, forwarding by hand, the raw details) folded away.
        /// </summary>
        Control BuildNetworkPanel()
        {
            var card = new Card("Can friends reach you?") { Dock = DockStyle.Fill, AutoSize = true, Margin = P(0, 14, 0, 0) };
            var g = Stack(P(0), autoSize: true);

            // The route: this PC -> router -> internet -> friends
            var route = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 7, Height = HopCard.PreferredHeight + S(4), Margin = P(0, 0, 0, 12) };
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(44)));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(120)));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(44)));
            route.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            route.RowStyles.Add(new RowStyle(SizeType.Absolute, HopCard.PreferredHeight));
            hopPc = new HopCard("1 · This PC") { Dock = DockStyle.Fill };
            hopRouter = new HopCard("2 · Router") { Dock = DockStyle.Fill };
            hopInternet = new HopCard("3 · Internet") { Dock = DockStyle.Fill };
            hopFriends = new HopCard("4 · Friends") { Dock = DockStyle.Fill };
            linkPcRouter = new Connector { Dock = DockStyle.Fill };
            linkRouterNet = new Connector { Dock = DockStyle.Fill, Unknown = true, Label = "checking" };
            linkNetFriends = new Connector { Dock = DockStyle.Fill, Unknown = true };
            foreach (var h in new[] { hopPc, hopRouter, hopInternet }) h.Set("…", "Checking", null, true);
            hopFriends.Set("Can't get in yet", "Open the port first", null, true, pending: true);
            route.Controls.Add(hopPc, 0, 0);
            route.Controls.Add(linkPcRouter, 1, 0);
            route.Controls.Add(hopRouter, 2, 0);
            route.Controls.Add(linkRouterNet, 3, 0);
            route.Controls.Add(hopInternet, 4, 0);
            route.Controls.Add(linkNetFriends, 5, 0);
            route.Controls.Add(hopFriends, 6, 0);
            g.Controls.Add(route);
            routeRow = route;

            // the next step, and what it means
            var step = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Margin = P(0, 0, 0, 4) };
            step.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            step.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            step.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnForward = Btn("Checking…", ButtonKind.Primary, async (s, e) => { if (netAction != null) await netAction(); }, 11f);
            btnForward.Margin = P(0, 0, 16, 0);
            lblVerdict = new Label { AutoSize = true, MaximumSize = Sz(620, 0), Font = Theme.BodySemi(10.5f), ForeColor = Theme.Bone, Anchor = AnchorStyles.Left, Margin = P(0), UseMnemonic = false };
            var helpToggle = new LinkLabel { Text = "Help & details ▸", AutoSize = true, Font = Theme.BodySemi(9.5f), Anchor = AnchorStyles.Right, Margin = P(12, 0, 0, 0), UseMnemonic = false };
            step.Controls.Add(btnForward, 0, 0);
            step.Controls.Add(lblVerdict, 1, 0);
            step.Controls.Add(helpToggle, 2, 0);
            g.Controls.Add(step);

            // the rest, folded away
            var help = Stack(P(0, 12, 0, 0), autoSize: true);
            help.Visible = false;
            helpToggle.LinkClicked += (s, e) => { help.Visible = !help.Visible; helpToggle.Text = help.Visible ? "Help & details ▾" : "Help & details ▸"; };
            btnFirewall = Btn("Allow in firewall", ButtonKind.Ghost, async (s, e) => await AllowFirewall(), 10f);
            btnReach = Btn("Test from outside", ButtonKind.Ghost, async (s, e) => await TestReachability(), 10f);
            btnNetCheck = Btn("Check again", ButtonKind.Ghost, async (s, e) => await CheckNetwork(verbose: true), 10f);
            btnUnforward = Btn("Close port", ButtonKind.Ghost, async (s, e) => await UnforwardNow(), 10f);
            var btnRouter = Btn("Router settings", ButtonKind.Ghost, (s, e) => OpenRouterPage(), 10f);
            var buttons = Row(btnFirewall, btnReach, btnNetCheck, btnUnforward, btnRouter);
            buttons.Margin = P(0, 0, 0, 8);
            help.Controls.Add(buttons);
            chkAutoForward = new GameCheck { Text = "Open the port while I host, and close it when I stop", AutoSize = true, Font = Theme.Body(10f), Checked = settings.AutoPortForward, Margin = P(0, 0, 0, 10) };
            chkAutoForward.CheckedChanged += (s, e) => { settings.AutoPortForward = chkAutoForward.Checked; settings.PortForwardAsked = true; settings.Save(); };
            help.Controls.Add(chkAutoForward);
            var texts = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = S(230), Margin = P(0) };
            texts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            texts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            texts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            txtNetHelp = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = Theme.Body(9.5f), Margin = P(0, 0, 16, 0) };
            txtNetStatus = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = Theme.Mono(8.5f), ScrollBars = ScrollBars.Vertical,
                                         Text = "Click Check again to test your connection." };
            texts.Controls.Add(txtNetHelp, 0, 0);
            texts.Controls.Add(txtNetStatus, 1, 0);
            help.Controls.Add(texts);
            g.Controls.Add(help);

            card.Controls.Add(g);
            UpdateNetHelp();
            return card;
        }

        void OpenRouterPage()
        {
            var gw = Network.Gateway();
            if (gw != null) System.Diagnostics.Process.Start("http://" + gw);
        }

        TextBox txtWebhook;
        CheckBox chkDiscordStart, chkDiscordStop;

        /// <summary>"Post to Discord": a channel webhook, when to post, and a test button.</summary>
        Card BuildDiscordCard()
        {
            var card = new Card("Post to Discord") { Dock = DockStyle.Fill, AutoSize = true, Margin = P(0, 0, 0, 10) };
            var st = Stack(P(0), autoSize: true);
            st.Controls.Add(new Label { Text = "Paste a channel webhook (in Discord: channel settings › Integrations › Webhooks › Copy Webhook URL). Treat it like a password.",
                AutoSize = true, MaximumSize = Sz(320, 0), Font = Theme.Body(9.5f), ForeColor = Theme.Muted, Margin = P(0, 0, 0, 8) }, 0, 0);
            txtWebhook = new TextBox { Dock = DockStyle.Fill, Font = Theme.Mono(9.5f), UseSystemPasswordChar = true, Text = settings.DiscordWebhook ?? "", Margin = P(0, 0, 0, 8), AccessibleName = "Discord webhook URL" };
            txtWebhook.TextChanged += (s, e) => { settings.DiscordWebhook = txtWebhook.Text.Trim(); settings.Save(); };
            st.Controls.Add(txtWebhook, 0, 1);
            chkDiscordStart = new GameCheck { Text = "Post the invite when the server starts", AutoSize = true, Font = Theme.Body(9.5f), Checked = settings.DiscordOnStart, Margin = P(0, 0, 0, 2) };
            chkDiscordStop = new GameCheck { Text = "Post when it stops", AutoSize = true, Font = Theme.Body(9.5f), Checked = settings.DiscordOnStop, Margin = P(0, 0, 0, 8) };
            chkDiscordStart.CheckedChanged += (s, e) => { settings.DiscordOnStart = chkDiscordStart.Checked; settings.Save(); };
            chkDiscordStop.CheckedChanged += (s, e) => { settings.DiscordOnStop = chkDiscordStop.Checked; settings.Save(); };
            st.Controls.Add(chkDiscordStart, 0, 2);
            st.Controls.Add(chkDiscordStop, 0, 3);
            var test = Btn("Send a test post", ButtonKind.Ghost, async (s, e) => await AnnounceDiscord(stopped: false, test: true), 10f);
            test.Margin = P(0);
            st.Controls.Add(test, 0, 4);
            card.Controls.Add(st);
            return card;
        }

        /// <summary>Posts the server's invite (or "stopped") to the Discord webhook, if one is set.</summary>
        async Task AnnounceDiscord(bool stopped, bool test = false)
        {
            var hook = settings.DiscordWebhook;
            if (string.IsNullOrWhiteSpace(hook)) { if (test) Warn("Discord: paste a channel webhook URL first."); return; }
            if (!Discord.IsWebhook(hook)) { Warn("Discord: that doesn't look like a webhook URL (it starts with https://discord.com/api/webhooks/)."); return; }
            if (!test && (stopped ? !settings.DiscordOnStop : !settings.DiscordOnStart)) return;
            if (settings.Mode == "listen") { if (test) Warn("Discord posts are for the dedicated server."); return; }
            if (settings.NetworkMode == "local" && !test) { Info("Discord: not posted, the server is set to Just me."); return; }
            if (publicIp == null && settings.Internet) { try { publicIp = await Launcher.PublicAddress(); } catch { } }

            var t = new JoinTarget { Host = ShareHost(), Port = HostPort, Password = settings.Password };
            var camp = ServerQuery.IsOfficialMap(settings.Map) ? null : Campaigns.ForMap(customCampaigns, settings.Map);
            var a = new Discord.Announcement
            {
                Server = (test ? "[test] " : "") + settings.ServerName,
                Campaign = CampaignTitle(settings.Map) is string ct && ct != "CUSTOM MAP" ? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(ct.ToLowerInvariant()) : settings.Map,
                Mode = settings.GameMode,
                Difficulty = settings.Difficulty,
                MaxPlayers = settings.MaxPlayers,
                SteamUrl = t.SteamUrl,
                ConsoleCommand = t.ConsoleCommand,
                NeedsTitle = camp?.Title,
                NeedsUrl = camp?.WorkshopUrl,
                Stopped = stopped,
            };
            try
            {
                await Discord.Post(hook, a);
                Info(stopped ? "Posted to Discord: server stopped." : "Posted the invite to Discord.");
            }
            catch (Exception ex) { Warn("Discord post failed: " + ex.Message); }
        }

        /// <summary>Paints the route cards from the latest network check.</summary>
        void ShowRoute(string lan, bool firewall, Upnp upnp, string gw, string udp, bool cgnat)
        {
            hopPc.Set(lan, firewall ? "Firewall rule added" : "Firewall asks on first start", "Ready", true);
            if (upnp != null) hopRouter.Set(upnp.FriendlyName, "UPnP on · " + (gw ?? ""), "Found", true);
            else hopRouter.Set(gw ?? "Not found", "UPnP is off or unsupported", "Check router", false);
            linkPcRouter.Set(true, false, null);

            bool forwarded = udp != null && udp == lan;
            if (upnp == null) linkRouterNet.Set(false, true, $"port {HostPort}: check by hand");
            else if (forwarded) linkRouterNet.Set(true, false, $"port {HostPort} open");
            else if (udp != null) linkRouterNet.Set(false, false, $"port {HostPort} goes to {udp}");
            else linkRouterNet.Set(false, false, $"port {HostPort} shut");

            if (cgnat) hopInternet.Set(publicIp ?? routerExternal ?? "unknown", "Behind CGNAT or a second router", "Blocked", false);
            else hopInternet.Set(publicIp ?? "unknown", publicIp == null ? "No internet answer" : "Real public IP · no CGNAT", publicIp == null ? "Offline?" : "Good", publicIp != null);

            bool ready = forwarded && !cgnat && publicIp != null;
            linkNetFriends.Set(ready, !ready, null);
            if (ready) hopFriends.Set("Can join", $"connect {publicIp}:{HostPort}", "Ready", true);
            else hopFriends.Set("Can't get in yet", cgnat ? "Use a virtual LAN instead" : "Open the port first", null, true, pending: true);

            // one button: the next step from where things stand
            if (cgnat) { btnForward.Text = "Router can't let friends in"; netAction = null; }
            else if (upnp == null) { btnForward.Text = "Open router settings"; netAction = () => { OpenRouterPage(); return Task.CompletedTask; }; }
            else if (!forwarded) { btnForward.Text = $"Open port {HostPort} on my router"; netAction = ForwardNow; }
            else { btnForward.Text = "Test from outside"; netAction = TestReachability; }
            btnForward.Enabled = netAction != null;
        }

        int HostPort => settings.Mode == "listen" ? 27015 : settings.Port;

        string HostProgram => settings.Mode == "listen"
            ? (settings.GamePath == null ? null : Path.Combine(settings.GamePath, "left4dead2.exe"))
            : (settings.ServerInstallDir == null ? null : Path.Combine(settings.ServerInstallDir, "srcds.exe"));

        async Task<Upnp> Router()
        {
            if (router != null) return router;
            router = await Upnp.Discover();
            routerSearched = true;
            return router;
        }

        async Task CheckNetwork(bool verbose)
        {
            var nl = Environment.NewLine;
            txtNetStatus.Text = "Checking your network...";
            btnNetCheck.Enabled = false;
            try
            {
                router = null;
                var lan = Network.LanAddress();
                var gw = Network.Gateway();
                var upnp = await Router();
                routerExternal = null;
                string udp = null, tcp = null;
                if (upnp != null)
                {
                    try { routerExternal = await upnp.ExternalAddress(); } catch { }
                    try { udp = await upnp.MappedTo(HostPort, "UDP"); tcp = await upnp.MappedTo(HostPort, "TCP"); } catch { }
                }
                if (publicIp == null) { try { publicIp = await Launcher.PublicAddress(); } catch { } }
                bool firewall = await Task.Run(() => Firewall.RuleExists());

                string Mark(string to) => to == null ? "✖ not forwarded" : to == lan ? "✔ forwarded to this PC" : $"⚠ forwarded to another device ({to})";
                var sb = new System.Text.StringBuilder();
                sb.Append($"This PC (LAN address)      {lan}{nl}");
                sb.Append($"Router                     {gw ?? "not found"}{(upnp != null ? "  ·  " + upnp.FriendlyName + "  ·  UPnP on" : "  ·  UPnP off or unsupported")}{nl}");
                sb.Append($"Router's internet address  {routerExternal ?? "unknown"}{nl}");
                sb.Append($"Your public address        {publicIp ?? "unknown (no internet?)"}{nl}");
                sb.Append($"Port {HostPort} UDP (game)      {(upnp == null ? "unknown (check your router)" : Mark(udp))}{nl}");
                sb.Append($"Port {HostPort} TCP (rcon)      {(upnp == null ? "unknown (check your router)" : Mark(tcp))}{nl}");
                sb.Append($"Windows Firewall rule      {(firewall ? "✔ present" : "– not added (Windows will ask when the server first starts)")}{nl}{nl}");

                bool cgnat = Network.IsPrivate(routerExternal) || (routerExternal != null && publicIp != null && routerExternal != publicIp);
                if (cgnat)
                    sb.Append("✖ Your router's internet address is not your public address. There is a second router, or your ISP uses carrier-grade NAT (CGNAT), so port forwarding on this router can't let friends in." + nl +
                              "  Fixes: put the ISP modem in bridge mode and forward on it too, ask your ISP for a public IPv4, or use a virtual LAN (Tailscale, ZeroTier, Radmin VPN) and have friends connect to your virtual-LAN IP.");
                else if (upnp != null && udp == lan)
                    sb.Append("✔ Ready: friends can get in with the invite. Test from outside checks it while the server runs.");
                else if (upnp != null)
                    sb.Append("➜ Open the port now, or just start hosting: it opens by itself while you host (Help & details).");
                else
                    sb.Append("➜ Your router has UPnP turned off. Turn it on in the router settings, or forward the port by hand (Help & details).");
                txtNetStatus.Text = sb.ToString();
                ShowRoute(lan, firewall, upnp, gw, udp, cgnat);
                lblVerdict.Text = sb.ToString().Split(new[] { nl + nl }, StringSplitOptions.None).Last().Trim();
                lblVerdict.ForeColor = cgnat ? Theme.Hex("#ff8a7a") : upnp != null && udp == lan ? Theme.Bone : Theme.Amber;
                if (verbose) Info("Network check: " + (cgnat ? "CGNAT / double NAT detected" : upnp == null ? "UPnP not available" : udp == lan ? "port forwarded" : "port not forwarded yet"));
            }
            catch (Exception ex) { txtNetStatus.Text = "Network check failed: " + ex.Message; }
            finally { btnNetCheck.Enabled = true; UpdateNetHelp(); }
        }

        async Task<bool> Forward(bool quiet)
        {
            var upnp = await Router();
            if (upnp == null)
            {
                if (!quiet) MessageBox.Show(this, "Your router didn't answer a UPnP request. Turn on UPnP in the router settings, or forward the port by hand (Play tab: Help & details).", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Warn("Automatic port forwarding: no UPnP router found. Forward the port by hand if friends can't join.");
                return false;
            }
            try
            {
                foreach (var proto in new[] { "UDP", "TCP" })
                {
                    var current = await upnp.MappedTo(HostPort, proto);
                    if (current != null && current != upnp.LocalAddress)
                    {
                        Warn($"Port {HostPort} {proto} is already forwarded to {current}, another device. Pick another port (Play tab, More settings) or change it on the router.");
                        return false;
                    }
                    await upnp.Add(HostPort, proto, MappingName);
                }
                settings.ForwardedPort = HostPort;
                settings.Save();
                Info($"Router ({upnp.FriendlyName}): port {HostPort} UDP+TCP now forwards to this PC ({upnp.LocalAddress}).");
                return true;
            }
            catch (Exception ex)
            {
                Error("Automatic port forwarding failed: " + ex.Message);
                if (!quiet) MessageBox.Show(this, "The router refused the port forward:\n" + ex.Message + "\n\nForward it by hand instead (Play tab: Help & details).", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        async Task Unforward(int port)
        {
            var upnp = await Router();
            if (upnp == null || port == 0) return;
            try
            {
                foreach (var proto in new[] { "UDP", "TCP" })
                    if (await upnp.MappedTo(port, proto) == upnp.LocalAddress) await upnp.Remove(port, proto);
                Info($"Router: closed port {port}.");
            }
            catch (Exception ex) { Warn("Could not close the router port: " + ex.Message); }
            settings.ForwardedPort = 0;
            settings.Save();
        }

        async Task ForwardNow()
        {
            if (await Forward(quiet: false)) await CheckNetwork(verbose: false);
        }

        async Task UnforwardNow()
        {
            await Unforward(settings.ForwardedPort != 0 ? settings.ForwardedPort : HostPort);
            await CheckNetwork(verbose: false);
        }

        /// <summary>Called when hosting starts (open=true) or stops (open=false).</summary>
        async Task AutoForward(bool open)
        {
            if (Preview) return;
            if (open)
            {
                if (!settings.PortForwardAsked)
                {
                    settings.PortForwardAsked = true;
                    settings.AutoPortForward = MessageBox.Show(this,
                        $"Open port {HostPort} on your router automatically (UPnP) so friends can join over the internet?\n\nIt is closed again when you stop hosting. You can change this on the Play tab, under Help & details.",
                        Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
                    if (chkAutoForward != null) chkAutoForward.Checked = settings.AutoPortForward;
                    settings.Save();
                }
                // LAN only / Just me never touch the router.
                if (settings.AutoPortForward && settings.Internet) await Forward(quiet: true);
            }
            else if (settings.AutoPortForward && settings.ForwardedPort != 0)
                await Unforward(settings.ForwardedPort);
        }

        /// <summary>If the tool was closed while hosting, the router mapping may still be open.</summary>
        async Task CleanupStaleForward()
        {
            if (Preview || settings.ForwardedPort == 0 || !settings.AutoPortForward) return;
            bool hosting = (serverProcess != null && !serverProcess.HasExited) || System.Diagnostics.Process.GetProcessesByName("srcds").Length > 0 || Launcher.GameRunning();
            if (!hosting) await Unforward(settings.ForwardedPort);
        }

        async Task AllowFirewall()
        {
            var program = HostProgram;
            if (program != null && !File.Exists(program)) program = null;
            Info("Adding a Windows Firewall rule (Windows will ask for permission)...");
            bool ok = await Task.Run(() => Firewall.Add(HostPort, program));
            if (ok) Info($"Windows Firewall now allows port {HostPort} (UDP+TCP)" + (program != null ? " and " + Path.GetFileName(program) : "") + ".");
            else Warn("The firewall rule wasn't added (permission declined or blocked).");
            await CheckNetwork(verbose: false);
        }

        async Task TestReachability()
        {
            if (publicIp == null) { try { publicIp = await Launcher.PublicAddress(); } catch { } }
            bool hosting = (serverProcess != null && !serverProcess.HasExited) || Launcher.GameRunning();
            if (!hosting)
            {
                MessageBox.Show(this, "Start the server (or host from your game) first, then run this test.", Title);
                return;
            }
            Info($"Querying your server through your public address {publicIp}:{HostPort}...");
            var local = await ServerQuery.Query(Launcher.LocalServerAddress(), HostPort);
            var outside = publicIp == null ? null : await ServerQuery.Query(publicIp, HostPort, 3000);
            if (local == null) Warn("The server isn't answering even locally yet. Wait for the map to load and try again.");
            else if (outside != null) Info($"✔ Reachable from the internet: \"{outside.Name}\" on {outside.Map}, {outside.Players}/{outside.MaxPlayers} players.");
            else Warn("No answer through your public address. Many routers can't loop back to themselves, so this can fail even when forwarding works. " +
                      "To be sure, ask a friend to paste your invite into L4D2 8-Player Co-op (JOIN) and press Check server.");
        }

        string InviteText()
        {
            var target = new JoinTarget { Host = ShareHost(), Port = HostPort, Password = settings.Mode == "listen" ? "" : settings.Password };
            return target.SteamUrl + CampaignNeeds();
        }

        /// <summary>
        /// "  ·  Needs <campaign>: <Workshop link>" when the server is on a custom campaign. Kept on the invite's one
        /// line: the Join app's address box only takes the first line of a paste, and finds the link there.
        /// </summary>
        string CampaignNeeds()
        {
            if (settings.Mode == "listen" || ServerQuery.IsOfficialMap(settings.Map)) return "";
            var c = Campaigns.ForMap(customCampaigns, settings.Map);
            if (c == null) return "";
            return $"  ·  Needs the {c.Title} campaign" + (c.WorkshopUrl != null ? ": " + c.WorkshopUrl : $" (map {c.FirstMap})");
        }

        async void CopyInvite()
        {
            if (publicIp == null) { try { publicIp = await Launcher.PublicAddress(); } catch { } }
            var t = new JoinTarget { Host = ShareHost(), Port = HostPort, Password = settings.Mode == "listen" ? "" : settings.Password };
            var text = $"Join my L4D2 8-player game: {t.SteamUrl}   (or in the L4D2 console: {t.ConsoleCommand})" + CampaignNeeds();
            Clipboard.SetText(text);
            Info("Copied invite: " + text + (publicIp == null ? "  (LAN address only; no internet address found)" : ""));
        }

        void UpdateNetHelp()
        {
            if (txtNetHelp == null) return;
            var nl = Environment.NewLine;
            txtNetHelp.Text =
                "FORWARDING BY HAND (if automatic doesn't work)" + nl +
                $"1. Click 'Router settings' (or browse to http://{Network.Gateway() ?? "<router IP>"}) and log in. The password is often on a sticker on the router." + nl +
                "2. Find 'Port Forwarding', 'Virtual Server', 'NAT' or 'Applications & Gaming'." + nl +
                $"3. Add a rule: external port {HostPort}, internal port {HostPort}, protocol UDP and TCP (or 'Both'), device/IP {Network.LanAddress()}." + nl +
                "4. Save, then come back here and click Check again." + nl +
                $"Tip: give this PC a reserved/static IP in the router's DHCP settings so the rule keeps pointing at it." + nl + nl +
                "NO PORT FORWARDING POSSIBLE? (CGNAT, dorm or mobile internet)" + nl +
                "Install a virtual LAN tool such as Tailscale, ZeroTier or Radmin VPN on your PC and on your friends' PCs, join the same network, and give friends your virtual-LAN IP. They connect with:  connect <that IP>:" + HostPort;
        }
    }
}
