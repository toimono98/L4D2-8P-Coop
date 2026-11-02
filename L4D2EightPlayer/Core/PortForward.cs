using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Minimal UPnP Internet Gateway Device client: finds the router on the LAN and
    /// adds/removes/reads port mappings (the same thing "port forwarding" pages do).
    /// </summary>
    public sealed class Upnp
    {
        public string ControlUrl { get; private set; }
        public string ServiceType { get; private set; }
        public string FriendlyName { get; private set; }
        public string LocalAddress { get; private set; }

        /// <summary>What the last discovery saw, for troubleshooting ("router answered but ...").</summary>
        public static List<string> LastDiagnostics { get; } = new List<string>();

        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        /// <summary>Returns the router's UPnP service, or null if UPnP is off or unsupported.</summary>
        public static async Task<Upnp> Discover(int timeoutMs = 3000)
        {
            var lan = Network.LanAddress();
            var locations = new List<string>();
            LastDiagnostics.Clear();
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Parse(lan), 0)))
            {
                ServerQuery.IgnoreConnReset(udp);
                var ssdp = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                foreach (var st in SearchTargets)
                {
                    var msg = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nST: {st}\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n\r\n");
                    await udp.SendAsync(msg, msg.Length, ssdp);
                }
                var end = DateTime.Now.AddMilliseconds(timeoutMs);
                while (DateTime.Now < end)
                {
                    string text;
                    try
                    {
                        var t = udp.ReceiveAsync();
                        if (await Task.WhenAny(t, Task.Delay(end - DateTime.Now)) != t) break;
                        text = Encoding.ASCII.GetString(t.Result.Buffer);
                    }
                    catch (Exception ex) when (ex is SocketException || ex is AggregateException) { continue; }
                    var m = Regex.Match(text, @"^LOCATION:\s*(\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (m.Success && !locations.Contains(m.Groups[1].Value)) locations.Add(m.Groups[1].Value);
                }
            }

            LastDiagnostics.Add($"SSDP replies with a location: {locations.Count}");
            foreach (var loc in locations)
            {
                try
                {
                    var xml = new XmlDocument();
                    xml.LoadXml(Encoding.UTF8.GetString(await Client.GetByteArrayAsync(loc)).TrimStart('﻿'));
                    var ns = new XmlNamespaceManager(xml.NameTable);
                    ns.AddNamespace("d", "urn:schemas-upnp-org:device-1-0");
                    var baseUrl = xml.SelectSingleNode("//d:URLBase", ns)?.InnerText;
                    var root = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? loc : baseUrl);
                    foreach (XmlNode svc in xml.SelectNodes("//d:service", ns))
                    {
                        var type = svc.SelectSingleNode("d:serviceType", ns)?.InnerText ?? "";
                        if (!type.Contains("WANIPConnection") && !type.Contains("WANPPPConnection")) continue;
                        var control = svc.SelectSingleNode("d:controlURL", ns)?.InnerText;
                        if (string.IsNullOrEmpty(control)) continue;
                        var u = new Upnp
                        {
                            ServiceType = type,
                            ControlUrl = new Uri(root, control).ToString(),
                            FriendlyName = xml.SelectSingleNode("//d:device/d:friendlyName", ns)?.InnerText ?? root.Host,
                            LocalAddress = lan,
                        };
                        // A PPP service can be listed but disconnected; prefer the one that reports an external IP.
                        try
                        {
                            var ext = await u.ExternalAddress();
                            LastDiagnostics.Add($"{type} at {u.ControlUrl}: external '{ext}'");
                            if (!string.IsNullOrEmpty(ext)) return u;
                        }
                        catch (Exception ex) { LastDiagnostics.Add($"{type} at {u.ControlUrl}: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { LastDiagnostics.Add($"{loc}: {ex.Message}"); /* try the next responder */ }
            }
            return null;
        }

        public async Task<string> ExternalAddress()
        {
            var r = await Soap("GetExternalIPAddress", new Dictionary<string, string>());
            return r.TryGetValue("NewExternalIPAddress", out var ip) ? ip : null;
        }

        public async Task Add(int port, string protocol, string description)
        {
            var args = new Dictionary<string, string>
            {
                ["NewRemoteHost"] = "",
                ["NewExternalPort"] = port.ToString(),
                ["NewProtocol"] = protocol,
                ["NewInternalPort"] = port.ToString(),
                ["NewInternalClient"] = LocalAddress,
                ["NewEnabled"] = "1",
                ["NewPortMappingDescription"] = description,
                ["NewLeaseDuration"] = "0",
            };
            try { await Soap("AddPortMapping", args); }
            catch (UpnpException ex) when (ex.Code == 725 || ex.Code == 402)
            {
                // Some routers refuse permanent mappings; ask for a week instead.
                args["NewLeaseDuration"] = "604800";
                await Soap("AddPortMapping", args);
            }
        }

        public async Task Remove(int port, string protocol)
        {
            try
            {
                await Soap("DeletePortMapping", new Dictionary<string, string>
                {
                    ["NewRemoteHost"] = "",
                    ["NewExternalPort"] = port.ToString(),
                    ["NewProtocol"] = protocol,
                });
            }
            catch (UpnpException ex) when (ex.Code == 714) { /* already gone */ }
        }

        /// <summary>Who the router currently forwards this port to (LAN IP), or null if not forwarded.</summary>
        public async Task<string> MappedTo(int port, string protocol)
        {
            try
            {
                var r = await Soap("GetSpecificPortMappingEntry", new Dictionary<string, string>
                {
                    ["NewRemoteHost"] = "",
                    ["NewExternalPort"] = port.ToString(),
                    ["NewProtocol"] = protocol,
                });
                return r.TryGetValue("NewInternalClient", out var c) ? c : null;
            }
            catch (UpnpException ex) when (ex.Code == 714 || ex.Code == 402) { return null; }
        }

        async Task<Dictionary<string, string>> Soap(string action, Dictionary<string, string> args)
        {
            var body = new StringBuilder();
            body.Append("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>");
            body.Append($"<u:{action} xmlns:u=\"{ServiceType}\">");
            foreach (var kv in args) body.Append($"<{kv.Key}>{System.Security.SecurityElement.Escape(kv.Value)}</{kv.Key}>");
            body.Append($"</u:{action}></s:Body></s:Envelope>");

            var req = new HttpRequestMessage(HttpMethod.Post, ControlUrl) { Content = new StringContent(body.ToString(), Encoding.UTF8, "text/xml") };
            req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{ServiceType}#{action}\"");
            using (var resp = await Client.SendAsync(req))
            {
                // Many routers send charset="utf-8" (quoted), which ReadAsStringAsync rejects; decode the bytes ourselves.
                var text = Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync());
                if (!resp.IsSuccessStatusCode)
                {
                    var code = Regex.Match(text, @"<errorCode>(\d+)</errorCode>");
                    var desc = Regex.Match(text, @"<errorDescription>([^<]*)</errorDescription>");
                    throw new UpnpException(code.Success ? int.Parse(code.Groups[1].Value) : (int)resp.StatusCode,
                        desc.Success ? desc.Groups[1].Value : resp.ReasonPhrase);
                }
                var result = new Dictionary<string, string>();
                foreach (Match m in Regex.Matches(text, @"<(New\w+)>([^<]*)</\1>")) result[m.Groups[1].Value] = WebUtility.HtmlDecode(m.Groups[2].Value);
                return result;
            }
        }
    }

    public sealed class UpnpException : Exception
    {
        public int Code { get; }
        public UpnpException(int code, string message) : base($"router error {code}: {message}") { Code = code; }
    }

    public static class Network
    {
        public static string LanAddress()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                         .OrderBy(n => n.GetIPProperties().GatewayAddresses.Count == 0))
            {
                var ip = ni.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ip != null) return ip.Address.ToString();
            }
            return "127.0.0.1";
        }

        public static string Gateway()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var gw = ni.GetIPProperties().GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
                if (gw != null) return gw.ToString();
            }
            return null;
        }

        /// <summary>
        /// True for addresses that are not reachable from the internet. If the router's own "external"
        /// address is one of these, there is a second router or carrier-grade NAT (CGNAT) in the way.
        /// </summary>
        public static bool IsPrivate(string ip)
        {
            if (!IPAddress.TryParse(ip ?? "", out var a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
            var b = a.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] == 127 || (b[0] == 169 && b[1] == 254);
        }
    }

    /// <summary>Windows Firewall inbound rule for the game port. Adding/removing asks for admin rights (UAC).</summary>
    public static class Firewall
    {
        public const string RuleName = "L4D2 8-Player Co-op Server";

        public static bool RuleExists()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{RuleName}\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                });
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        /// <summary>Allows UDP+TCP on the port for this program. Returns false if the user declined UAC.</summary>
        public static bool Add(int port, string program)
        {
            var cmds = new List<string>
            {
                $"netsh advfirewall firewall delete rule name=\"{RuleName}\"",
                $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport={port}",
                $"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=TCP localport={port}",
            };
            if (!string.IsNullOrEmpty(program))
                cmds.Add($"netsh advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{program}\"");
            return RunElevated(string.Join(" & ", cmds)) && RuleExists();
        }

        public static bool Remove() => RunElevated($"netsh advfirewall firewall delete rule name=\"{RuleName}\"");

        static bool RunElevated(string commandLine)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + commandLine)
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                });
                p.WaitForExit();
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // UAC declined
        }
    }
}
