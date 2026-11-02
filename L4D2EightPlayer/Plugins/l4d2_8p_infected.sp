/**
 * [L4D2] Infected For 5+ Survivors (8-Player Co-op Manager)
 *
 * The director is tuned for four survivors, so eight mow through it. This scales what it sends by the number
 * of survivors, bots included:
 *   specials    how many can be alive at once (MaxSpecials, DominatorLimit, and the per-class limits so
 *               there are enough classes to fill them)
 *   horde       common infected limit, mob and mega-mob sizes
 *   Tank        health, set when the Tank spawns
 *   respawn     (Custom only) how soon a dead special's slot refills
 *
 * Modes (l4d2_8p_infected_mode):
 *   0 Off      the game's own director
 *   1 Auto     x survivors / 4 (8 survivors: twice the specials, horde and Tank health)
 *   2 Half     half that (8 survivors: 1.5x)
 *   3 Custom   the l4d2_8p_infected_custom_* rules: % more per survivor beyond four
 *
 * The count is the most survivors seen this round; it doesn't drop mid-round when someone leaves. Co-op,
 * realism and survival only: in versus and scavenge the infected are players.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <left4dhooks>

#define PLUGIN_VERSION "1.0.0"
#define TEAM_SURVIVOR  2
#define ZC_TANK        8

public Plugin myinfo =
{
	name = "[L4D2] Infected For 5+ Survivors",
	author = "L4D2 8-Player Co-op Manager",
	description = "Scales special infected, horde size and Tank health to the number of survivors",
	version = PLUGIN_VERSION,
	url = ""
};

enum
{
	Mode_Off,
	Mode_Auto,
	Mode_Half,
	Mode_Custom
}

static const char g_sModeName[][] = { "off", "auto", "half", "custom" };
static const char g_sClassLimits[][] = { "SmokerLimit", "BoomerLimit", "HunterLimit", "SpitterLimit", "JockeyLimit", "ChargerLimit" };
static const char g_sHordeInt[][] = { "CommonLimit", "MegaMobSize" };
static const char g_sHordeFloat[][] = { "MobMinSize", "MobMaxSize", "MobSpawnSize" };

ConVar g_cvMode, g_cvSpecials, g_cvHorde, g_cvTank, g_cvRespawn, g_cvMaxSpecials, g_cvMaxCommon;
int g_iMost;              // most survivors seen this round
bool g_bScaledMode;       // co-op or survival
int g_iBaseMaxSpecials = 2;
StringMap g_hSeen;        // key -> "base -> scaled", for sm_8pinfected

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
	CreateConVar("l4d2_8p_infected_version", PLUGIN_VERSION, "Infected For 5+ Survivors version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvMode = CreateConVar("l4d2_8p_infected_mode", "1", "0 = off, 1 = auto (x survivors / 4), 2 = half strength, 3 = custom rules.", FCVAR_NOTIFY, true, 0.0, true, 3.0);
	g_cvSpecials = CreateConVar("l4d2_8p_infected_custom_specials", "25", "Custom: % more specials allowed at once per survivor beyond 4.", FCVAR_NOTIFY, true, 0.0, true, 200.0);
	g_cvHorde = CreateConVar("l4d2_8p_infected_custom_horde", "25", "Custom: % more common infected per survivor beyond 4.", FCVAR_NOTIFY, true, 0.0, true, 200.0);
	g_cvTank = CreateConVar("l4d2_8p_infected_custom_tank", "25", "Custom: % more Tank health per survivor beyond 4.", FCVAR_NOTIFY, true, 0.0, true, 200.0);
	g_cvRespawn = CreateConVar("l4d2_8p_infected_custom_respawn", "0", "Custom: % faster special respawns per survivor beyond 4.", FCVAR_NOTIFY, true, 0.0, true, 100.0);
	g_cvMaxSpecials = CreateConVar("l4d2_8p_infected_max_specials", "12", "Never allow more specials at once than this.", FCVAR_NOTIFY, true, 1.0, true, 24.0);
	g_cvMaxCommon = CreateConVar("l4d2_8p_infected_max_common", "100", "Never raise the common infected limit or a mob above this.", FCVAR_NOTIFY, true, 30.0, true, 200.0);
	AutoExecConfig(true, "l4d2_8p_infected");

	g_hSeen = new StringMap();
	HookEvent("round_start", Event_RoundStart, EventHookMode_PostNoCopy);
	HookEvent("tank_spawn", Event_TankSpawn);
	RegAdminCmd("sm_8pinfected", Cmd_Status, ADMFLAG_GENERIC, "Shows how the infected are scaled right now.");
	// the director asks for its values many times a second, so the count is kept up to date here instead
	CreateTimer(1.0, Timer_Count, _, TIMER_REPEAT);
	Count();
}

public void OnMapStart()
{
	g_iMost = 0;
	g_hSeen.Clear();
}

void Event_RoundStart(Event event, const char[] name, bool dontBroadcast)
{
	g_iMost = 0;
	Count();
}

Action Timer_Count(Handle timer)
{
	Count();
	return Plugin_Continue;
}

void Count()
{
	int mode = L4D_GetGameModeType();
	g_bScaledMode = mode == GAMEMODE_COOP || mode == GAMEMODE_SURVIVAL;
	int n = 0;
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_SURVIVOR) n++;
	if (n > g_iMost) g_iMost = n;
}

/** Survivors beyond four, or 0 when nothing should change. */
int Extra()
{
	if (g_cvMode.IntValue == Mode_Off || !g_bScaledMode) return 0;
	int e = g_iMost - 4;
	return e > 0 ? e : 0;
}

