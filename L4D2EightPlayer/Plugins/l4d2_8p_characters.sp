/**
 * [L4D2] Unique Survivors (8-Player Co-op Manager)
 *
 * Bot fill copies the four map survivors, so 8 survivors come out as three Rochelles and two Coaches. This
 * gives every survivor their own character, the L4D1 cast included: Nick, Rochelle, Coach, Ellis, Bill,
 * Zoey, Francis and Louis.
 *
 * After survivors spawn or swap, one pass keeps each player's remembered character, keeps anyone whose
 * character is already unique, and moves the rest to the first free character (model, character number,
 * and for bots the name).
 *
 * m_survivorCharacter is set-dependent: 0-3 are the map's own cast (L4D2 cast on L4D2 maps, L4D1 cast on
 * L4D1 maps) and 4-7 are always Bill, Zoey, Francis, Louis. So on L4D1-cast maps the L4D2 cast only differs
 * by model (names and voices then follow the model through the mixed-character and vocalize fixes). On maps
 * where the L4D1 cast are scripted bots (The Passing), survivors wearing L4D1 models take the number of an
 * L4D2 counterpart so the map's scripts still find the real NPCs, the same workaround Jackz's identity fix uses.
 *
 * The L4D1 cast's own "calm" animations (the relaxed idle and walk survivors use when nothing is around)
 * hold several L4D2-era weapons straight up and down through the body, where the L4D2 cast hold them across
 * the chest. The server picks the same animation for both; the difference is in Valve's L4D1 models. So for
 * survivors wearing an L4D1-cast model this makes CTerrorPlayer::IsCalm answer "no" (a DHooks detour,
 * gamedata l4d2_8p_characters.txt), and they stay in their ready pose. Clearing m_isCalm afterwards isn't
 * enough: PostThink updates the animation right after asking, and clients follow the server's animation.
 *
 * Picking: !char (or !survivor) opens a menu of the eight; a character another player has is greyed out with
 * their name, and one a bot has swaps with the bot. The menu also opens once by itself when a player first
 * joins the survivors. The host can choose theirs in the Manager (l4d2_8p_characters_host_id/_host_pick): it's
 * kept for them, so nobody else can pick it, and they get it when they join. Picks are remembered by Steam ID
 * like everything else here, so they last across chapters. Admins can move anyone with sm_8ppick.
 *
 * Requires Left 4 DHooks (survivor set of the map).
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <dhooks>
#include <left4dhooks>

#define PLUGIN_VERSION "1.2.0"
#define TEAM_SURVIVOR  2
#define TEAM_HOLDOUT   4   // The Passing's L4D1 NPCs
#define CAST           8

public Plugin myinfo =
{
	name = "[L4D2] Unique Survivors",
	author = "L4D2 8-Player Co-op Manager",
	description = "Gives each of up to 8 survivors their own character, L4D1 cast included",
	version = PLUGIN_VERSION,
	url = ""
};

// Cast entries 0-7: Nick, Rochelle, Coach, Ellis, Bill, Zoey, Francis, Louis
static const char g_sName[CAST][] = { "Nick", "Rochelle", "Coach", "Ellis", "Bill", "Zoey", "Francis", "Louis" };
static const char g_sModel[CAST][] =
{
	"models/survivors/survivor_gambler.mdl",
	"models/survivors/survivor_producer.mdl",
	"models/survivors/survivor_coach.mdl",
	"models/survivors/survivor_mechanic.mdl",
	"models/survivors/survivor_namvet.mdl",
	"models/survivors/survivor_teenangst.mdl",
	"models/survivors/survivor_biker.mdl",
	"models/survivors/survivor_manager.mdl"
};
// Order new survivors get characters in: the map's own cast first.
static const int g_iOrderL4D2[CAST] = { 0, 1, 2, 3, 4, 5, 6, 7 };
static const int g_iOrderL4D1[CAST] = { 4, 5, 7, 6, 0, 1, 2, 3 };
// L4D1-cast character numbers on an L4D1-cast map (Bill 0, Zoey 1, Louis 2, Francis 3), and the paired
// number for the L4D2 cast there (Nick-Bill, Rochelle-Zoey, Coach-Louis, Ellis-Francis).
static const int g_iNumOnL4D1Map[CAST] = { 4, 5, 7, 6, 0, 1, 3, 2 };
// On a map with L4D1 NPCs: L4D1 models borrow an L4D2 counterpart's number.
static const int g_iNumOnHoldoutMap[CAST] = { 0, 1, 2, 3, 0, 1, 3, 2 };

ConVar g_cvEnable, g_cvL4D1Cast, g_cvReadyPose, g_cvMenu, g_cvHostId, g_cvHostPick;
StringMap g_hRemembered;     // SteamID -> cast entry, kept across chapters
StringMap g_hOffered;        // SteamIDs the pick menu has opened for by itself (until they leave)
Handle g_hPassTimer;
float g_fLastPick[MAXPLAYERS + 1];

public APLRes AskPluginLoad2(Handle myself, bool late, char[] error, int err_max)
{
	if (GetEngineVersion() != Engine_Left4Dead2)
	{
		strcopy(error, err_max, "This plugin only supports Left 4 Dead 2.");
		return APLRes_SilentFailure;
	}
	return APLRes_Success;
}

public void OnPluginStart()
{
	CreateConVar("l4d2_8p_characters_version", PLUGIN_VERSION, "Unique Survivors version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_characters_enable", "1", "Give every survivor their own character (1 = on, 0 = off).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvL4D1Cast = CreateConVar("l4d2_8p_characters_other_cast", "1", "Use the other game's cast once the map's four are taken (1 = all 8 characters, 0 = only the map's four).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvReadyPose = CreateConVar("l4d2_8p_characters_l4d1_ready_pose", "1", "Keep the L4D1 cast out of their calm animations, which hold some weapons straight up through the body (1 = on, 0 = off).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvMenu = CreateConVar("l4d2_8p_characters_pick_menu", "1", "Open the survivor menu by itself when a player first joins the survivors (1 = on, 0 = only with !char).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvHostId = CreateConVar("l4d2_8p_characters_host_id", "", "The host's Steam ID (STEAM_1:x:y), written by the Manager.", FCVAR_PROTECTED);
	g_cvHostPick = CreateConVar("l4d2_8p_characters_host_pick", "", "The host's survivor (nick, rochelle, coach, ellis, bill, zoey, francis, louis; empty = any). Kept for the host.", FCVAR_NOTIFY);
	AutoExecConfig(true, "l4d2_8p_characters");

	GameData gd = new GameData("l4d2_8p_characters");
	DynamicDetour calm = gd == null ? null : DynamicDetour.FromConf(gd, "CTerrorPlayer::IsCalm");
	if (calm == null || !calm.Enable(Hook_Post, OnIsCalmPost))
		LogError("Couldn't hook CTerrorPlayer::IsCalm (gamedata l4d2_8p_characters.txt); the L4D1 cast keep their calm animations.");
	delete gd;

	g_hRemembered = new StringMap();
	g_hOffered = new StringMap();
	HookEvent("player_team", Event_Team);
	HookEvent("bot_player_replace", Event_TookOver);
	HookEvent("player_disconnect", Event_Left);
	HookEvent("player_spawn", Event_Changed);
	HookEvent("player_bot_replace", Event_Changed);
	HookEvent("round_start", Event_Round, EventHookMode_PostNoCopy);
	RegAdminCmd("sm_8pchars", Cmd_List, ADMFLAG_ROOT, "Lists each survivor's character.");
	RegAdminCmd("sm_8ppick", Cmd_Pick, ADMFLAG_ROOT, "sm_8ppick <player> <character>: moves a survivor (players or bots) to a character.");
	RegConsoleCmd("sm_char", Cmd_Menu, "Pick your survivor.");
	RegConsoleCmd("sm_survivor", Cmd_Menu, "Pick your survivor.");
	RegConsoleCmd("sm_character", Cmd_Menu, "Pick your survivor.");
}

/** "Not calm" for survivors wearing an L4D1-cast model (see the header). */
MRESReturn OnIsCalmPost(int client, DHookReturn ret)
{
	if (!ret.Value || !g_cvReadyPose.BoolValue || client < 1 || client > MaxClients || !IsClientInGame(client)) return MRES_Ignored;
	char model[64];
	GetClientModel(client, model, sizeof(model));
	if (StrContains(model, "namvet") == -1 && StrContains(model, "teenangst") == -1
		&& StrContains(model, "biker") == -1 && StrContains(model, "manager") == -1) return MRES_Ignored;
	ret.Value = false;
	return MRES_Override;
}

