# L4D2 8-Player Co-op

One small Windows app with two sides, plus a setup that installs it. It needs no extra runtime.

- **Host** sets up, checks and launches 8-player co-op for Left 4 Dead 2 (the server, its mods, your game), and opens your router port so friends can get in.
- **Join** is for **friends who only join**: paste the host's invite, check the server and click Join. It needs no mods and changes nothing in their game.

Both sides live in one window: **HOST | JOIN** at the top switches between them at any time, and the window keeps its size and place (maximized included). The other side stays as it was (a running server or an install carries on), and the app opens on whichever side you used last.

**Easiest:** run `L4D2-8P-Setup.exe` and pick **Host a game** or **Join a friend** (which also offers the HUD add-on). Both install the same app; the choice sets the side it opens on. It installs just for you, with no admin prompt, into `%LOCALAPPDATA%\Programs\L4D2 8-Player Co-op` (you can change the folder), and adds Start menu and Desktop shortcuts. Its last page offers **Show me around** (ticked on a first install): the app opens with a short tour that spotlights the parts you'll use, one at a time (on the Host side: how you host, installing the server and mods, the mod scan, settings for your group, Start and invite, letting friends in and the switch; on the Join side: pasting the invite, checking the server, recent groups, the HUD add-on and the switch). Next or the arrow keys step through it, Esc skips it, and **? TOUR** at the top right shows it again. Opening a side for the first time without the Setup shows its tour too. Run the Setup again to update it or change the side it opens on. To remove it, open **Uninstall L4D2 8-Player Co-op** in the install folder or the Start menu (or Windows Settings › Apps › Installed apps › L4D2 8-Player Co-op, or run the Setup again and click Uninstall); your settings, the server and your game stay. Send friends the same Setup file. `L4D2-8P-Coop.exe` also runs on its own without installing (the first time, it opens on hosting).

**Upgrading from 1.10 or earlier**, where hosting and joining were two apps (`L4D2-8P-Manager.exe` and `L4D2-8P-Join.exe`): run the new Setup once. It replaces both with the one app and removes their old shortcuts. Settings, the server, recent groups and the HUD add-on carry over.

It follows the current community method from fbef0102's *8+ Survivors In Coop* guide (updated July 2026): Metamod + SourceMod + L4DToolZ + l4dmultislots, plus the fixes that keep games with 5+ survivors stable.

Both sides share a dark, in-game look inspired by the L4D2 safe room:

- condensed stencil headings, taped section labels and spray-painted notes
- blood red only on the main action of each screen, amber for warnings
- the **Play** tab shows your group as 8 survivor cards (Nick to Francis), tagged HOST / PLAYER / BOT / EMPTY

It's an original take on the style; no Valve art or logos are used.

## Quick start

