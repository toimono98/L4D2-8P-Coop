using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// The Packs tab: add-ons that skin more than one thing (any mix of survivors, weapons, items and infected), and
    /// Workshop collections the player added by link. Either can be used as a whole (on, with the other skins for
    /// the same things off) or turned off as a whole.
    /// </summary>
    public static class Packs
    {
        /// <summary>The skin add-ons that cover more than one target, the biggest first.</summary>
        public static List<Skins.Mod> Of(Skins.Inventory inv) =>
            inv == null ? new List<Skins.Mod>() : inv.Mods.Where(m => m.Pack).OrderByDescending(m => m.Targets.Count).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToList();

        // ================================================================== collections in a game folder

        public sealed class Item
        {
            public string Id, Key, Title;
            /// <summary>Downloaded by the game into addons\workshop.</summary>
            public bool Installed;
            public bool Enabled;
            /// <summary>The skin add-on it is, when it is one.</summary>
            public Skins.Mod Skin;
            /// <summary>The HUD add-on, the lobby mod and the like: never turned off from here.</summary>
            public bool Protected;
        }

        public sealed class CollectionState
        {
            public SkinPrefs.Collection Saved;
            public List<Item> Items = new List<Item>();
            public List<Item> Installed => Items.Where(i => i.Installed).ToList();
            public int On => Items.Count(i => i.Enabled);
            public int Missing => Items.Count(i => !i.Installed);
        }

        /// <summary>Which of a collection's items the game has downloaded, and which are on.</summary>
        public static CollectionState State(string gameDir, Skins.Inventory inv, Manifest manifest, SkinPrefs.Collection c)
        {
            var st = new CollectionState { Saved = c };
            var list = gameDir == null ? new Dictionary<string, bool>() : Compat.ReadAddonList(gameDir);
            foreach (var id in c.Items)
            {
                var key = "workshop\\" + id + ".vpk";
                var path = gameDir == null ? null : Path.Combine(gameDir, "addons", "workshop", id + ".vpk");
                var item = new Item { Id = id, Key = key, Installed = path != null && File.Exists(path) };
                if (item.Installed)
                {
                    item.Enabled = !list.TryGetValue(key, out var on) || on;   // no entry: the game turns it on
                    item.Skin = inv?.Find(key);
                    item.Protected = Compat.IsProtected(path, manifest);
                    item.Title = item.Skin?.Title ?? Compat.AddonTitle(path, Compat.ReadAddon(path));
                }
                st.Items.Add(item);
            }
            return st;
        }

        // ================================================================== using and turning off

        /// <summary>What using or turning off a pack or collection changes.</summary>
        public sealed class Plan
        {
            public string What;
            /// <summary>addonlist key -> title.</summary>
            public Dictionary<string, string> TurnOn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> TurnOff = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The skin each target ends up with, remembered as the pick.</summary>
            public Dictionary<Skins.Target, Skins.Mod> Picks = new Dictionary<Skins.Target, Skins.Mod>();
            /// <summary>Other targets that go back to the game's look because a skin turned off covered them too.</summary>
            public List<Skins.Target> BackToDefault = new List<Skins.Target>();
            /// <summary>Targets two of the collection's own skins cover: the game shows one of them (asked about at launch).</summary>
            public List<Skins.Target> Shared = new List<Skins.Target>();
            /// <summary>Campaign or script mods that also skin these targets: they stay on, so they may still show.</summary>
            public List<Skins.Mod> StaysOn = new List<Skins.Mod>();
            /// <summary>Picks dropped because the skin they name is turned off.</summary>
            public List<string> DropPicksFor = new List<string>();
            public bool Nothing => TurnOn.Count == 0 && TurnOff.Count == 0;
        }

        /// <summary>A pack on for everything it skins, and the other skins for those things off (Skins.Plan does the same for one target).</summary>
        public static Plan UsePack(Skins.Inventory inv, Skins.Mod pack)
        {
            var cs = Skins.Plan(inv, pack.Targets[0], pack);
            var p = new Plan { What = pack.Title };
            foreach (var m in cs.TurnOn) p.TurnOn[m.Key] = m.Title;
            foreach (var m in cs.TurnOff) p.TurnOff[m.Key] = m.Title;
            foreach (var t in pack.Targets) p.Picks[t] = pack;
            p.BackToDefault.AddRange(cs.BackToDefault);
            p.StaysOn.AddRange(cs.StaysOn);
            return p;
        }

        /// <summary>
        /// Every downloaded item of a collection on (its skins, and anything else in it: maps, sounds, HUDs), and skins
        /// from outside it for the same things off. A target only one of its skins covers is remembered as the pick.
        /// </summary>
        public static Plan UseCollection(Skins.Inventory inv, CollectionState st)
        {
            var p = new Plan { What = st.Saved.Title };
            foreach (var i in st.Installed.Where(i => !i.Enabled)) p.TurnOn[i.Key] = i.Title;
            var own = new HashSet<Skins.Mod>(st.Installed.Where(i => i.Skin != null).Select(i => i.Skin));
            foreach (var t in own.SelectMany(m => m.Targets).Distinct())
            {
                var mine = own.Where(m => m.Targets.Contains(t)).ToList();
                if (mine.Count == 1) p.Picks[t] = mine[0]; else p.Shared.Add(t);
                foreach (var m in inv.ByTarget[t.Id].Mods.Where(m => m.Enabled && !own.Contains(m)))
                {
                    if (m.Locked) { if (!p.StaysOn.Contains(m)) p.StaysOn.Add(m); }
                    else p.TurnOff[m.Key] = m.Title;
                }
            }
            return p;
        }

        /// <summary>A pack, or every item of a collection, off; picks naming them are dropped.</summary>
        public static Plan TurnOff(string what, IEnumerable<(string key, string title, bool on, bool locked)> addons)
        {
            var p = new Plan { What = what };
            foreach (var a in addons.Where(a => a.on && !a.locked)) p.TurnOff[a.key] = a.title;
            p.DropPicksFor.AddRange(addons.Where(a => !a.locked).Select(a => a.key));
            return p;
        }

        public static Plan TurnOffPack(Skins.Mod pack) => TurnOff(pack.Title, new[] { (pack.Key, pack.Title, pack.Enabled, pack.Locked) });

        public static Plan TurnOffCollection(CollectionState st) =>
            TurnOff(st.Saved.Title, st.Installed.Select(i => (i.Key, i.Title, i.Enabled, i.Protected || i.Skin?.Locked == true)));

        /// <summary>Writes a plan to addonlist.txt and the picks. The game has to be closed.</summary>
        public static void Apply(string gameDir, Plan p, SkinPrefs prefs)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.");
            var changes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in p.TurnOn.Keys) changes[k] = true;
            foreach (var k in p.TurnOff.Keys) changes[k] = false;
            if (changes.Count > 0) Compat.SetAddonsEnabled(gameDir, changes);
            foreach (var kv in p.Picks) prefs.Picks[kv.Key.Id] = kv.Value.Key;
            foreach (var t in p.Shared) prefs.Picks.Remove(t.Id);
            foreach (var t in p.BackToDefault) prefs.Picks[t.Id] = "";
            foreach (var key in p.DropPicksFor)
                foreach (var pick in prefs.Picks.Where(x => string.Equals(x.Value, key, StringComparison.OrdinalIgnoreCase)).ToList())
                    prefs.Picks.Remove(pick.Key);
            prefs.Save();
        }
    }
}