public void OnMapStart()
{
	for (int i = 0; i < CAST; i++) PrecacheModel(g_sModel[i], true);
	g_hPassTimer = null;
}

void Event_Changed(Event event, const char[] name, bool dontBroadcast)
{
	SchedulePass(0.3);
}

void Event_Round(Event event, const char[] name, bool dontBroadcast)
{
	SchedulePass(1.5); // everyone spawns at round start; let the game finish first
}

void SchedulePass(float delay)
{
	if (!g_cvEnable.BoolValue || g_hPassTimer != null) return;
	g_hPassTimer = CreateTimer(delay, Timer_Pass, _, TIMER_FLAG_NO_MAPCHANGE);
}

Action Timer_Pass(Handle timer)
{
	g_hPassTimer = null;
	UniquePass();
	return Plugin_Stop;
}

// ----------------------------------------------------------------------------- the pass

bool IsL4D1Map()
{
	return L4D2_GetSurvivorSetMap() == 1;
}

bool HasHoldoutNPCs()
{
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_HOLDOUT) return true;
	return false;
}

/** The cast entry a survivor currently is, from their model (-1 if it isn't one of the eight). */
int EntryOf(int client)
{
	char model[PLATFORM_MAX_PATH];
	GetEntPropString(client, Prop_Data, "m_ModelName", model, sizeof(model));
	for (int i = 0; i < CAST; i++)
		if (StrEqual(model, g_sModel[i], false)) return i;
	return -1;
}