1. Install with `L4D2-8P-Setup.exe` › Host a game (or run `L4D2-8P-Coop.exe`; you're on the Host side when HOST is lit at the top). It finds Left 4 Dead 2 through your Steam libraries. You can also click **Browse**.
2. **Setup**: pick how you'll host, then click **Install / update**. You'll see everything that will be downloaded, and where it comes from, before anything is installed. The line above the button says what's missing or out of date; **Advanced** lists every component, for extras.
3. **Mods**: the infection report lists every addon and plugin. Fix anything marked PROBLEM (see below).
4. **Play**: click **Launch L4D2 & join**. It starts the server and the game, waits until the game has loaded your Workshop add-ons (usually under a minute: joining sooner would leave your skins and campaigns out for the whole session), then puts you in. While the server runs, the survivor cards show who is connected (refreshed every 5 seconds); when it's stopped, they preview your settings. **Start server only** runs the server without your game.
5. Still on **Play**: click **Copy invite** and send it to friends along with `L4D2-8P-Setup.exe` (they pick Join a friend), or just `L4D2-8P-Coop.exe` (they click JOIN at the top). Over the internet, **Can friends reach you?** under the settings opens the port on your router.

The app opens maximized. At the bottom, a one-line status bar shows the latest message and progress; **Log** opens the full log, and it opens by itself when something goes wrong.

## Hosting modes

| | Dedicated server (recommended) | Host from my game (listen) |
|---|---|---|
| Players | up to 16 (8 by default) | up to 8 |
| Stability | best; SourceMod is built for this | lower; SourceMod doesn't officially support listen servers |
| Download | about 10 GB server from Valve via SteamCMD | none |
| VAC | your game stays normal | the game runs with `-insecure` while you host |

In listen mode the loader files (`metamod.vdf`, `l4dtoolz.vdf`) stay switched off. The tool switches them on only for a launch from the Play tab, then off again when you quit, so a normal Steam launch is always clean.

**VAC:** in dedicated mode the mods run only in the separate server program, which itself runs VAC-secured. Your game client stays stock, so you can still join other VAC servers. The Play tab shows this under the launch buttons. Switching to "Host from my game" asks you to confirm, because that mode needs `-insecure`.

## Who can join (Play tab)

| Button | Who gets in | What the app does |
|---|---|---|
| **Internet** (default) | Friends anywhere | Lists the server online (`sv_lan 0`) and opens the router port while you host (if auto mode is on) |
| **LAN only** | People on your home network | `sv_lan 1`, never touches the router; friends use your LAN IP or *Find games on my network* on the Join side |
| **Just me** | Only this PC | `sv_lan 1` and no port forwarding, so it stays off the internet. Someone on your own network could still join if they know your address (set a password to stop that). Good for testing maps, mods and bot fill on your own |

The Steam lobby option needs Internet mode, so it only shows there; LAN only and Just me always use direct connect. The invite and the network check follow the mode you pick.

**Play tab layout:** the survivor cards and Who can join on the left; campaign, chapter, difficulty and Bot fill on the right. **More settings** opens the rest: Max players, **Skip intros** (on by default: your game starts without the Valve and Turtle Rock videos, and the server cuts a campaign's opening cutscene short so everyone can move as soon as they load), More items and Harder infected for 5+, After the finale, HUD bars, the dedicated server's name, port and password (plus updating it, copying campaigns to it and checking compatibility before launch), and Post to Discord. The launch buttons and the invite stay at the bottom.

**Before you launch**, one window lists anything worth a look, and only when there is something: skins for the same thing that are on together (**Choose skins**), add-ons that replace the same files (keep one or both for each pair), and the compatibility check's problems (**Open the Mods tab**). Launch goes ahead with your choices.

## What gets installed

| Group | Components |
|---|---|
| Core | Metamod:Source 1.12, SourceMod 1.12, L4DToolZ 2.5.x, Left 4 DHooks, Actions 3.9.2 (last build for SourceMod 1.12), Source Scramble, l4d_CreateSurvivorBot, l4dmultislots, l4d_unreservelobby (dedicated only), Items For 5+ Survivors (built in), Infected For 5+ Survivors (built in), Campaign Rotation (built in, dedicated only) |
| 5+ survivor fixes | Defib fix, AFK fix, AFK dead-bot fix, upgrade-pack fix, Charger collision, Witch target, trigger-flow fix, vocalize-by-model, changelevel fix, transition-info fix, command-buffer fix, map-tank fix, rescue-vehicle fix, ladder crash fix, mixed-character fix, full-slot bot fix, target-replace fix, Keep Verified Players (built in), Unique Survivors (built in) |
| Optional | On by default: Skip the welcome screen (built in: players join without the MOTD panel). Off by default: Survivor Identity Fix (community build), save weapons, auto take-over, 8-survivor HUD (built in), 8-Player HUD add-on (installs into your own game) |

### Built-in plugins

These plugins are written for this tool and ship inside the exe (source in `Plugins/`). They compile on install like the other fixes.

- **Skip The Opening Cutscene** (`l4d2_8p_skipintro`): the first chapter of a campaign normally opens with a camera flight while everyone is frozen. The plugin lets the map start it, then cancels the rest and runs the map's own end-of-intro step (`relay_intro_survivor_cameras`, else `relay_intro_finished`), so play starts at once. Maps without those relays, like most custom campaigns, are left alone. **Skip intros** on the Play tab turns it on and off (`l4d2_8p_skipintro_enable`); Start server installs it on servers set up before it existed.
- **Server Mod List** (`l4d2_8p_mods`): tells joining friends which Workshop mods the host plays with. At Start server the app lists your enabled Workshop add-ons (and ones the app downloaded for you), marks each as **required** (campaigns, game modes, mods that change how the game plays, dialogue and animations) or **cosmetic** (skins, sounds, menus and HUD), and writes the list to `cfg/sourcemod/l4d2_8p_mods.cfg`. The plugin publishes it as server rules (`l4d2_8p_mods_1` to `_8`, Workshop numbers only), which the Join side reads with Check server. Start server installs it on servers set up before it existed.
- **Keep Verified Players** (`l4d2_8p_steamauth`): when the dedicated server runs on the same PC as your game, both sign in to that PC's Steam client as Left 4 Dead 2. About a minute after you join, Steam then reports your ticket as invalid and the server drops you with "No Steam logon" (STEAMAUTH failure code 8). This plugin keeps a player Steam already verified when they joined. It also remembers verified players while the server runs, so rejoining from the same PC (after the game hung or was restarted) doesn't end in that drop either. A player whose ticket was never valid is still dropped, and so are VAC bans. The engine hook is adapted from blueblur's [Block No Steam Logon](https://github.com/blueblur0730/modified-plugins) (GPL-3.0).
- **Unique Survivors** (`l4d2_8p_characters`): bot fill copies the map's four survivors, so 8 survivors come out as three Rochelles and two Coaches. This gives everyone their own character, the L4D1 cast included: Nick, Rochelle, Coach, Ellis, Bill, Zoey, Francis and Louis. Players keep their character across chapters.  On L4D1-cast maps the L4D2 cast is told apart by model (names and voices follow the model through the mixed-character and vocalize fixes). On The Passing, where Bill's group are scripted NPCs, survivors wearing L4D1 models borrow an L4D2 counterpart's character number so the map's scripts still find the NPCs. `l4d2_8p_characters_other_cast 0` limits it to the map's own four. It also keeps the L4D1 cast out of their calm animations (the relaxed idle and walk survivors fall into when nothing is around): Valve's L4D1 models hold several weapons straight up through the body in those, where the L4D2 cast hold them across the chest. They stay in their ready pose instead (`l4d2_8p_characters_l4d1_ready_pose`, on by default; it hooks the game's calm check through `gamedata/l4d2_8p_characters.txt`).
  - **Picking a survivor:** players type `!char` (or `!survivor`) in chat for a menu of the eight. A character another player has is greyed out with their name; one a bot has swaps with the bot. The menu also opens by itself once, when a player first joins the survivors (`l4d2_8p_characters_pick_menu 0` turns that off; `!char` still works). Picks last across chapters.
  - **The host's survivor:** on the Play tab, click a survivor card to play as them (click it again for any free one). It's kept for you, so nobody else can pick it, and you get it when you join. The cards show every survivor: yours is slot 1, the others follow in the order players get them, and characters beyond the player limit show as **Not used** (still clickable). The server tells you apart by the Steam account signed in on this PC, written to `cfg/sourcemod/l4d2_8p_characters.cfg` with the pick when the server starts. You can still change in game with `!char`.
  - Admins can move anyone: `sm_8ppick <player> <character>`. `sm_8pchars` lists who is who.
- **8-survivor HUD** (`l4d2_8p_hud`): the stock HUD shows you plus the first four other survivors by player slot (checked in game: dead survivors keep their panel, and a bot's panel is labelled with its character); anyone after that has no panel. This lists exactly those survivors, with name, health (+temporary health), a health bar and DOWN / LEDGE / B&W / DEAD, in a column on the right just below the weapon slots. It stays visible on the Tab screen. Everyone sees the same list (the game draws one scripted HUD for all players), so when a player sits in slot 6 or later the list also includes slot 5, which that player has no panel for; the others then see slot 5 twice, and that player sees their own row. When there are more survivors than rows (4 in the bars-and-icons look, 8 in plain text), the last row lists the rest by name, with anyone dead, down, pinned or low on health first. By default it's plain text and nobody needs to install anything: each game draws it from the server. Settings are in `cfg/sourcemod/l4d2_8p_hud.cfg`: `l4d2_8p_hud_enable`, `l4d2_8p_hud_show` (0 = only survivors without a stock panel, 1 = everyone), `l4d2_8p_hud_layout` (1 = right-hand column, 0 = rows above the bottom panels), `l4d2_8p_hud_x` / `l4d2_8p_hud_y` (position) and `l4d2_8p_hud_background`. The game only draws scripted HUD text inside a centred 4:3 area, so on wide screens the plain column sits just left of the weapon slots rather than under them. It also only draws it in "challenge mode" (the state a mutation sets), so the plugin turns that on while the list is shown; like a mutation, that can stop some achievements.
- **HUD bars + icons** (Play tab, `l4d2_8p_hud_style 1`): the same list as dark boxes right under the weapon slots. Each shows the survivor's name and health (or what's happening to them: SMOKED, POUNCED, RIDDEN for a Jockey, CHARGED, BILED, LEDGE, DOWN, DEAD), a health bar coloured by state (green, yellow, red; red while pinned, hanging or down) and the game's own icon for everything they carry: molotov, pipe bomb or bile jar; first aid kit, defibrillator or an ammo pack; pills or adrenaline. Long names are shortened to fit beside the icons. Every player needs the small **8-Player HUD add-on** (`left4dead2\addons\l4d2_8p_hud.vpk`): click the link under the setting on the Host side, or "Install the 8-player HUD add-on" at the bottom of the Join side, with the game closed. Players without it see stray grey boxes, so only turn it on when everyone has it. Things the game won't let an add-on change: the text font and its size (it's always the scripted HUD's own font). The add-on also makes the scripted-HUD area cover the whole screen, which other HUD mods that replace `scripts/hudlayout.res` or `hudscriptedmode.res` will clash with. The game has 15 HUD boxes in all, so up to 4 survivors fit and, with 3 survivors listed, about 9 item icons at once; when several survivors carry the same item and its boxes run out, the row ends with "+1" (or +2) for the items it couldn't show. `l4d2_8p_hud_addon_x` / `l4d2_8p_hud_addon_y` move the column (the default lines up with the weapon slots on 16:9 screens).
- **Items For 5+ Survivors** (`l4d2_8p_items`) and **Infected For 5+ Survivors** (`l4d2_8p_infected`): scale the map's items and the director's infected to the number of survivors. See [Scaling for 5+ survivors](#scaling-for-5-survivors-items-and-infected).

**Fixed (October 2026): LAN / "Just me" servers crashed on the second map change.** The server connects to Steam when the first map loads, so a `sv_lan 1` that only arrives with `server.cfg` makes it reconnect at the next map. At the end of the map after that, the game uploads its per-map Steam stats (`L4D2MapData`) through the Steam interface it got before the reconnect, which no longer exists, and the server crashes (Windows then blames `sourcemod.2.l4d2.dll`, which faults while the process exits). The app now puts `+sv_lan` on the server's launch line (and on your game's when you host from it), so Steam starts in the right mode and nothing changes later. If you start srcds yourself, put `+sv_lan 1` before `+map` for a LAN server. Internet servers were never affected (`sv_lan 0` is the default). SourceMod 1.12 git7259 also fixes a separate entity-output crash ([alliedmodders/sourcemod#2589](https://github.com/alliedmodders/sourcemod/issues/2589)); **Setup > Check for updates** installs new builds.

Everything comes from the authors' GitHub repositories, AlliedModders (SourceMod/Metamod) and Valve (SteamCMD). Seven fixes are only published as source code, so the tool compiles them on your PC with SourceMod's own compiler.

Three fixes are only on the AlliedModders forum, which blocks automated downloads. **Forum-only extras...** on the Setup tab links to them.

**Client side:** the host should subscribe to the [8 Slots Lobby Mod](https://steamcommunity.com/sharedfiles/filedetails/?id=2754956355) to get 8-slot lobbies. Friends need nothing except the same custom campaigns.

## Custom campaigns

- **Play tab > Campaign** lists the official campaigns by their in-game names (Dead Center to The Last Stand), then every custom campaign in the server's add-ons and your game's add-ons by its own title (read from each add-on's `missions` file). **Start from chapter** picks the level the server starts on, by the chapter's in-game name ("2 · Streets", "5 · Rooftop Finale"); a custom campaign's chapters use the names in its `missions` file, or the map name where it has none. Picking another campaign starts it from chapter 1. A map saved by an older version that isn't in any campaign shows as **Other map**.
- **Open Workshop** (Mods tab, Skins tab, and the Join side's footer) opens Left 4 Dead 2's Steam Workshop in the Steam client, or in your browser when Steam isn't installed. Subscribe there, and the add-on is checked on the next scan.
- With **Copy campaigns and script mods to the server** on, campaigns you subscribe to in your game reach the dedicated server the next time it starts.
- **Copy invite** adds "Needs the <campaign> campaign: <Workshop link>" when the server runs a custom campaign that came from the Workshop. It stays on the invite's single line, so friends can paste the whole invite into the Join side, which then links straight to that Workshop item.

## After the finale (campaign rotation)

**Play tab > After the finale** decides where a dedicated server goes once a campaign's rescue leaves (the built-in *Campaign Rotation* plugin, a required install):

| Choice | What happens, a few seconds into the credits |
|---|---|
| **Stay** | Nothing: the game's own behaviour (the server sits on the finale) |
| **Next** (default) | The next campaign in order: the 14 official campaigns, then your custom campaigns |
| **Random** | Any other campaign |
| **Vote** | The players vote between the next campaign, a few random ones and "Surprise me". Anyone can start it early by typing **!mapvote**; the result is kept for the finale. Nobody voting means Next |

- The list is rewritten every time the server starts. Custom campaigns only join it when the server has their maps (see [Custom campaigns](#custom-campaigns)); any it can't load are skipped.
- **Moving on after wipes** is off by default. `l4d2_8p_rotation_wipes 3` in `cfg/sourcemod/l4d2_8p_rotation.cfg` moves on after 3 wipes on a finale (wipes on other chapters don't count). `l4d2_8p_rotation_delay` sets the seconds after the rescue (default 15).
- Admins can type `sm_nextcampaign` to move on now (in Vote mode it starts the vote), and `sm_8protation` shows the list and what comes next.
- Co-op and realism only. Listen servers (Host from my game) don't use it.
## Working with mods you already have

- **Existing SourceMod/Metamod/plugins are kept.** The tool records every file it writes. Before replacing a file it didn't create, it saves a backup, and **Remove everything** restores the originals. Your admin lists and plugin configs are never overwritten.
- **Config files are edited, never replaced.** Settings go into a marked block at the end of `server.cfg` (or `listenserver.cfg`) and `cfg/sourcemod/l4dmultislots.cfg`. Put your own lines outside the block.
- **Campaigns and script mods reach the server.** With *Copy my server-side addons* on, your enabled campaign, mutation, script and dialogue addons are hard-linked (or copied) into the dedicated server before each start. Cosmetic mods stay client-side.

## Compatibility checks

The **Mods & compatibility** tab reads your addons and plugins directly and flags what can break 8-player games:

- **Addon conflicts.** Two VPKs that override the same files with different copies, where only one wins. This is escalated when it hits the 8-slot lobby UI.
- **Harmless overlaps.** When both add-ons hold the same copy of a file (same size and CRC32, or the same bytes when a VPK has no CRCs), nothing changes whichever wins. That's typical of parts of one campaign, or the same mod installed twice. The game's Add-ons menu still marks these as conflicting; the app notes them as harmless and doesn't count them. `sound/sound.cache`, which the game rebuilds and many campaigns ship, never counts either.
- **Script risks.** VScripts that change `survivor_limit` or player-limit cvars, or that loop over exactly 4 players. Mutations that cap their lobby below 8.
- **Plugin conflicts.** Rival slot managers (SuperVersus, Bebop, l4d2_rmc, old MultiSlots), plugins the guide marks as broken or harmful (Left4Fix, Character Manager, ...), obsolete extensions (Left4Downtown2, old L4DToolZ), and duplicate copies.
- **Missing dependencies.** Read from each `.smx`: required plugin libraries, extensions, and Left 4 DHooks natives.
- **Stability.** Plugin load failures and runtime errors from the last 7 days of SourceMod error logs, plus recent server crash dumps.
- **Setup.** The lobby mod is subscribed and enabled, and the dedicated server is the same version as your game.

**The rating key.** Every add-on in your game gets a color in the Rating column (and on the skin picker's cards), from its conflicts (other add-ons that are on and replace the same files with different copies) and its requirements (what it changes beyond looks: a campaign, a game mode, gameplay scripts, weapon stats, dialogue, animations):

| Rating | Color | Means |
|---|---|---|
| Safe | blue | no conflicts, no requirements |
| Stable | green | no conflicts, at least one requirement |
| Moderate | yellow | at least one conflict |
| Unstable | orange | more than 2 conflicts, or more than 2 requirements |
| Unsafe | red | can't be used right now (unreadable, or the check found a problem with it), whatever else |

A campaign's own scripts and dialogue count as part of the campaign. An add-on that's off is rated as it would be if you turned it on ("would conflict with ..."). Hover the key for the details, or click an add-on to see why it got its color.

**Disable conflicting** (next to Scan now, shown with the number of conflicting pairs) lists every pair of add-ons that are on and really clash, skins included. Each pair has **Keep** one / **Keep** the other / **Keep both**, with a suggestion already picked: never the HUD add-on or the lobby mod, then the skin you picked, a campaign or game mode, the subscribed Workshop copy over a loose file, and the add-on with more in it. Change any of them, then **Turn off**. Harmless overlaps are listed below for reference and left alone. Turned-off add-ons stay installed.

**Future mods are checked as they arrive.** While the tool is open it watches your `addons` and `plugins` folders. Anything new is scanned right away, marked **NEW**, and announced with a notification. A full check also runs before every launch, and you can continue or cancel if it finds problems.

**Disable / enable highlighted** moves a plugin to `plugins/disabled` or turns an addon off in `addonlist.txt`, and the same button reverses it.

## Skins (the Skins tab, on both sides)

The picker sorts the skin add-ons you have (in `left4dead2\addons` and Workshop subscriptions) by what they change:

- **Survivors:** each of the eight, always shown.
- **Weapons:** each gun and melee weapon, and the chainsaw.
- **Items:** first aid kit, pills, adrenaline, defibrillator, throwables, ammo packs, gas can, propane, oxygen, fireworks, gnome and cola.
- **Infected:** *Special* (Hunter, Smoker, Boomer, Charger, Jockey, Spitter, Tank, Witch) and *Common* (commons, plus each uncommon type).

Everything but the survivors appears only once you have a skin for it.

**Disable all** (top right) turns off every skin that's on, so everything uses the game's own look, and remembers that, so a launch keeps it (a skin installed later is asked about). Campaign and script mods that include skins stay on, and the HUD add-on and lobby mod aren't touched.

**Picking a skin:** click a survivor, weapon, item or infected, then click the skin you want, or **Game default**. That skin is turned on, and every other skin for the same thing is turned off in the game's add-on list.

**Conflicts:** when two skins for one thing are on, the game uses one of them and which one isn't predictable. The card shows "2 skins on · pick one" in amber.

**What counts as a skin:**
- A mod is recognised when it replaces the thing's model, or one of the materials and textures that model uses. Texture-only skins count.
- The material list comes from the game's own models (`Resources/skin_targets.json`, built with `--skin-catalog`).
- Materials that many things share, like eye textures, don't count, so they don't make a mod look like a pack.

**Packs:** an add-on is all or nothing. If the one you click also skins other things, the app asks first and lists:
- everything it will skin;
- the add-ons it turns off;
- anything that goes back to the game's look because a turned-off add-on skinned it.

You can tick **Don't ask again for this add-on**.

**Packs & collections** (the switch above the list) shows the add-ons that skin more than one thing, whatever the mix: several survivors, a set of guns, survivors plus a weapon. Click one to see everything it skins, then **Use this pack** turns it on for all of them, with the other skins for those things off. **Turn it off** turns it off and forgets the picks that named it. Either way, a question lists what gets turned on and off first.

**Workshop collections** live on the same view. Click **Add a collection** and paste a collection's link. The app reads its list of items from Steam's public Web API (no sign-in, nothing downloaded), and shows how many items the game has downloaded and which are on. **Use this collection** turns on every downloaded item (skins, maps, sounds, anything) and turns off skins from outside it for the same things. A target two of its own skins cover is left to the question before launch. **Open on the Workshop** goes to the collection's page, where **Subscribe to all** gets the rest; the game downloads them when it starts. **Update the list** reads it from Steam again. The list of collections is kept in `skins.json`.

**Remembered:** your picks are saved in `skins.json` and put back each time the app starts the game (Launch L4D2 & join, Start server only, Join game). The game's Add-ons menu or a new Workshop subscription can't quietly undo them. The Radio log lists anything that had to be put back. A newly installed skin for something you've picked is asked about: use it, or keep your pick.

**Checked before each launch:** the game turns on every new Workshop subscription, so two skins for the same thing can end up on with nothing picked. Then only one of them shows, and which one isn't predictable, while every extra add-on still loads. Before **Launch L4D2 & join** or **Join game** starts the game, the app lists these and offers **Choose skins** (opens the picker on the first one), **Launch anyway** (keeps them, and doesn't ask again until another skin for the same thing turns up) or **Cancel**.

**Add-ons that replace the same files** are asked about next, skins or not. The game uses one copy of each file, so with both on you get part of each: a survivor pack showing another add-on's HUD portraits, or a HUD mod with half of the 8-player HUD. The question names both add-ons and what they share ("survivor portraits on the HUD (12), character-select pictures (4)"), and for each pair you choose **Keep** one (the other is turned off), or **Keep both** (not asked again while they share the same files). **Launch** waits for a choice on every pair. The HUD add-on and the lobby mod are never the one turned off, and a skin pick for an add-on you turn off here is dropped, so the next launch doesn't turn it back on.

**Never touched:**
- The 8-Player HUD add-on, the 8 Slots Lobby Mod and the other add-ons the app needs.
- Campaigns, mutations and script mods: they stay on, and the picker says so.
- Add-ons that aren't skins (maps, sounds, scripts): left alone.

A skin that also ships weapon scripts is marked: those can change how the weapon handles. The compatibility check's verdict on each add-on (the Mods tab) shows on its card, with its rating (the same key as the Mods tab) as a colored badge and the reason underneath. Each survivor, weapon, item and infected card has a dot in the color of the skin in use.

**Things to know:**
- Skins show only on your own screen.
- Servers that enforce pure files (`sv_pure`, `sv_consistency 1`) can block them. Servers made with this app set `sv_consistency 0`.
- The game reads its add-on list when it starts, so changes need it closed. The picker is read-only while it runs.
- Skins aren't copied to the dedicated server, even one that ships a small script of its own (some witch and survivor packs do). With a skin's textures on the server, joining stalls on a black screen while the game asks the server for files it already has.
- **Open Workshop** takes you to Left 4 Dead 2's Workshop. Subscribe to a skin, let Steam download it, then click **Look again**.
## Bot fill

**Play tab > Bot fill: fill every open slot with a bot.**

- **On:** the round starts with a full team (Max players, 8 by default). Bots hold every empty slot, and players who join take a bot over.
- **Off:** you get the game's normal 4 survivors. Survivors 5+ only appear when players 5+ join.

The game doesn't do this by itself. l4dmultislots does it through `l4d_multislots_min_survivors` and `l4d_multislots_spawn_survivors_roundstart`.

Survivors 5+ always start with the starting pistol, a first aid kit and 100 health (no temporary health), instead of l4dmultislots' default random tier 2 gun, secondary, throwable and 80+20 health. The app sets this in the same block of `l4dmultislots.cfg` (`l4d_multislots_firstweapon 0`, `secondweapon 0`, `thirdweapon 0`, `forthweapon 1`, `fifthweapon 0`, `respawnhp 100`, `respawnbuffhp 0`).

**Gear at chapter changes:** the game carries weapons and items over for the first four survivors only. Survivors 5+ (seen with the bots in those slots) arrive in the next chapter with the starting loadout and re-arm from the saferoom. The optional **l4d2_ty_saveweapons** (Setup tab) keeps their gear, but on L4D2 it also needs *Transition Restore Fix*, a forum-only download (**Forum-only extras**); with saveweapons alone, only some items come back.

## Scaling for 5+ survivors: items and infected

The game is tuned for 4 survivors. Two built-in plugins, both required installs (Core group), scale it to the team you actually have, bots included. Each has its own row on the **Play tab** with four modes:

| Mode | What it does (S = survivors) | 6 survivors | 8 survivors |
|---|---|---|---|
| **Auto** (default) | × S ÷ 4 | 1.5× | 2× |
| **Half** | half of Auto's increase | 1.25× | 1.5× |
| **Custom** | your own % per survivor beyond 4, for each rule (**Custom rules…**) | 2 × your % | 4 × your % |
| **Off** | the game's normal amounts for 4 | 1× | 1× |

With 4 or fewer survivors nothing changes. In Custom, 25% per survivor beyond 4 matches Auto. Changes apply the next time the server starts.

### More items for 5+ (*Items For 5+ Survivors*)

| Survivors (Auto) | Weapon pile of 5 | Each kit, pills or throwable spawn |
|---|---|---|
| 4 or fewer | 5 | 1 |
| 6 | 7 or 8 | 1 or 2 (about half get a second) |
| 8 | 10 | 2 |

- It covers weapon piles, pistols, melee weapons, the chainsaw, molotovs, pipe bombs, bile jars, first aid kits, pills, adrenaline and defibrillators. Ammo piles, upgrade packs and infinite spawns stay as they are.
- Custom rules: weapons, throwables and healing, each with its own %.
- A spawn still shows one item until it runs out. A doubled kit stays on the table after the first person takes it.
- When survivors join mid-chapter, the spawns still on the map are topped up. Nothing is taken away when they leave, and spawns that are already empty don't come back.
- The game removes a random share of item spawns when each map loads. That stays as it is; the plugin scales what's left.
- Bots grab spare items quickly (the saferoom pistols especially), so they take a share of the extras too.
- In `cfg/sourcemod/l4d2_8p_items.cfg`: `l4d2_8p_items_weapons`, `_throwables`, `_healing` (1/0) switch a category off entirely, and `l4d2_8p_items_max` caps the multiplier (default 4). Admins can type `sm_8pitems` in the console to see the counts.

### Harder infected for 5+ (*Infected For 5+ Survivors*)

| On Normal, Auto | 4 survivors | 6 survivors | 8 survivors |
|---|---|---|---|
| Specials alive at once | 2 | 3 | 4 |
| Common infected limit | 30 | 45 | 60 |
| Panic horde (mega mob) | 50 | 75 | 100 |
| Tank health | 4000 | 6000 | 8000 |

- It changes the director's own limits (through Left 4 DHooks), so the game still decides where and when infected come; there are just more of them. The per-class limits rise with the total so it can fill them.
- Custom rules: specials at once, horde size, Tank health, and **faster specials** (how much sooner a dead special's slot refills; 0% by default, and not part of Auto or Half).
- Tank health is set when each Tank spawns, from the game's own value for the difficulty.
- The count is the most survivors seen this round, so a player leaving mid-round doesn't make it easier until the next map.
- Co-op, realism and survival only. In versus and scavenge the infected are players, so nothing changes.
- In `cfg/sourcemod/l4d2_8p_infected.cfg`: `l4d2_8p_infected_max_specials` (default 12) and `l4d2_8p_infected_max_common` (default 100) are hard caps. Admins can type `sm_8pinfected` to see every value it has changed.
## Friends over the internet (port forwarding)

With **Who can join** on **Internet**, the Play tab shows **Can friends reach you?** under the settings:

- **The route**, step by step: this PC, your router (and whether it supports UPnP), your public address, and whether friends can get in. It runs the first time you open Play, and finds the setups where forwarding can't work: a second router or carrier-grade NAT (CGNAT), with fixes such as bridge mode, asking your ISP for a public IP, or a virtual LAN like Tailscale, ZeroTier or Radmin VPN.
- **One button for the next step**: **Open port 27015 on my router** forwards UDP+TCP on the game port to this PC (it warns instead if another device already holds that port); once it's open, the same button becomes **Test from outside**, which queries your server through your public address. Many routers can't loop back to themselves, so if that test fails, have a friend check with the Join side. With UPnP off, it opens your router's settings page.
- **Help & details** has the rest: **Allow in firewall** (an inbound rule for the port and `srcds.exe`; Windows asks), **Check again**, **Close port**, **Router settings**, the **auto mode** box (the port opens when you host and closes when you stop; if the app was closed mid-game, the port is closed next time it starts), step-by-step manual forwarding with your own values filled in, and the raw details.

The invite at the bottom of Play (**Copy invite**) is a `steam://connect/...` link plus the console command, and the campaign's Workshop link when you run a custom one.

## Post to Discord

**Play tab > More settings > Post to Discord** posts your invite to a Discord channel when the server is up:

1. In Discord: the channel's settings › **Integrations** › **Webhooks** › **New Webhook** › **Copy Webhook URL**.
2. Paste it into the app (Host side). It's hidden like a password, because anyone with it can post to that channel.
3. Click **Send a test post** to check.

The post shows the server name, campaign, mode, difficulty and size, the `steam://connect` link and console command, a download link for the app (friends click JOIN), and the Workshop link when you run a custom campaign. **Post when it stops** (off by default) adds a short "has stopped" post. Nothing is posted for **Just me** servers or listen servers, and the post never mentions anyone. The webhook is stored only in `settings.json` and is never written to the log.
## Joining only (for friends): the Join side

The Join side has its own tabs, so friends who only join never need the Host side: **JOIN** (the invite and the server), **MODS**, **SKINS** and **SETTINGS**.

- **Mods** lists every add-on in your game, ticked when it's on. Tick one to turn it on, untick it to turn it off; the change takes effect the next time the game starts, and the list is read-only while the game runs.
  - Each add-on shows its rating (the color key from the Mods tab on the Host side), what kind it is (campaign, game mode, scripts, weapon stats, dialogue, the skins it has) and why it got its color. Hover a row for the details, or click it to see where the file is.
  - **All / Campaigns / Gameplay / Skins / Other** shows one kind at a time.
  - **Disable conflicting** shows up when add-ons that are on clash. **Open Workshop** and **Look again** are there too.
  - A skin turned on here becomes your pick for what it skins (so the next launch turns the other skins for those off); turned off, its picks go back to the game's own look. The 8-Player HUD add-on and the lobby mod can't be turned off here.
- **Skins** is the same skin picker as the Host side's Skins tab.
- **Settings** holds:
  - **Updates:** the version you have and the latest on GitHub, **Check for updates** (asks GitHub now, then installs or offers it the usual way), **What's new**, and the automatic or by-hand choice.
  - **Your game:** the game folder (**Change game folder**, **Open the addons folder**), **Download a server's missing mods when I join**, and installing, updating or removing the 8-Player HUD add-on.
  - **Help and files:** **Show the tour** (the tour's button lives here now), the settings folder, the error log, and **Uninstall…** (Windows' Installed apps).
- The links under the Join tab (game folder, Workshop, HUD add-on, add-on conflicts) show only on that tab.

1. Paste the host's invite. A `steam://connect/...` link, an `IP:port`, or a `connect ...` line all work.
2. **Check server** shows the server name, map, players and ping. It also warns about:
   - a game version mismatch
   - a missing password
   - a full server
   - a custom map you don't have. When the host's invite names the campaign, the link opens that exact Workshop item in Steam, where Subscribe is one click; otherwise it searches the Workshop for the map name
3. **Mods on this server** (shown after Check server, for servers started with this app) lists the host's Workshop mods:
   - **REQUIRED** ones (campaigns, game modes, gameplay changes, animations) are needed to play there. With **Download missing mods when I join** ticked (the default), the ones you're missing download before the game starts. Turned off, Join game asks first.
   - **Cosmetic** ones (the host's skins, sounds, HUD) are your choice: tick the ones you want and they download too. The ones you already have show a check mark.
   - Downloads come straight from Steam and go into your game's `addons` folder as `8p_<number>.vpk`. If you later subscribe to the same item on the Workshop, the app removes its own copy. A running game can't load new add-ons, so if required mods are missing, close the game first.
4. **Join game** starts L4D2, waits until it has loaded your Workshop add-ons (skins, the host's campaign), then connects. If the game is already running, it connects directly.
5. **Go to lobby** (next to Join game) does the same preparation, downloading the server's mods and putting your skin picks back, but leaves the game on its main menu instead of connecting. From there, join your host's lobby: accept their invite, or right-click them in Steam's friends list and choose **Join Game**.

A host with many mods makes the list long: the whole Join page scrolls, so every mod and the Join buttons stay reachable.

**Conflicts on the Join side too:** the app checks your add-ons when it opens, after the Skins window, after the server's mods download, and whenever an add-on is installed or removed while it's open. Add-ons that are on and clash show as a link at the bottom ("2 add-on conflicts · Disable conflicting"), which opens the same Disable conflicting window as the Host side. Harmless overlaps don't count.

**Find games on my network** lists LAN servers, and recent servers are remembered. Joiners don't need SourceMod, the lobby mod or anything else, only the host's required mods, which the app can download for them.

## Friends joining (manual)

- **Direct connect (default):** forward UDP+TCP port 27015 on your router to this PC, and allow `srcds.exe` when Windows Firewall asks. Friends open the console and type `connect <your public IP>:27015`; **Copy invite** includes that line.
- **Steam lobby:** create an 8-slot lobby, choose *Best Available Dedicated* and start. The tool launches your game with `mm_dedicated_force_servers` aimed at your server. When 4 players are in, `l4d_unreservelobby` opens slots 5-8.

## Keeping it current

- **New versions** are found by asking GitHub at most once a day (quietly, and not at all when offline). How they install is chosen in the Setup, under **Updates**:
  - **Install updates automatically** (the first install's default): the new version's Setup downloads in the background ("Downloading version X" in the header), then installs when you close the app. The header says "Version X ready · Restart to update": click it to restart into the new version now.
  - **Let me install updates myself:** the header says "Version X is out · Install". Click it for **Install now**, which downloads it, closes the app, installs it and opens the app again, or **Later**.
  - Either way the update keeps the install folder, the shortcuts you have and the side the app opens on; settings, skins and the server aren't touched. The Setup waits for the app to close before replacing it, and if an update fails, the app says so the next time it starts.
  - The update window (click the notice) has **What's new** and an **Install updates automatically** box to switch between the two. So do the Join side's **Settings** tab and the Host side's Setup tab (**Advanced**: **Check for app updates** and **Install app updates automatically**). Running the Setup again (Windows' Installed apps, Modify) changes it too.
  - **Check for updates** asks GitHub right away instead of waiting for the daily check.
  - A copy of the app that isn't the installed one (run straight from Downloads) only points at the release page.
- **Built-in plugins update themselves.** When a new version of the app brings changed built-in plugins, they are rebuilt on the server the next time it starts (the log lists each one).
- **Updates are looked for by themselves** the first time you open Setup in a session: SourceMod, Metamod, L4DToolZ and Source Scramble against their latest releases, and the dedicated server against your game version. The line above **Install / update** says what's out of date (**Advanced › Check for updates** looks again).
- **Install / update** always pulls the latest plugin builds and gamedata from GitHub. Run it after every L4D2 update, because outdated gamedata is the usual cause of crashes.
- **Server check after every start.** Once the server answers, the app reads SourceMod's error log for that start. Any plugin or extension that failed to load or hit an error is listed in the Radio log by component name, with a warning under the launch buttons (**Details** opens the list and the log folder). Nothing listed means every plugin loaded cleanly. A game update that changes the server's code is the usual cause; the built-in plugins log a plain message when their hook can't be found (Unique Survivors keeps working, only the L4D1 ready pose is lost).
- The component list (`components.json`) is built into the exe. To change sources without rebuilding, put an edited `components.json` next to the exe.

## Where things are

- Tool settings and download cache: `%LOCALAPPDATA%\L4D2-8P-Manager` (the Join side keeps its recent list in `join.json` there, `mode.txt` remembers the side the app opens on, and `skins.json` your skin picks)
- Install records and backups: `<left4dead2>\addons\l4d2_8p_manager`
- Dedicated server (default): `<drive of your game>\L4D2-8P-Server\server`

## Building from source

Requires the .NET SDK (6 or newer) on Windows. The output targets .NET Framework 4.8, which ships with Windows 10 and 11. Both sides are one project: the Host side is `UI\`, the Join side `Join\`, sharing `Core\`, `UI\Theme\` and `Fonts\`; `UI\AppWindow.cs` holds both in one window and switches between them.

```bash
dotnet build L4D2EightPlayer -c Release
```

Headless diagnostics (writes a text report; the paths below are examples):

```bash
L4D2-8P-Coop.exe --selftest --detect --scan-client "C:\...\Left 4 Dead 2\left4dead2" --out report.txt
```

`--mods-test report.txt` checks the server mod list without a server: splitting a long list over the cvars and reading it back, Steam's details for two Workshop items, and downloading them into a throwaway folder (needs the internet).

## Credits

- Keep Verified Players adapts the engine hook from blueblur's [modified-plugins](https://github.com/blueblur0730/modified-plugins) (GPL-3.0). The 8-survivor HUD writes the scripted-HUD network props the way fbef0102's [l4d2_cs_kill_hud](https://github.com/fbef0102/L4D1_2-Plugins) does.


Fonts embedded in both apps, all under the SIL Open Font License 1.1 (license texts in `licenses/`):

- [Oswald](https://github.com/googlefonts/OswaldFont)
- [Barlow](https://github.com/jpt/barlow)
- [Rubik Dirt](https://github.com/googlefonts/rubik-filtered)