/** Multiplier for horde (or Tank health when tank is true). */
float Mult(int extra, bool tank)
{
	switch (g_cvMode.IntValue)
	{
		case Mode_Auto: return 1.0 + extra / 4.0;
		case Mode_Half: return 1.0 + extra / 8.0;
		case Mode_Custom: return 1.0 + extra * (tank ? g_cvTank.FloatValue : g_cvHorde.FloatValue) / 100.0;
	}
	return 1.0;
}

int ScaledSpecials(int base, int extra)
{
	float n = g_cvMode.IntValue == Mode_Custom ? base * (1.0 + extra * g_cvSpecials.FloatValue / 100.0) : base * Mult(extra, false);
	int r = RoundToNearest(n);
	return r > g_cvMaxSpecials.IntValue ? (base > g_cvMaxSpecials.IntValue ? base : g_cvMaxSpecials.IntValue) : r;
}

int CapCommon(int base, float n)
{
	int r = RoundToNearest(n);
	int cap = g_cvMaxCommon.IntValue;
	return r > cap ? (base > cap ? base : cap) : r;
}

void Note(const char[] key, float base, float scaled)
{
	char s[48];
	if (base == float(RoundToFloor(base)) && scaled == float(RoundToFloor(scaled)))
		FormatEx(s, sizeof(s), "%d -> %d", RoundToFloor(base), RoundToFloor(scaled));
	else
		FormatEx(s, sizeof(s), "%.1f -> %.1f", base, scaled);
	g_hSeen.SetString(key, s);
}

bool InList(const char[] key, const char[][] list, int count)
{
	for (int i = 0; i < count; i++) if (StrEqual(key, list[i])) return true;
	return false;
}

public Action L4D_OnGetScriptValueInt(const char[] key, int &retVal)
{
	int extra = Extra();
	if (extra == 0) return Plugin_Continue;
	int base = retVal;

	if (StrEqual(key, "MaxSpecials"))
	{
		g_iBaseMaxSpecials = base;
		retVal = ScaledSpecials(base, extra);
	}
	else if (StrEqual(key, "DominatorLimit"))
		retVal = ScaledSpecials(base, extra);
	else if (InList(key, g_sClassLimits, sizeof(g_sClassLimits)))
	{
		// enough room per class for the larger total: spread over four classes, like the game's own mix
		int need = RoundToCeil(ScaledSpecials(g_iBaseMaxSpecials, extra) / 4.0);
		if (need > base) retVal = need;
	}
	else if (InList(key, g_sHordeInt, sizeof(g_sHordeInt)))
		retVal = CapCommon(base, base * Mult(extra, false));
	else
		return Plugin_Continue;

	if (retVal == base) return Plugin_Continue;
	Note(key, float(base), float(retVal));
	return Plugin_Handled;
}

public Action L4D_OnGetScriptValueFloat(const char[] key, float &retVal)
{
	int extra = Extra();
	if (extra == 0 || retVal <= 0.0) return Plugin_Continue;
	float base = retVal;

	if (InList(key, g_sHordeFloat, sizeof(g_sHordeFloat)))
		retVal = float(CapCommon(RoundToNearest(base), base * Mult(extra, false)));
	else if (StrEqual(key, "SpecialRespawnInterval") && g_cvMode.IntValue == Mode_Custom && g_cvRespawn.FloatValue > 0.0)
		retVal = base / (1.0 + extra * g_cvRespawn.FloatValue / 100.0);
	else
		return Plugin_Continue;

	if (retVal == base) return Plugin_Continue;
	Note(key, base, retVal);
	return Plugin_Handled;
}

void Event_TankSpawn(Event event, const char[] name, bool dontBroadcast)
{
	int tank = event.GetInt("userid");
	// the game sets the Tank's health after spawning it
	RequestFrame(Frame_TankHealth, tank);
}

void Frame_TankHealth(int userid)
{
	int tank = GetClientOfUserId(userid);
	int extra = Extra();
	if (!tank || !IsClientInGame(tank) || !IsPlayerAlive(tank) || GetEntProp(tank, Prop_Send, "m_zombieClass") != ZC_TANK || extra == 0) return;
	int base = GetEntProp(tank, Prop_Data, "m_iMaxHealth");
	int hp = RoundToNearest(base * Mult(extra, true));
	if (hp <= base) return;
	if (hp > 65535) hp = 65535;   // the networked health is 16 bits
	SetEntProp(tank, Prop_Data, "m_iMaxHealth", hp);
	SetEntProp(tank, Prop_Data, "m_iHealth", hp);
	Note("Tank health", float(base), float(hp));
	LogMessage("Tank health %d -> %d for %d survivors (%s)", base, hp, extra + 4, g_sModeName[g_cvMode.IntValue]);
}

Action Cmd_Status(int client, int args)
{
	Count();
	int extra = Extra();
	ReplyToCommand(client, "[8P infected] mode %s, %d survivors (most this round), specials %d -> %d, horde x%.2f, Tank x%.2f",
		g_sModeName[g_cvMode.IntValue], g_iMost, g_iBaseMaxSpecials, extra ? ScaledSpecials(g_iBaseMaxSpecials, extra) : g_iBaseMaxSpecials,
		Mult(extra, false) + 0.0005, Mult(extra, true) + 0.0005);   // %.2f truncates
	StringMapSnapshot snap = g_hSeen.Snapshot();
	for (int i = 0; i < snap.Length; i++)
	{
		char key[48], val[48];
		snap.GetKey(i, key, sizeof(key));
		g_hSeen.GetString(key, val, sizeof(val));
		ReplyToCommand(client, "[8P infected]   %s: %s", key, val);
	}
	delete snap;
	return Plugin_Handled;
}