int NumberFor(int entry, bool l4d1Map, bool holdout)
{
	if (l4d1Map) return g_iNumOnL4D1Map[entry];
	if (holdout) return g_iNumOnHoldoutMap[entry];
	return entry;
}

/** A player's Steam ID, in one form ("STEAM_0:" and "STEAM_1:" are the same account). */
bool AuthOf(int client, char[] auth, int size)
{
	if (IsFakeClient(client) || !GetClientAuthId(client, AuthId_Steam2, auth, size, false)) return false;
	if (strncmp(auth, "STEAM_0:", 8) == 0) auth[6] = '1';
	return true;
}

void HostAuth(char[] auth, int size)
{
	g_cvHostId.GetString(auth, size);
	TrimString(auth);
	if (strncmp(auth, "STEAM_0:", 8) == 0) auth[6] = '1';
}

bool IsHost(int client)
{
	char auth[32], host[32];
	HostAuth(host, sizeof(host));
	return host[0] != '\0' && AuthOf(client, auth, sizeof(auth)) && StrEqual(auth, host, false);
}

/** A cast entry from a name ("Nick", "zoey"), or -1. */
int EntryNamed(const char[] name)
{
	for (int i = 0; i < CAST; i++)
		if (StrEqual(name, g_sName[i], false)) return i;
	return -1;
}

/** The host's pick from the Manager (-1 = any). */
int HostPick()
{
	char pick[16];
	g_cvHostPick.GetString(pick, sizeof(pick));
	TrimString(pick);
	return EntryNamed(pick);
}

/** The character kept for the host, which nobody else may pick: the one they last had, else their Manager pick (-1 = none). */
int ReservedForHost()
{
	char host[32];
	HostAuth(host, sizeof(host));
	if (host[0] == '\0') return -1;
	int entry;
	if (g_hRemembered.GetValue(host, entry) && entry >= 0 && entry < CAST) return entry;
	return HostPick();
}

