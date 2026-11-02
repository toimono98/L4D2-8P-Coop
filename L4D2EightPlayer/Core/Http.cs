using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace L4D2EightPlayer.Core
{
    public static class Http
    {
        static readonly HttpClient Client;

        static Http()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
            Client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            Client.DefaultRequestHeaders.UserAgent.ParseAdd("L4D2-8P-Manager/1.0 (+https://github.com)");
        }

        public static async Task<string> GetString(string url, CancellationToken ct = default)
        {
            using (var resp = await Client.GetAsync(url, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    throw new IOException($"HTTP {(int)resp.StatusCode} from {url}");
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>POSTs a JSON body. The error names only the status, never the URL (webhook URLs are secrets).</summary>
        public static async Task PostJson(string url, string json, CancellationToken ct = default)
        {
            using (var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"))
            using (var resp = await Client.PostAsync(url, content, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    throw new IOException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
            }
        }

        /// <summary>Downloads to <paramref name="dest"/> via a temp file, reporting progress.</summary>
        public static async Task Download(string url, string dest, ILog log, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            var tmp = dest + ".part";
            using (var resp = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                    throw new IOException($"HTTP {(int)resp.StatusCode} downloading {url}");
                long? total = resp.Content.Headers.ContentLength;
                using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var dst = File.Create(tmp))
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    var name = Path.GetFileName(dest);
                    while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                    {
                        await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                        done += n;
                        if (total > 0) log.Progress("Downloading " + name, (double)done / total.Value);
                    }
                }
            }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        /// <summary>
        /// AlliedModders publishes a "latest" text file containing the current build's file name.
        /// </summary>
        public static async Task<string> ResolveAlliedModders(string latestUrl, string baseUrl, CancellationToken ct = default)
        {
            var name = (await GetString(latestUrl, ct).ConfigureAwait(false)).Trim();
            if (!Regex.IsMatch(name, @"^[\w.\-]+\.zip$"))
                throw new IOException($"Unexpected response from {latestUrl}: {name}");
            return baseUrl.TrimEnd('/') + "/" + name;
        }

        /// <summary>Finds the newest GitHub release asset matching a pattern. Returns (url, tag).</summary>
        public static async Task<(string url, string tag)> ResolveGitHubRelease(string repo, string assetPattern, CancellationToken ct = default)
        {
            var json = await GetString($"https://api.github.com/repos/{repo}/releases?per_page=10", ct).ConfigureAwait(false);
            var releases = Json.Parse(json) as object[];
            if (releases == null) throw new IOException("GitHub returned no releases for " + repo);
            var rx = new Regex(assetPattern, RegexOptions.IgnoreCase);
            foreach (var r in releases.OfType<Dictionary<string, object>>())
            {
                if (r.TryGetValue("draft", out var draft) && draft is bool d && d) continue;
                if (r.TryGetValue("prerelease", out var pre) && pre is bool p && p) continue;
                if (!(r.TryGetValue("assets", out var assets) && assets is object[] list)) continue;
                foreach (var a in list.OfType<Dictionary<string, object>>())
                {
                    if (rx.IsMatch(a.Str("name", "")))
                        return (a.Str("browser_download_url"), r.Str("tag_name"));
                }
            }
            throw new IOException($"No release asset matching '{assetPattern}' in {repo}");
        }

        public static string FileNameFromUrl(string url)
        {
            var name = Uri.UnescapeDataString(new Uri(url).Segments.Last());
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
