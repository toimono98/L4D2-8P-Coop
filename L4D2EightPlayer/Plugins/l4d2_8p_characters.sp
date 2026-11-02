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
 * Requires Left 4 DHooks (survivor set of the map).
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <dhooks>
#include <left4dhooks>

#define PLUGIN_VERSION "1.1.0"
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

ConVar g_cvEnable, g_cvL4D1Cast, g_cvReadyPose;
StringMap g_hRemembered;     // SteamID -> cast entry, kept across chapters
Handle g_hPassTimer;

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
	AutoExecConfig(true, "l4d2_8p_characters");

	GameData gd = new GameData("l4d2_8p_characters");
	DynamicDetour calm = gd == null ? null : DynamicDetour.FromConf(gd, "CTerrorPlayer::IsCalm");
	if (calm == null || !calm.Enable(Hook_Post, OnIsCalmPost))
		LogError("Couldn't hook CTerrorPlayer::IsCalm (gamedata l4d2_8p_characters.txt); the L4D1 cast keep their calm animations.");
	delete gd;

	g_hRemembered = new StringMap();
	HookEvent("player_spawn", Event_Changed);
	HookEvent("player_team", Event_Changed);
	HookEvent("bot_player_replace", Event_Changed);
	HookEvent("player_bot_replace", Event_Changed);
	HookEvent("round_start", Event_Round, EventHookMode_PostNoCopy);
	RegAdminCmd("sm_8pchars", Cmd_List, ADMFLAG_ROOT, "Lists each survivor's character.");
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

bool Remembered(int client, int &entry)
{
	if (IsFakeClient(client)) return false;
	char auth[32];
	if (!GetClientAuthId(client, AuthId_Steam2, auth, sizeof(auth), false)) return false;
	return g_hRemembered.GetValue(auth, entry) && entry >= 0 && entry < CAST;
}

void Remember(int client, int entry)
{
	if (IsFakeClient(client)) return;
	char auth[32];
	if (GetClientAuthId(client, AuthId_Steam2, auth, sizeof(auth), false)) g_hRemembered.SetValue(auth, entry);
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
	// 3. the rest take the first free character
	for (int i = 0; i < count; i++)
	{
		int c = list[i];
		if (placed[c]) continue;
		for (int k = 0; k < choices; k++)
		{
			int entry = order[k];
			if (used[entry]) continue;
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
	ReplyToCommand(client, "[8P] survivor set L4D%d, holdout NPCs: %s", l4d1Map ? 1 : 2, HasHoldoutNPCs() ? "yes" : "no");
	for (int c = 1; c <= MaxClients; c++)
	{
		if (!IsClientInGame(c) || GetClientTeam(c) != TEAM_SURVIVOR) continue;
		int entry = EntryOf(c);
		ReplyToCommand(client, "[8P] %N: %s, character %d%s", c, entry >= 0 ? g_sName[entry] : "?", GetEntProp(c, Prop_Send, "m_survivorCharacter"), IsFakeClient(c) ? " (bot)" : "");
	}
	return Plugin_Handled;
}