bool Remembered(int client, int &entry)
{
	char auth[32];
	if (!AuthOf(client, auth, sizeof(auth))) return false;
	if (g_hRemembered.GetValue(auth, entry) && entry >= 0 && entry < CAST) return true;
	// the host's first appearance: their pick from the Manager
	entry = IsHost(client) ? HostPick() : -1;
	return entry >= 0;
}

void Remember(int client, int entry)
{
	char auth[32];
	if (AuthOf(client, auth, sizeof(auth))) g_hRemembered.SetValue(auth, entry);
}

void UniquePass()
{
	if (!g_cvEnable.BoolValue) return;
	bool l4d1Map = IsL4D1Map(), holdout = !l4d1Map && HasHoldoutNPCs();
	int order[CAST];
	for (int i = 0; i < CAST; i++) order[i] = l4d1Map ? g_iOrderL4D1[i] : g_iOrderL4D2[i];
	int choices = g_cvL4D1Cast.BoolValue ? CAST : 4;

	int list[MAXPLAYERS + 1], count;
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_SURVIVOR) list[count++] = c;

	bool used[CAST], placed[MAXPLAYERS + 1];
	int target[MAXPLAYERS + 1];

	// 1. players keep the character they had (across chapters too)
	for (int i = 0; i < count; i++)
	{
		int c = list[i], entry;
		if (Remembered(c, entry) && !used[entry]) { used[entry] = true; placed[c] = true; target[c] = entry; }
	}
	// 2. anyone whose current character is still free keeps it
	for (int i = 0; i < count; i++)
	{
		int c = list[i];
		if (placed[c]) continue;
		int entry = EntryOf(c);
		if (entry >= 0 && !used[entry]) { used[entry] = true; placed[c] = true; target[c] = entry; }
	}
	// 3. the rest take the first free character; players other than the host leave the host's alone if they can
	int reserved = ReservedForHost();
	for (int pass = 0; pass < 2; pass++)
		for (int i = 0; i < count; i++)
		{
			int c = list[i];
			if (placed[c]) continue;
			for (int k = 0; k < choices; k++)
			{
				int entry = order[k];
				if (used[entry]) continue;
				if (pass == 0 && entry == reserved && !IsFakeClient(c) && !IsHost(c)) continue;
				used[entry] = true; placed[c] = true; target[c] = entry;
				break;
			}
		}

	for (int i = 0; i < count; i++)
	{
		int c = list[i];
		if (!placed[c]) continue; // more survivors than characters: leave as the game made them
		Apply(c, target[c], NumberFor(target[c], l4d1Map, holdout));
		Remember(c, target[c]);
	}
}

void Apply(int client, int entry, int number)
{
	bool changed = false;
	if (EntryOf(client) != entry) { SetEntityModel(client, g_sModel[entry]); changed = true; }
	if (GetEntProp(client, Prop_Send, "m_survivorCharacter") != number) { SetEntProp(client, Prop_Send, "m_survivorCharacter", number); changed = true; }
	if (IsFakeClient(client))
	{
		char name[MAX_NAME_LENGTH];
		GetClientName(client, name, sizeof(name));
		if (!StrEqual(name, g_sName[entry])) { SetClientName(client, g_sName[entry]); changed = true; }
	}
	if (changed) LogMessage("%N is now %s (character %d)", client, g_sName[entry], number);
}

Action Cmd_List(int client, int args)
{
	bool l4d1Map = IsL4D1Map();
	int reserved = ReservedForHost();
	ReplyToCommand(client, "[8P] survivor set L4D%d, holdout NPCs: %s, kept for the host: %s", l4d1Map ? 1 : 2, HasHoldoutNPCs() ? "yes" : "no", reserved >= 0 ? g_sName[reserved] : "none");
	for (int c = 1; c <= MaxClients; c++)
	{
		if (!IsClientInGame(c) || GetClientTeam(c) != TEAM_SURVIVOR) continue;
		int entry = EntryOf(c);
		ReplyToCommand(client, "[8P] %N: %s, character %d%s", c, entry >= 0 ? g_sName[entry] : "?", GetEntProp(c, Prop_Send, "m_survivorCharacter"), IsFakeClient(c) ? " (bot)" : "");
	}
	return Plugin_Handled;
}

