using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Reads metadata from a compiled SourcePawn plugin (.smx): plugin info, the natives it
    /// calls, and the extensions/libraries it declares as dependencies.
    /// </summary>
    public sealed class Smx
    {
        public sealed class Dependency
        {
            public string Name;
            public string File;
            public bool Required;
        }

        public string Name, Author, Version, Description, Url;
        public List<string> Natives { get; } = new List<string>();
        public List<Dependency> Extensions { get; } = new List<Dependency>();
        public List<Dependency> Libraries { get; } = new List<Dependency>();
        byte[] data;      // the .data section's initialised memory
        byte[] namesTable;

        const uint Magic = 0x53504646; // "FFPS"

        public static Smx Open(string path)
        {
            var raw = File.ReadAllBytes(path);
            var smx = new Smx();
            smx.Load(raw);
            return smx;
        }

        /// <summary>True if the plugin's data section contains this literal string (e.g. a cvar name).</summary>
        public bool ContainsString(string s)
        {
            if (data == null) return false;
            var needle = Encoding.UTF8.GetBytes(s + "\0");
            return IndexOf(data, needle) >= 0;
        }

        void Load(byte[] raw)
        {
            if (raw.Length < 24 || BitConverter.ToUInt32(raw, 0) != Magic) throw new InvalidDataException("Not an SMX file");
            byte compression = raw[6];
            int diskSize = BitConverter.ToInt32(raw, 7);
            int imageSize = BitConverter.ToInt32(raw, 11);
            byte sections = raw[15];
            int stringTab = BitConverter.ToInt32(raw, 16);
            int dataOffs = BitConverter.ToInt32(raw, 20);

            byte[] image;
            if (compression == 1)
            {
                image = new byte[imageSize];
                Buffer.BlockCopy(raw, 0, image, 0, dataOffs);
                // zlib stream: skip the 2-byte header, inflate the rest.
                using (var ms = new MemoryStream(raw, dataOffs + 2, Math.Min(diskSize, raw.Length) - dataOffs - 2))
                using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    int at = dataOffs, n;
                    while (at < imageSize && (n = z.Read(image, at, imageSize - at)) > 0) at += n;
                }
            }
            else image = raw;

            var sectionMap = new Dictionary<string, (int offs, int size)>();
            for (int i = 0; i < sections; i++)
            {
                int h = 24 + i * 12;
                string name = ReadZ(image, stringTab + BitConverter.ToInt32(image, h));
                sectionMap[name] = (BitConverter.ToInt32(image, h + 4), BitConverter.ToInt32(image, h + 8));
            }

            if (sectionMap.TryGetValue(".names", out var names))
            {
                namesTable = new byte[names.size];
                Buffer.BlockCopy(image, names.offs, namesTable, 0, names.size);
            }

            if (sectionMap.TryGetValue(".data", out var ds))
            {
                // sp_file_data_t { uint datasize; uint memsize; uint data; }
                int dataSize = BitConverter.ToInt32(image, ds.offs);
                int dataStart = ds.offs + BitConverter.ToInt32(image, ds.offs + 8);
                data = new byte[dataSize];
                Buffer.BlockCopy(image, dataStart, data, 0, Math.Min(dataSize, image.Length - dataStart));
            }

            if (sectionMap.TryGetValue(".natives", out var nat) && namesTable != null)
            {
                for (int p = nat.offs; p + 4 <= nat.offs + nat.size; p += 4)
                    Natives.Add(ReadZ(namesTable, BitConverter.ToInt32(image, p)));
            }

            if (sectionMap.TryGetValue(".pubvars", out var pv) && namesTable != null && data != null)
            {
                for (int p = pv.offs; p + 8 <= pv.offs + pv.size; p += 8)
                {
                    int addr = BitConverter.ToInt32(image, p);
                    string name = ReadZ(namesTable, BitConverter.ToInt32(image, p + 4));
                    try { ReadPubvar(name, addr); } catch { /* unusual layout: skip this pubvar */ }
                }
            }
        }

        void ReadPubvar(string name, int addr)
        {
            if (name == "myinfo")
            {
                Name = DataString(Cell(addr, 0));
                Description = DataString(Cell(addr, 1));
                Author = DataString(Cell(addr, 2));
                Version = DataString(Cell(addr, 3));
                Url = DataString(Cell(addr, 4));
            }
            else if (name.StartsWith("__ext_"))
            {
                // struct Extension { const char[] name; const char[] file; bool autoload; bool required; }
                Extensions.Add(new Dependency { Name = DataString(Cell(addr, 0)), File = DataString(Cell(addr, 1)), Required = Cell(addr, 3) != 0 });
            }
            else if (name.StartsWith("__pl_"))
            {
                // struct SharedPlugin { const char[] name; const char[] file; bool required; }
                Libraries.Add(new Dependency { Name = DataString(Cell(addr, 0)), File = DataString(Cell(addr, 1)), Required = Cell(addr, 2) != 0 });
            }
        }

        int Cell(int addr, int index) => BitConverter.ToInt32(data, addr + index * 4);

        string DataString(int addr) => addr >= 0 && addr < data.Length ? ReadZ(data, addr) : null;

        static string ReadZ(byte[] buf, int at)
        {
            if (at < 0 || at >= buf.Length) return "";
            int end = at;
            while (end < buf.Length && buf[end] != 0) end++;
            return Encoding.UTF8.GetString(buf, at, end - at);
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i <= hay.Length - needle.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
