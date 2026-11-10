using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Reads the directory tree of a Valve VPK (v1/v2) and can pull small files out of it.
    /// L4D2 addon VPKs are single-file archives, so entry data lives in the same file; the game's own
    /// pak01_dir.vpk points into its pak01_NNN.vpk archives, which Read follows too.
    /// </summary>
    public sealed class Vpk
    {
        public sealed class Entry
        {
            public string Path;
            /// <summary>The file's CRC32 as the VPK records it (0 when the tool that made it left it out).</summary>
            public uint Crc;
            public ushort ArchiveIndex;
            public uint Offset;
            public uint Length;
            public byte[] Preload;
        }

        const uint Signature = 0x55AA1234;
        const ushort InlineArchive = 0x7FFF;

        public string FilePath { get; }
        public Dictionary<string, Entry> Entries { get; } = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        long dataStart;

        Vpk(string path) { FilePath = path; }

        public static Vpk Open(string path)
        {
            var vpk = new Vpk(path);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                if (br.ReadUInt32() != Signature) throw new InvalidDataException("Not a VPK file");
                uint version = br.ReadUInt32();
                uint treeSize = br.ReadUInt32();
                int headerSize;
                if (version == 1) headerSize = 12;
                else if (version == 2) { br.ReadBytes(16); headerSize = 28; }
                else throw new InvalidDataException("Unsupported VPK version " + version);

                if (treeSize > fs.Length) throw new InvalidDataException("Corrupt VPK tree size");
                vpk.dataStart = headerSize + treeSize;

                while (true)
                {
                    string ext = ReadZ(br);
                    if (ext.Length == 0) break;
                    while (true)
                    {
                        string dir = ReadZ(br);
                        if (dir.Length == 0) break;
                        while (true)
                        {
                            string name = ReadZ(br);
                            if (name.Length == 0) break;

                            uint crc = br.ReadUInt32();
                            ushort preload = br.ReadUInt16();
                            var e = new Entry
                            {
                                Crc = crc,
                                ArchiveIndex = br.ReadUInt16(),
                                Offset = br.ReadUInt32(),
                                Length = br.ReadUInt32(),
                            };
                            br.ReadUInt16(); // terminator 0xFFFF
                            e.Preload = preload > 0 ? br.ReadBytes(preload) : null;

                            var sb = new StringBuilder();
                            if (dir != " ") sb.Append(dir).Append('/');
                            sb.Append(name);
                            if (ext != " ") sb.Append('.').Append(ext);
                            e.Path = sb.ToString().ToLowerInvariant();
                            vpk.Entries[e.Path] = e;
                        }
                    }
                }
            }
            return vpk;
        }

        /// <summary>Reads a file stored inside the VPK; returns null if missing, external, or larger than maxBytes.</summary>
        public byte[] Read(string path, int maxBytes = 2 * 1024 * 1024)
        {
            if (!Entries.TryGetValue(path, out var e)) return null;
            int total = (e.Preload?.Length ?? 0) + (int)e.Length;
            if (total > maxBytes) return null;
            // The game's own pak01_dir.vpk keeps the data in pak01_000.vpk, pak01_001.vpk...: offsets count from
            // the start of that file. Add-ons keep everything inline, after the directory tree.
            string file = FilePath;
            long start = dataStart;
            if (e.Length > 0 && e.ArchiveIndex != InlineArchive)
            {
                if (!FilePath.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase)) return e.Preload;
                file = FilePath.Substring(0, FilePath.Length - "dir.vpk".Length) + e.ArchiveIndex.ToString("000") + ".vpk";
                if (!File.Exists(file)) return e.Preload;
                start = 0;
            }

            var buf = new byte[total];
            int at = 0;
            if (e.Preload != null) { Buffer.BlockCopy(e.Preload, 0, buf, 0, e.Preload.Length); at = e.Preload.Length; }
            if (e.Length > 0)
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(start + e.Offset, SeekOrigin.Begin);
                    int read, left = (int)e.Length;
                    while (left > 0 && (read = fs.Read(buf, at, left)) > 0) { at += read; left -= read; }
                }
            }
            return buf;
        }

        public string ReadText(string path, int maxBytes = 2 * 1024 * 1024)
        {
            var b = Read(path, maxBytes);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        static string ReadZ(BinaryReader br)
        {
            var bytes = new List<byte>(64);
            byte b;
            while ((b = br.ReadByte()) != 0) bytes.Add(b);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