// ----------------------------------------------------------------------------- picking

/** Who has a cast entry now: a survivor's client, or 0. */
int HolderOf(int entry)
{
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_SURVIVOR && EntryOf(c) == entry) return c;
	return 0;
}

/**
 * Whether a survivor may take this character: not the one they have, not another player's (a bot's swaps),
 * not the one kept for the host unless they're the host (admins skip that check), and in the cast in use.
 * why gets a short reason when not.
 */
bool CanTake(int client, int entry, bool admin, char[] why, int size)
{
	why[0] = '\0';
	if (!g_cvL4D1Cast.BoolValue)
	{
		// only the map's four
		bool l4d1Map = IsL4D1Map();
		bool inSet = false;
		for (int k = 0; k < 4; k++) if ((l4d1Map ? g_iOrderL4D1[k] : g_iOrderL4D2[k]) == entry) inSet = true;
		if (!inSet) { strcopy(why, size, "not on this map"); return false; }
	}
	int holder = HolderOf(entry);
	if (holder == client) { strcopy(why, size, "you"); return false; }
	if (holder > 0 && !IsFakeClient(holder)) { FormatEx(why, size, "%N", holder); return false; }
	if (!admin && entry == ReservedForHost() && !IsHost(client)) { strcopy(why, size, "kept for the host"); return false; }
	return true;
}

/** Moves a survivor to a character now (a bot that had it gets another), and remembers it. */
void TakeCharacter(int client, int entry)
{
	Remember(client, entry);
	if (IsFakeClient(client))
	{
		// a bot has no memory: give it the character directly, then let the pass sort out the rest
		bool l4d1Map = IsL4D1Map();
		int holder = HolderOf(entry);
		if (holder > 0 && holder != client)
		{
			int old = EntryOf(client);
			if (old >= 0) Apply(holder, old, NumberFor(old, l4d1Map, !l4d1Map && HasHoldoutNPCs()));
		}
		Apply(client, entry, NumberFor(entry, l4d1Map, !l4d1Map && HasHoldoutNPCs()));
	}
	UniquePass();
}

Action Cmd_Menu(int client, int args)
{
	if (client == 0) { ReplyToCommand(client, "[8P] Survivors pick in game with !char."); return Plugin_Handled; }
	ShowPickMenu(client, 0);
	return Plugin_Handled;
}

void ShowPickMenu(int client, int timeout)
{
	if (!g_cvEnable.BoolValue) { PrintToChat(client, "[8P] Picking survivors is turned off on this server."); return; }
	if (GetClientTeam(client) != TEAM_SURVIVOR) { PrintToChat(client, "[8P] Join the survivors first, then type !char."); return; }
	Menu menu = new Menu(PickMenu);
	int now = EntryOf(client);
	menu.SetTitle("Pick your survivor%s%s", now >= 0 ? "\nYou're " : "", now >= 0 ? g_sName[now] : "");
	for (int i = 0; i < CAST; i++)
	{
		char why[MAX_NAME_LENGTH], label[64], info[4];
		bool ok = CanTake(client, i, false, why, sizeof(why));
		if (!ok && StrEqual(why, "not on this map")) continue;
		if (ok) strcopy(label, sizeof(label), g_sName[i]);
		else FormatEx(label, sizeof(label), "%s (%s)", g_sName[i], why);
		IntToString(i, info, sizeof(info));
		menu.AddItem(info, label, ok ? ITEMDRAW_DEFAULT : ITEMDRAW_DISABLED);
	}
	menu.ExitButton = true;
	menu.Display(client, timeout > 0 ? timeout : MENU_TIME_FOREVER);
}

