using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Skin mods: add-on VPKs that replace how a survivor, weapon, item or infected looks. Each target here is
    /// one thing a player can skin; a mod covers a target when it replaces one of the target's models, or one of
    /// the materials those models use (texture-only skins are common). A VPK is all or nothing, so a mod covering
    /// several targets (a pack) is on for all of them or none.
    ///
    /// The game reads left4dead2\addonlist.txt when it starts, so the picks below are applied by turning add-ons
    /// on and off there (with the game closed). Skins are client-side: only the player who has them sees them.
    /// </summary>
    public static class Skins
    {
        // ================================================================== the targets

        public sealed class Target
        {
            public string Id, Section, Sub, Name;
            internal Regex Models;   // model paths without extension, e.g. models/survivors/survivor_gambler
            public override string ToString() => Name;
        }

        public const string Survivors = "Survivors", Weapons = "Weapons", Items = "Items", Infected = "Infected";
        public const string Special = "Special", Common = "Common";

        static Target T(string section, string sub, string id, string name, params string[] models) => new Target
        {
            Section = section, Sub = sub, Id = id, Name = name,
            Models = new Regex("^(" + string.Join("|", models) + ")$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };

        const string V = "models/v_models/", W = "models/w_models/weapons/", M = "models/weapons/melee/", A = "models/weapons/arms/", I = "models/infected/";
        const string Claw = "models/v_models/weapons/v_claw_";

        /// <summary>Every target, in the order the picker shows them. Model names checked against the game's VPKs (--skin-catalog).</summary>
        public static readonly IReadOnlyList<Target> Targets = new[]
        {
            T(Survivors, null, "nick", "Nick", "models/survivors/survivor_gambler", A + "v_arms_gambler_new"),
            T(Survivors, null, "rochelle", "Rochelle", "models/survivors/survivor_producer", A + "v_arms_producer_new"),
            T(Survivors, null, "coach", "Coach", "models/survivors/survivor_coach", A + "v_arms_coach_new"),
            T(Survivors, null, "ellis", "Ellis", "models/survivors/survivor_mechanic", A + "v_arms_mechanic_new"),
            T(Survivors, null, "bill", "Bill", "models/survivors/survivor_namvet", A + "v_arms_bill"),
            T(Survivors, null, "zoey", "Zoey", "models/survivors/survivor_teenangst(_light)?", A + "v_arms_zoey"),
            T(Survivors, null, "louis", "Louis", "models/survivors/survivor_manager", A + "v_arms_louis"),
            T(Survivors, null, "francis", "Francis", "models/survivors/survivor_biker(_light)?", A + "v_arms_francis"),

            T(Weapons, null, "pistol", "Pistols", V + "v_pistola?", V + "v_dual_pistola", W + "w_pistol_[ab](_dual)?", W + "w_dual_pistol_1911"),
            T(Weapons, null, "magnum", "Magnum", V + "v_desert_eagle", W + "w_desert_eagle"),
            T(Weapons, null, "uzi", "Uzi", V + "v_smg", W + "w_smg_uzi"),
            T(Weapons, null, "mac10", "Silenced SMG", V + "v_silenced_smg", W + "w_smg_a"),
            T(Weapons, null, "mp5", "MP5", V + "v_smg_mp5", W + "w_smg_mp5"),
            T(Weapons, null, "pumpshotgun", "Pump shotgun", V + "v_pumpshotgun", W + "w_shotgun"),
            T(Weapons, null, "chromeshotgun", "Chrome shotgun", V + "v_shotgun_chrome", W + "w_pumpshotgun_a"),
            T(Weapons, null, "autoshotgun", "Auto shotgun", V + "v_autoshotgun", W + "w_autoshot_m4super"),
            T(Weapons, null, "spas", "SPAS shotgun", V + "v_shotgun_spas", W + "w_shotgun_spas"),
            T(Weapons, null, "m16", "M16", V + "v_rifle", W + "w_rifle_m16a2"),
            T(Weapons, null, "ak47", "AK-47", V + "v_rifle_ak47", W + "w_rifle_ak47"),
            T(Weapons, null, "scar", "SCAR", V + "v_desert_rifle", W + "w_desert_rifle"),
            T(Weapons, null, "sg552", "SG552", V + "v_rif_sg552", W + "w_rifle_sg552"),
            T(Weapons, null, "huntingrifle", "Hunting rifle", V + "v_huntingrifle", W + "w_sniper_mini14"),
            T(Weapons, null, "militarysniper", "Military sniper", V + "v_sniper_military", W + "w_sniper_military"),
            T(Weapons, null, "scout", "Scout", V + "v_snip_scout", W + "w_sniper_scout"),
            T(Weapons, null, "awp", "AWP", V + "v_snip_awp", W + "w_sniper_awp"),
            T(Weapons, null, "m60", "M60", V + "v_m60", W + "w_m60"),
            T(Weapons, null, "grenadelauncher", "Grenade launcher", V + "v_grenade_launcher", W + "w_grenade_launcher"),
            T(Weapons, null, "chainsaw", "Chainsaw", M + "v_chainsaw", M + "w_chainsaw"),
            T(Weapons, null, "bat", "Baseball bat", M + "v_bat", M + "w_bat"),
            T(Weapons, null, "cricketbat", "Cricket bat", M + "v_cricket_bat", M + "w_cricket_bat"),
            T(Weapons, null, "crowbar", "Crowbar", M + "v_crowbar", M + "w_crowbar"),
            T(Weapons, null, "guitar", "Guitar", M + "v_electric_guitar", M + "w_electric_guitar"),
            T(Weapons, null, "fireaxe", "Fire axe", M + "v_fireaxe", M + "w_fireaxe"),
            T(Weapons, null, "fryingpan", "Frying pan", M + "v_frying_pan", M + "w_frying_pan"),
            T(Weapons, null, "golfclub", "Golf club", M + "v_golfclub", M + "w_golfclub"),
            T(Weapons, null, "katana", "Katana", M + "v_katana", M + "w_katana"),
            T(Weapons, null, "knife", "Knife", V + "v_knife_t", W + "w_knife_t"),
            T(Weapons, null, "machete", "Machete", M + "v_machete", M + "w_machete"),
            T(Weapons, null, "pitchfork", "Pitchfork", M + "v_pitchfork", M + "w_pitchfork"),
            T(Weapons, null, "riotshield", "Riot shield", M + "v_riotshield", M + "w_riotshield"),
            T(Weapons, null, "shovel", "Shovel", M + "v_shovel", M + "w_shovel"),
            T(Weapons, null, "tonfa", "Nightstick", M + "v_tonfa", M + "w_tonfa"),

            T(Items, null, "medkit", "First aid kit", V + "v_medkit", W + "w_eq_medkit"),
            T(Items, null, "pills", "Pills", V + "v_painpills", W + "w_eq_painpills"),
            T(Items, null, "adrenaline", "Adrenaline", V + "v_adrenaline", W + "w_eq_adrenaline"),
            T(Items, null, "defib", "Defibrillator", V + "v_defibrillator", W + "w_eq_defibrillator(_no_paddles|_paddles)?"),
            T(Items, null, "molotov", "Molotov", V + "v_molotov", W + "w_eq_molotov"),
            T(Items, null, "pipebomb", "Pipe bomb", V + "v_pipebomb", W + "w_eq_pipebomb"),
            T(Items, null, "bile", "Bile jar", V + "v_bile_flask", W + "w_eq_bile_flask"),
            T(Items, null, "incendiaryammo", "Incendiary ammo", V + "v_incendiary_ammopack", W + "w_eq_incendiary_ammopack"),
            T(Items, null, "explosiveammo", "Explosive ammo", V + "v_explosive_ammopack", W + "w_eq_explosive_ammopack"),
            T(Items, null, "gascan", "Gas can", "models/props_junk/gascan001a"),
            T(Items, null, "propane", "Propane tank", "models/props_junk/propanecanister001a"),
            T(Items, null, "oxygen", "Oxygen tank", "models/props_equipment/oxygentank01"),
            T(Items, null, "fireworks", "Fireworks crate", "models/props_junk/explosive_box001"),
            T(Items, null, "gnome", "Gnome", "models/props_junk/gnome", M + "v_gnome"),
            T(Items, null, "cola", "Cola", V + "v_cola", W + "w_cola"),

            T(Infected, Special, "hunter", "Hunter", I + "hunter(_l4d1)?", Claw + "hunter(_l4d1)?"),
            T(Infected, Special, "smoker", "Smoker", I + "smoker(_l4d1)?", Claw + "smoker(_l4d1)?"),
            T(Infected, Special, "boomer", "Boomer", I + "boomer(_l4d1)?", I + "boomette", Claw + "boomer(_l4d1)?", I + "limbs/exploded_boom(er|ette)[a-z0-9_]*"),
            T(Infected, Special, "charger", "Charger", I + "charger", A + "v_charger_arms"),
            T(Infected, Special, "jockey", "Jockey", I + "jockey", A + "v_jockey_arms"),
            T(Infected, Special, "spitter", "Spitter", I + "spitter", A + "v_spitter_arms"),
            T(Infected, Special, "tank", "Tank", I + "hulk(_dlc3|_l4d1)?", Claw + "hulk(_dlc3|_l4d1)?"),
            T(Infected, Special, "witch", "Witch", I + "witch(_bride)?"),

            T(Infected, Common, "common", "Common infected",
                I + "common_(?!male_(ceda|clown|mud|roadcrew|riot|fallen_survivor|jimmy)|shadertest|test|morph_test)[a-z0-9_]+", I + "limbs/(?!exploded_boom)[a-z0-9_]+", I + "gibs/gibs"),
            T(Infected, Common, "ceda", "CEDA", I + "common_male_ceda(_l4d1)?", I + "common_infected_w_ceda_faceplate", I + "cim_faceplate"),
            T(Infected, Common, "clown", "Clown", I + "common_male_clown"),
            T(Infected, Common, "mudmen", "Mudmen", I + "common_male_mud(_l4d1)?"),
            T(Infected, Common, "roadcrew", "Road crew", I + "common_male_roadcrew(_l4d1|_rain)?"),
            T(Infected, Common, "riot", "Riot", I + "common_male_riot(_l4d1)?", I + "cim_riot_faceplate"),
            T(Infected, Common, "fallen", "Fallen survivor", I + "common_male_fallen_survivor(_l4d1)?", I + "cim_fallen_survivor_pocket0[1-3]"),
            T(Infected, Common, "jimmy", "Jimmy Gibbs", I + "common_male_jimmy"),
        };

        public static Target Get(string id) => Targets.FirstOrDefault(t => t.Id == id);

        // ================================================================== the catalog of materials

        /// <summary>Material (vmt) and texture (vtf) paths -> the targets whose models use them, from skin_targets.json.</summary>
        static Dictionary<string, string[]> materials;
        /// <summary>A material used by more targets than this says nothing about which one a mod skins (shared shells, gore...).</summary>
        const int MaxSharedTargets = 3;

        static Dictionary<string, string[]> Materials
        {
            get
            {
                if (materials != null) return materials;
                var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("skin_targets.json"))
                    using (var r = new StreamReader(s))
                        if (Json.Parse(r.ReadToEnd()) is Dictionary<string, object> root && root.TryGetValue("materials", out var m) && m is Dictionary<string, object> mats)
                            foreach (var kv in mats)
                                if (kv.Value is System.Collections.IEnumerable ids && !(kv.Value is string)) map[kv.Key] = ids.Cast<object>().Select(o => o.ToString()).ToArray();
                }
                catch { /* no catalog: model replacements are still recognised */ }
                return materials = map;
            }
        }

        /// <summary>
        /// --skin-catalog: reads the game's own VPKs, checks every target's models exist, and maps each target to
        /// the materials and textures its models use (the MDL texture tables, then each vmt's textures).
        /// Writes skin_targets.json; returns a report.
        /// </summary>
        public static string BuildCatalog(string gameRoot, string outFile)
        {
            var log = new StringBuilder();
            var paks = Directory.GetFiles(gameRoot, "pak01_dir.vpk", SearchOption.AllDirectories)
                .OrderBy(p => p.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0).ToList();   // update last: it wins
            var where = new Dictionary<string, Vpk>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in paks)
            {
                var v = Vpk.Open(p);
                foreach (var e in v.Entries.Keys) where[e] = v;
            }
            log.AppendLine($"{paks.Count} game VPKs, {where.Count} files");

            byte[] Read(string path) => where.TryGetValue(path, out var v) ? v.Read(path, 64 * 1024 * 1024) : null;
            var mdls = where.Keys.Where(k => k.EndsWith(".mdl")).Select(k => k.Substring(0, k.Length - 4)).ToList();
            var map = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
            int missing = 0;
            foreach (var t in Targets)
            {
                var models = mdls.Where(m => t.Models.IsMatch(m)).ToList();
                var mats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in models) foreach (var vmt in MdlMaterials(Read(m + ".mdl"), where.ContainsKey)) mats.Add(vmt);
                foreach (var vmt in mats.ToList()) foreach (var vtf in VmtTextures(vmt, Read, where.ContainsKey)) mats.Add(vtf);
                foreach (var f in mats) { if (!map.TryGetValue(f, out var s)) map[f] = s = new SortedSet<string>(); s.Add(t.Id); }
                if (models.Count == 0 || mats.Count == 0) missing++;
                log.AppendLine($"{(models.Count == 0 || mats.Count == 0 ? "MISSING " : "")}{t.Section}{(t.Sub != null ? "/" + t.Sub : "")} {t.Name}: {models.Count} model(s), {mats.Count} material file(s)");
            }

            var sb = new StringBuilder("{\n  \"generated\": \"" + DateTime.UtcNow.ToString("yyyy-MM-dd") + "\",\n  \"materials\": {\n");
            sb.Append(string.Join(",\n", map.OrderBy(k => k.Key).Select(kv => $"    \"{kv.Key}\": [{string.Join(", ", kv.Value.Select(id => "\"" + id + "\""))}]")));
            sb.Append("\n  }\n}\n");
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            log.AppendLine($"catalog: {map.Count} material files for {Targets.Count} targets, {missing} target(s) missing models or materials");
            return log.ToString();
        }

        /// <summary>The vmt paths an MDL uses: its texture names under each of its material folders (studiohdr_t tables).</summary>
        static IEnumerable<string> MdlMaterials(byte[] mdl, Func<string, bool> exists)
        {
            if (mdl == null || mdl.Length < 220 || BitConverter.ToInt32(mdl, 0) != 0x54534449) yield break;   // "IDST"
            int numTextures = BitConverter.ToInt32(mdl, 204), textureIndex = BitConverter.ToInt32(mdl, 208);
            int numCd = BitConverter.ToInt32(mdl, 212), cdIndex = BitConverter.ToInt32(mdl, 216);
            string Z(int at)
            {
                if (at <= 0 || at >= mdl.Length) return "";
                int end = Array.IndexOf(mdl, (byte)0, at);
                return Encoding.ASCII.GetString(mdl, at, (end < 0 ? mdl.Length : end) - at);
            }
            var folders = new List<string>();
            for (int i = 0; i < numCd && cdIndex + i * 4 + 4 <= mdl.Length; i++) folders.Add(Z(BitConverter.ToInt32(mdl, cdIndex + i * 4)));
            for (int i = 0; i < numTextures; i++)
            {
                int tex = textureIndex + i * 64;   // mstudiotexture_t is 64 bytes; its name offset counts from the struct
                if (tex + 4 > mdl.Length) break;
                var name = Z(tex + BitConverter.ToInt32(mdl, tex));
                foreach (var folder in folders.DefaultIfEmpty(""))
                {
                    var path = ("materials/" + folder + "/" + name + ".vmt").Replace('\\', '/').ToLowerInvariant();
                    while (path.Contains("//")) path = path.Replace("//", "/");
                    if (exists(path)) yield return path;
                }
            }
        }

        static readonly Regex VmtValue = new Regex("\"?\\$?([A-Za-z0-9_]+)\"?\\s+\"([^\"\\r\\n]+)\"", RegexOptions.Compiled);

        /// <summary>The vtf textures (and an included base vmt's) a vmt points at.</summary>
        static IEnumerable<string> VmtTextures(string vmt, Func<string, byte[]> read, Func<string, bool> exists, int depth = 0)
        {
            var bytes = read(vmt);
            if (bytes == null || depth > 2) yield break;
            foreach (Match m in VmtValue.Matches(Encoding.ASCII.GetString(bytes)))
            {
                var key = m.Groups[1].Value.ToLowerInvariant();
                var value = m.Groups[2].Value.Replace('\\', '/').Trim().ToLowerInvariant();
                if (key == "include")
                {
                    if (exists(value)) { yield return value; foreach (var t in VmtTextures(value, read, exists, depth + 1)) yield return t; }
                    continue;
                }
                if (!value.Contains("/")) continue;
                var vtf = "materials/" + value.TrimStart('/') + (value.EndsWith(".vtf") ? "" : ".vtf");
                if (exists(vtf)) yield return vtf;
            }
        }

        // ================================================================== what a mod covers

        /// <summary>The targets a mod's files replace: a target's models (any of their files), or a material only a few targets use.</summary>
        public static List<Target> Coverage(IEnumerable<string> files)
        {
            var ids = new HashSet<string>();
            foreach (var f in files)
            {
                if (f.StartsWith("models/"))
                {
                    // models/x/y.mdl, .vvd, .dx90.vtx, .phy...: the model path without its extensions
                    int slash = f.LastIndexOf('/'), dot = f.IndexOf('.', slash + 1);
                    var model = dot < 0 ? f : f.Substring(0, dot);
                    foreach (var t in Targets) if (t.Models.IsMatch(model)) ids.Add(t.Id);
                }
                else if (f.StartsWith("materials/") && Materials.TryGetValue(f, out var users) && users.Length <= MaxSharedTargets)
                    foreach (var id in users) ids.Add(id);
            }
            return Targets.Where(t => ids.Contains(t.Id)).ToList();
        }

        // ================================================================== the add-ons in a game

        public sealed class Mod
        {
            public string Key;        // the addonlist.txt key: "workshop\123.vpk" or "name.vpk"
            public string Path, Title;
            public bool Enabled;
            /// <summary>Also a campaign, mutation or script mod: the picker never turns it off.</summary>
            public bool Locked;
            public bool WeaponScripts, NotJustSkin;
            public List<Target> Targets = new List<Target>();
            public List<(Severity sev, string text)> Notes = new List<(Severity, string)>();
            public bool Pack => Targets.Count > 1;
            public string WorkshopId => Key.StartsWith("workshop\\", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileNameWithoutExtension(Key) : null;
            public override string ToString() => Title;
        }

        public sealed class TargetState
        {
            public Target Target;
            public List<Mod> Mods = new List<Mod>();
            public List<Mod> On => Mods.Where(m => m.Enabled).ToList();
            /// <summary>More than one skin on: the game uses one of them, and which isn't predictable.</summary>
            public bool Conflict => On.Count > 1;
            public Mod Active => On.Count == 1 ? On[0] : null;
        }

        public sealed class Inventory
        {
            public string GameDir;
            public List<Mod> Mods = new List<Mod>();
            public Dictionary<string, TargetState> ByTarget = new Dictionary<string, TargetState>();
            /// <summary>Enabled add-ons that skin nothing here (maps, sounds, UI...), for the footnote.</summary>
            public int Others;
            public Mod Find(string key) => Mods.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reads every add-on in a game's left4dead2 folder and sorts the skins by target. Protected add-ons are left out.</summary>
        public static Inventory Scan(string gameDir, Manifest manifest)
        {
            var inv = new Inventory { GameDir = gameDir };
            foreach (var t in Targets) inv.ByTarget[t.Id] = new TargetState { Target = t };
            if (gameDir == null || !Directory.Exists(gameDir)) return inv;
            var list = Compat.ReadAddonList(gameDir);
            var addons = System.IO.Path.Combine(gameDir, "addons");
            var vpks = new List<string>();
            if (Directory.Exists(addons)) vpks.AddRange(Directory.GetFiles(addons, "*.vpk"));
            var ws = System.IO.Path.Combine(addons, "workshop");
            if (Directory.Exists(ws)) vpks.AddRange(Directory.GetFiles(ws, "*.vpk"));
            foreach (var vpk in vpks.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
            {
                if (Compat.IsProtected(vpk, manifest)) continue;
                var key = Compat.AddonListKey(gameDir, vpk);
                bool enabled = !list.TryGetValue(key, out var on) || on;   // no entry: the game turns it on
                var info = Compat.ReadAddon(vpk);
                var covered = info.Error == null ? Coverage(info.Files) : new List<Target>();
                if (covered.Count == 0) { if (enabled) inv.Others++; continue; }
                var mod = new Mod
                {
                    Key = key, Path = vpk, Enabled = enabled, Targets = covered,
                    Title = Compat.AddonTitle(vpk, info),
                    Locked = info.Campaign || info.Mutation || info.Scripts,
                    WeaponScripts = info.WeaponScripts,
                    NotJustSkin = info.Campaign || info.Mutation || info.Scripts || info.Talker || info.Ui,
                };
                if (info.Campaign) mod.Notes.Add((Severity.Info, "Part of a campaign, so it stays on: turn it off on the Mods tab if you need to."));
                else if (info.Mutation || info.Scripts) mod.Notes.Add((Severity.Info, "Also a script or mutation mod, so it stays on: turn it off on the Mods tab if you need to."));
                if (info.WeaponScripts) mod.Notes.Add((Severity.Info, "Also changes weapon scripts, which can change how the weapon handles."));
                if (info.Ui) mod.Notes.Add((Severity.Info, "Also changes menus or the HUD."));
                foreach (var n in info.ScriptNotes) mod.Notes.Add(n);
                inv.Mods.Add(mod);
                foreach (var t in covered) inv.ByTarget[t.Id].Mods.Add(mod);
            }
            return inv;
        }

        // ================================================================== picking

        /// <summary>What a pick changes: add-ons turned on and off, and what that means for the other targets.</summary>
        public sealed class ChangeSet
        {
            public Target Target;
            /// <summary>The mod picked, or null for the game's default look.</summary>
            public Mod Pick;
            public List<Mod> TurnOn = new List<Mod>(), TurnOff = new List<Mod>();
            /// <summary>Targets the pick sets (the pick's targets, or just Target for the default).</summary>
            public List<Target> Sets = new List<Target>();
            /// <summary>Other targets that go back to the game's default because a mod turned off skinned them too.</summary>
            public List<Target> BackToDefault = new List<Target>();
            /// <summary>Campaign or script mods that also skin the picked targets: they stay on, so they may still show.</summary>
            public List<Mod> StaysOn = new List<Mod>();
            /// <summary>Whether it touches more than the target clicked, which is worth a question first.</summary>
            public bool Wider => Sets.Count > 1 || BackToDefault.Count > 0 || StaysOn.Count > 0;
            public Dictionary<string, bool> Changes => TurnOn.ToDictionary(m => m.Key, m => true).Concat(TurnOff.ToDictionary(m => m.Key, m => false)).ToDictionary(kv => kv.Key, kv => kv.Value);
            public bool Nothing => TurnOn.Count == 0 && TurnOff.Count == 0;
        }

        /// <summary>The changes for picking a mod (or the default, pick null) for one target.</summary>
        public static ChangeSet Plan(Inventory inv, Target target, Mod pick)
        {
            var cs = new ChangeSet { Target = target, Pick = pick };
            cs.Sets.AddRange(pick != null ? pick.Targets : new List<Target> { target });
            if (pick != null && !pick.Enabled) cs.TurnOn.Add(pick);
            foreach (var t in cs.Sets)
                foreach (var m in inv.ByTarget[t.Id].Mods.Where(m => m != pick && m.Enabled))
                {
                    if (m.Locked) { if (!cs.StaysOn.Contains(m)) cs.StaysOn.Add(m); }
                    else if (!cs.TurnOff.Contains(m)) cs.TurnOff.Add(m);
                }
            // what the mods turned off also skinned: back to default unless another skin stays on for it
            foreach (var t in cs.TurnOff.SelectMany(m => m.Targets).Distinct().Where(t => !cs.Sets.Contains(t)))
                if (!inv.ByTarget[t.Id].Mods.Any(m => m.Enabled && !cs.TurnOff.Contains(m)) && !cs.BackToDefault.Contains(t))
                    cs.BackToDefault.Add(t);
            return cs;
        }

        /// <summary>Writes a change set to addonlist.txt and remembers the picks. The game has to be closed.</summary>
        public static void Apply(Inventory inv, ChangeSet cs, SkinPrefs prefs)
        {
            if (Launcher.GameRunning()) throw new InvalidOperationException("Close Left 4 Dead 2 first: it rewrites its add-on list when it exits.");
            Compat.SetAddonsEnabled(inv.GameDir, cs.Changes);
            foreach (var m in cs.TurnOn) m.Enabled = true;
            foreach (var m in cs.TurnOff) m.Enabled = false;
            foreach (var t in cs.Sets) prefs.Picks[t.Id] = cs.Pick?.Key ?? "";
            foreach (var t in cs.BackToDefault) prefs.Picks[t.Id] = "";
            prefs.Save();
        }

        // ================================================================== re-applying the picks at launch

        /// <summary>
        /// Before the game starts: puts the remembered picks back in addonlist.txt (the game's Add-ons menu, or a new
        /// Workshop subscription, may have changed it). A newly installed skin for a picked target is asked about
        /// (useNew returns true to switch to it); without asking it stays off. Returns what changed, for the log.
        /// </summary>
        public static List<string> Reapply(string gameDir, Manifest manifest, SkinPrefs prefs, Func<Mod, Target, bool> useNew = null)
        {
            var log = new List<string>();
            if (gameDir == null || !Directory.Exists(gameDir) || Launcher.GameRunning()) return log;
            var inv = Scan(gameDir, manifest);
            bool firstLook = prefs.Seen.Count == 0;
            var fresh = inv.Mods.Where(m => !prefs.HasSeen(m)).ToList();

            var on = new HashSet<Mod>(inv.Mods.Where(m => m.Enabled));
            var wantOn = new HashSet<Mod>();
            var wantOff = new HashSet<Mod>();
            foreach (var kv in prefs.Picks.ToList())
            {
                var target = Get(kv.Key);
                if (target == null) continue;
                var state = inv.ByTarget[target.Id];
                Mod pick = null;
                if (kv.Value != "")
                {
                    pick = inv.Find(kv.Value);
                    if (pick == null) { log.Add($"{target.Name}: its skin ({kv.Value}) is gone, so the game's own look is used."); continue; }
                }
                // a skin installed since last time, switched on by the game: ask before turning it off
                foreach (var m in state.Mods.Where(m => m != pick && m.Enabled && !m.Locked && fresh.Contains(m) && !firstLook).ToList())
                    if (useNew != null && useNew(m, target))
                    {
                        pick = m;
                        foreach (var t in m.Targets) prefs.Picks[t.Id] = m.Key;
                        log.Add($"{target.Name}: switched to the new skin {m.Title}.");
                    }
                if (pick != null) wantOn.Add(pick);
                foreach (var m in state.Mods.Where(m => m != pick && !m.Locked)) wantOff.Add(m);
            }
            wantOff.ExceptWith(wantOn);   // a pack picked for one target wins over another target's "off"

            var changes = new Dictionary<string, bool>();
            foreach (var m in wantOn.Where(m => !on.Contains(m))) { changes[m.Key] = true; log.Add($"{string.Join(", ", m.Targets.Select(t => t.Name))}: back to {m.Title}."); }
            foreach (var m in wantOff.Where(m => on.Contains(m))) { changes[m.Key] = false; log.Add($"{m.Title} turned off again (it isn't your pick for {string.Join(", ", m.Targets.Where(t => prefs.Picks.ContainsKey(t.Id)).Select(t => t.Name))})."); }
            if (changes.Count > 0) Compat.SetAddonsEnabled(gameDir, changes);
            foreach (var m in inv.Mods) prefs.MarkSeen(m);
            prefs.Save();
            return log;
        }

        /// <summary>
        /// Before a launch: targets with more than one skin on and no pick to settle them, which the picker can fix (at
        /// most one of those skins is a campaign or script mod it never turns off). Overlaps the user already chose to
        /// keep (the same skins on as then) aren't listed again.
        /// </summary>
        public static List<TargetState> Overlaps(string gameDir, Manifest manifest, SkinPrefs prefs)
        {
            if (gameDir == null || !Directory.Exists(gameDir)) return new List<TargetState>();
            var inv = Scan(gameDir, manifest);
            return Targets.Select(t => inv.ByTarget[t.Id])
                .Where(s => s.Conflict && !prefs.Picks.ContainsKey(s.Target.Id) && s.On.Count(m => m.Locked) <= 1
                         && !(prefs.KeepOverlap.TryGetValue(s.Target.Id, out var kept) && kept == OverlapStamp(s)))
                .ToList();
        }

        /// <summary>"Launch anyway": these overlaps stay as they are until another skin for the same thing turns up.</summary>
        public static void KeepOverlaps(IEnumerable<TargetState> overlaps, SkinPrefs prefs)
        {
            foreach (var s in overlaps) prefs.KeepOverlap[s.Target.Id] = OverlapStamp(s);
            prefs.Save();
        }

        static string OverlapStamp(TargetState s) => string.Join("|", s.On.Select(m => m.Key.ToLowerInvariant()).OrderBy(k => k));
    }

    /// <summary>The skin picks, remembered in %LOCALAPPDATA%\L4D2-8P-Manager\skins.json (one file for both sides of the app).</summary>
    public sealed class SkinPrefs
    {
        /// <summary>Target id -> the picked mod's addonlist key, or "" for the game's default look.</summary>
        public Dictionary<string, string> Picks { get; set; } = new Dictionary<string, string>();
        /// <summary>Packs the user said not to ask about again.</summary>
        public List<string> NoAsk { get; set; } = new List<string>();
        /// <summary>Mods already seen (key -> size and date), so new ones can be told apart.</summary>
        public Dictionary<string, string> Seen { get; set; } = new Dictionary<string, string>();
        /// <summary>Target id -> the skins left on together when the user chose "Launch anyway".</summary>
        public Dictionary<string, string> KeepOverlap { get; set; } = new Dictionary<string, string>();
        /// <summary>Add-on pairs ("key|key") left on together when asked before a launch, with how many files they share.</summary>
        public Dictionary<string, string> KeepClash { get; set; } = new Dictionary<string, string>();

        /// <summary>Set by tests and audits: never write the file.</summary>
        public static bool ReadOnly;
        /// <summary>Tests point this elsewhere.</summary>
        public static string FilePath = Path.Combine(Paths.AppData, "skins.json");

        public static SkinPrefs Load() => Json.Load<SkinPrefs>(FilePath);
        public void Save() { if (!ReadOnly) Json.Save(FilePath, this); }

        static string Stamp(Skins.Mod m)
        {
            try { var fi = new FileInfo(m.Path); return fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; } catch { return ""; }
        }
        public bool HasSeen(Skins.Mod m) => Seen.ContainsKey(m.Key);
        public void MarkSeen(Skins.Mod m) => Seen[m.Key] = Stamp(m);
    }
}
