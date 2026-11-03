using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    /// <summary>Result of an A2S_INFO query (what the server browser shows).</summary>
    public sealed class ServerInfo
    {
        public string Address;
        public string Name, Map, Folder, Game, Version;
        public int Players, MaxPlayers, Bots;
        public bool Password, Vac;
        public int PingMs;
    }

    /// <summary>Parsed "where to join" input: an IP, host:port, steam://connect link or console command.</summary>
    public sealed class JoinTarget
    {
        public string Host;
        public int Port = 27015;
        public string Password = "";
        /// <summary>The Workshop item of the host's custom campaign, when the invite names one.</summary>
        public string WorkshopId;
        public string Address => Host + ":" + Port;
        static readonly Regex Workshop = new Regex(@"sharedfiles/filedetails/\?id=(\d+)", RegexOptions.IgnoreCase);

        static readonly Regex SteamLink = new Regex(@"steam://connect/([^/:\s]+)(?::(\d+))?(?:/([^\s]*))?", RegexOptions.IgnoreCase);
        static readonly Regex Console = new Regex(@"connect\s+([^\s:;]+)(?::(\d+))?(?:\s*;\s*password\s+""?([^"";]*)""?)?", RegexOptions.IgnoreCase);
        static readonly Regex Plain = new Regex(@"^\s*([A-Za-z0-9.\-]+)(?::(\d+))?\s*$");

        public static JoinTarget Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var rx in new[] { SteamLink, Console, Plain })
            {
                var m = rx.Match(text.Trim());
                if (!m.Success) continue;
                var t = new JoinTarget { Host = m.Groups[1].Value };
                if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, out var p) && p > 0 && p < 65536) t.Port = p;
                if (m.Groups.Count > 3 && m.Groups[3].Success) t.Password = Uri.UnescapeDataString(m.Groups[3].Value);
                var w = Workshop.Match(text);
                if (w.Success) t.WorkshopId = w.Groups[1].Value;
                return t;
            }
            return null;
        }

        public string SteamUrl => "steam://connect/" + Address + (string.IsNullOrEmpty(Password) ? "" : "/" + Uri.EscapeDataString(Password));
        public string ConsoleCommand => "connect " + Address + (string.IsNullOrEmpty(Password) ? "" : $"; password \"{Password}\"");
    }

    /// <summary>Valve A2S_INFO server query over UDP, including the 2020+ challenge handshake.</summary>
    public static class ServerQuery
    {
        static byte[] Request(byte[] challenge)
        {
            var req = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x54 }.Concat(Encoding.ASCII.GetBytes("Source Engine Query\0"));
            return (challenge == null ? req : req.Concat(challenge)).ToArray();
        }

        public static async Task<ServerInfo> Query(string host, int port, int timeoutMs = 2500)
        {
            var ip = await Resolve(host);
            using (var udp = new UdpClient(ip.AddressFamily))
            {
                IgnoreConnReset(udp);
                var ep = new IPEndPoint(ip, port);
                var sw = Stopwatch.StartNew();
                byte[] challenge = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var req = Request(challenge);
                    await udp.SendAsync(req, req.Length, ep);
                    var data = await Receive(udp, timeoutMs);
                    if (data == null) return null;
                    if (data.Length >= 9 && data[4] == 0x41) { challenge = data.Skip(5).Take(4).ToArray(); sw.Restart(); continue; }
                    if (data.Length > 5 && data[4] == 0x49)
                    {
                        var info = Parse(data);
                        info.PingMs = (int)sw.ElapsedMilliseconds;
                        info.Address = host + ":" + port;
                        return info;
                    }
                    return null;
                }
                return null;
            }
        }

        /// <summary>A2S_PLAYER: names of the human players on the server (null if it doesn't answer).</summary>
        public static async Task<List<string>> Players(string host, int port, int timeoutMs = 2000)
        {
            var ip = await Resolve(host);
            using (var udp = new UdpClient(ip.AddressFamily))
            {
                IgnoreConnReset(udp);
                var ep = new IPEndPoint(ip, port);
                byte[] challenge = { 0xFF, 0xFF, 0xFF, 0xFF };
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var req = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x55 }.Concat(challenge).ToArray();
                    await udp.SendAsync(req, req.Length, ep);
                    var d = await Receive(udp, timeoutMs);
                    if (d == null || d.Length < 5) return null;
                    if (d[4] == 0x41 && d.Length >= 9) { challenge = d.Skip(5).Take(4).ToArray(); continue; }
                    if (d[4] != 0x44) return null;
                    var names = new List<string>();
                    int count = d.Length > 5 ? d[5] : 0, i = 6;
                    for (int p = 0; p < count && i < d.Length; p++)
                    {
                        i++; // index
                        int s = i;
                        while (i < d.Length && d[i] != 0) i++;
                        var name = Encoding.UTF8.GetString(d, s, i - s);
                        i += 1 + 4 + 4; // terminator, score, duration
                        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                    }
                    return names;
                }
                return null;
            }
        }

        /// <summary>A2S_RULES: the server's public cvars (FCVAR_NOTIFY), name to value; null if it doesn't answer.</summary>
        public static async Task<Dictionary<string, string>> Rules(string host, int port, int timeoutMs = 2500)
        {
            var ip = await Resolve(host);
            using (var udp = new UdpClient(ip.AddressFamily))
            {
                IgnoreConnReset(udp);
                var ep = new IPEndPoint(ip, port);
                byte[] challenge = { 0xFF, 0xFF, 0xFF, 0xFF };
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var req = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x56 }.Concat(challenge).ToArray();
                    await udp.SendAsync(req, req.Length, ep);
                    var d = await ReceiveWhole(udp, timeoutMs);
                    if (d == null || d.Length < 5) return null;
                    if (d[4] == 0x41 && d.Length >= 9) { challenge = d.Skip(5).Take(4).ToArray(); continue; }
                    if (d[4] != 0x45 || d.Length < 7) return null;
                    var rules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    int count = BitConverter.ToUInt16(d, 5), i = 7;
                    string Str()
                    {
                        int s = i;
                        while (i < d.Length && d[i] != 0) i++;
                        var v = Encoding.UTF8.GetString(d, s, i - s);
                        i++;
                        return v;
                    }
                    for (int r = 0; r < count && i < d.Length; r++) { var name = Str(); rules[name] = Str(); }
                    return rules;
                }
                return null;
            }
        }

        /// <summary>
        /// One answer, put back together when the server split it over several packets (header -2: an id, the packet
        /// count and number, and the size). Compressed answers (old engines) aren't read.
        /// </summary>
        static async Task<byte[]> ReceiveWhole(UdpClient udp, int timeoutMs)
        {
            var first = await Receive(udp, timeoutMs);
            if (first == null || first.Length < 4 || BitConverter.ToInt32(first, 0) != -2) return first;
            var parts = new Dictionary<int, byte[]>();
            int total = 1;
            for (var d = first; d != null; d = parts.Count < total ? await Receive(udp, timeoutMs) : null)
            {
                if (d.Length < 12 || BitConverter.ToInt32(d, 0) != -2) continue;
                if ((BitConverter.ToInt32(d, 4) & unchecked((int)0x80000000)) != 0) return null;   // bz2-compressed
                total = d[8];
                parts[d[9]] = d.Skip(12).ToArray();
            }
            if (parts.Count < total) return null;
            return Enumerable.Range(0, total).SelectMany(n => parts[n]).ToArray();   // starts with FF FF FF FF again
        }

        /// <summary>Broadcasts A2S_INFO on the local network and collects every L4D2 server that answers.</summary>
        public static async Task<List<ServerInfo>> FindLan(int timeoutMs = 1500)
        {
            var found = new List<ServerInfo>();
            using (var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true })
            {
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                IgnoreConnReset(udp);
                var req = Request(null);
                for (int port = 27015; port <= 27020; port++)
                    await udp.SendAsync(req, req.Length, new IPEndPoint(IPAddress.Broadcast, port));

                var end = DateTime.Now.AddMilliseconds(timeoutMs);
                var challenged = new HashSet<string>();
                while (DateTime.Now < end)
                {
                    var got = await ReceiveFrom(udp, (int)Math.Max(1, (end - DateTime.Now).TotalMilliseconds));
                    if (got == null) break;
                    var r = got.Value;
                    if (r.Buffer.Length >= 9 && r.Buffer[4] == 0x41 && challenged.Add(r.RemoteEndPoint.ToString()))
                    {
                        var again = Request(r.Buffer.Skip(5).Take(4).ToArray());
                        await udp.SendAsync(again, again.Length, r.RemoteEndPoint);
                        continue;
                    }
                    if (r.Buffer.Length > 5 && r.Buffer[4] == 0x49)
                    {
                        try
                        {
                            var info = Parse(r.Buffer);
                            info.Address = r.RemoteEndPoint.ToString();
                            if (!found.Any(f => f.Address == info.Address)) found.Add(info);
                        }
                        catch { }
                    }
                }
            }
            return found;
        }

        static async Task<IPAddress> Resolve(string host)
        {
            if (IPAddress.TryParse(host, out var ip)) return ip;
            var all = await Dns.GetHostAddressesAsync(host);
            return all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? all.First();
        }

        static async Task<byte[]> Receive(UdpClient udp, int timeoutMs) => (await ReceiveFrom(udp, timeoutMs))?.Buffer;

        static async Task<UdpReceiveResult?> ReceiveFrom(UdpClient udp, int timeoutMs)
        {
            try
            {
                var t = udp.ReceiveAsync();
                if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) return null;
                return t.Result;
            }
            catch (Exception ex) when (ex is SocketException || ex is AggregateException || ex is ObjectDisposedException)
            {
                return null; // closed port ("port unreachable") or socket torn down: treat as no answer
            }
        }

        /// <summary>Stops Windows from turning an ICMP "port unreachable" into an exception on the next receive.</summary>
        internal static void IgnoreConnReset(UdpClient udp)
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            try { udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null); } catch { }
        }

        static ServerInfo Parse(byte[] d)
        {
            int i = 5;
            string Str()
            {
                int s = i;
                while (i < d.Length && d[i] != 0) i++;
                var v = Encoding.UTF8.GetString(d, s, i - s);
                i++;
                return v;
            }
            var info = new ServerInfo();
            i++; // protocol
            info.Name = Str();
            info.Map = Str();
            info.Folder = Str();
            info.Game = Str();
            i += 2; // app id
            info.Players = d[i++];
            info.MaxPlayers = d[i++];
            info.Bots = d[i++];
            i += 2; // server type, environment
            info.Password = d[i++] == 1;
            info.Vac = d[i++] == 1;
            info.Version = Str();
            return info;
        }

        /// <summary>Official campaign maps ship with the game, so a joining player always has them.</summary>
        public static bool IsOfficialMap(string map) => Regex.IsMatch(map ?? "", @"^c\d{1,2}m\d+_", RegexOptions.IgnoreCase);

        /// <summary>Map names a client can load: loose .bsp files plus maps inside addon VPKs.</summary>
        public static HashSet<string> ClientMaps(string clientGameDir)
        {
            var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (clientGameDir == null) return maps;
            var loose = Path.Combine(clientGameDir, "maps");
            if (Directory.Exists(loose))
                foreach (var f in Directory.GetFiles(loose, "*.bsp")) maps.Add(Path.GetFileNameWithoutExtension(f));
            var addons = Path.Combine(clientGameDir, "addons");
            if (!Directory.Exists(addons)) return maps;
            foreach (var vpk in Directory.GetFiles(addons, "*.vpk", SearchOption.AllDirectories))
            {
                var info = Compat.ReadAddon(vpk);
                foreach (var f in info.Files.Where(f => f.StartsWith("maps/") && f.EndsWith(".bsp")))
                    maps.Add(Path.GetFileNameWithoutExtension(f));
            }
            return maps;
        }
    }
}