int PickMenu(Menu menu, MenuAction action, int client, int item)
{
	if (action == MenuAction_End) { delete menu; return 0; }
	if (action != MenuAction_Select) return 0;
	char info[4], why[MAX_NAME_LENGTH];
	menu.GetItem(item, info, sizeof(info));
	int entry = StringToInt(info);
	if (!IsClientInGame(client) || GetClientTeam(client) != TEAM_SURVIVOR) return 0;
	if (GetGameTime() - g_fLastPick[client] < 2.0) { PrintToChat(client, "[8P] One moment, then try again."); return 0; }
	if (!CanTake(client, entry, false, why, sizeof(why)))
	{
		// someone got there first while the menu was open
		PrintToChat(client, "[8P] %s isn't free any more (%s). Pick another:", g_sName[entry], why);
		ShowPickMenu(client, 0);
		return 0;
	}
	g_fLastPick[client] = GetGameTime();
	if (!IsPlayerAlive(client))
	{
		Remember(client, entry);
		PrintToChat(client, "[8P] You'll be %s when you're back in.", g_sName[entry]);
		return 0;
	}
	TakeCharacter(client, entry);
	PrintToChat(client, "[8P] You're %s now. Type !char to change again.", g_sName[entry]);
	return 0;
}

Action Cmd_Pick(int client, int args)
{
	if (args < 2) { ReplyToCommand(client, "[8P] Usage: sm_8ppick <player> <character>"); return Plugin_Handled; }
	char who[MAX_NAME_LENGTH], name[16], why[MAX_NAME_LENGTH];
	GetCmdArg(1, who, sizeof(who));
	GetCmdArg(2, name, sizeof(name));
	int entry = EntryNamed(name);
	if (entry < 0) { ReplyToCommand(client, "[8P] No survivor called %s.", name); return Plugin_Handled; }
	int target = FindTarget(client, who, false, false);
	if (target <= 0) return Plugin_Handled;
	if (GetClientTeam(target) != TEAM_SURVIVOR) { ReplyToCommand(client, "[8P] %N isn't a survivor.", target); return Plugin_Handled; }
	if (!CanTake(target, entry, true, why, sizeof(why))) { ReplyToCommand(client, "[8P] %N can't be %s (%s).", target, g_sName[entry], why); return Plugin_Handled; }
	TakeCharacter(target, entry);
	ReplyToCommand(client, "[8P] %N is now %s.", target, g_sName[entry]);
	return Plugin_Handled;
}

// ----------------------------------------------------------------------------- the menu on joining

void Event_Team(Event event, const char[] name, bool dontBroadcast)
{
	SchedulePass(0.3);
	if (event.GetInt("team") == TEAM_SURVIVOR) OfferSoon(event.GetInt("userid"));
}

void Event_TookOver(Event event, const char[] name, bool dontBroadcast)
{
	SchedulePass(0.3);
	OfferSoon(event.GetInt("player"));
}

void Event_Left(Event event, const char[] name, bool dontBroadcast)
{
	char auth[32];
	event.GetString("networkid", auth, sizeof(auth));
	if (strncmp(auth, "STEAM_0:", 8) == 0) auth[6] = '1';
	g_hOffered.Remove(auth);
}

void OfferSoon(int userid)
{
	if (g_cvEnable.BoolValue && g_cvMenu.BoolValue) CreateTimer(2.5, Timer_Offer, userid, TIMER_FLAG_NO_MAPCHANGE);
}

/** Opens the menu once per visit, after the pass has given the player a character. */
Action Timer_Offer(Handle timer, int userid)
{
	int client = GetClientOfUserId(userid);
	char auth[32];
	if (client == 0 || !IsClientInGame(client) || GetClientTeam(client) != TEAM_SURVIVOR || !AuthOf(client, auth, sizeof(auth))) return Plugin_Stop;
	bool offered;
	if (g_hOffered.GetValue(auth, offered)) return Plugin_Stop;
	g_hOffered.SetValue(auth, true);
	PrintToChat(client, "[8P] Pick your survivor from the menu, or keep this one. Type !char any time to change.");
	ShowPickMenu(client, 30);
	return Plugin_Stop;
}
