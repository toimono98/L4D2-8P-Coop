/**
 * [L4D2] Items For 5+ Survivors (8-Player Co-op Manager)
 *
 * Maps are stocked for four survivors. This scales every weapon, throwable and healing item spawn by
 * survivors / 4, bots included: with 8 survivors a weapon pile of 5 holds 10, each saferoom kit can be
 * taken twice, and so on. Fractions are spread at random (6 survivors = 1.5x, so about half the single
 * items get a second pickup). The spawn still shows one model until it runs out.
 *
 * When more survivors join mid-chapter the remaining spawns are topped up. Nothing is taken away when
 * they leave. Spawns that are already empty are gone, and the game's own random removal of item spawns at
 * map start is left as it is. Infinite spawns and ammo piles are not touched.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <sdkhooks>

#define PLUGIN_VERSION "1.0.0"
#define TEAM_SURVIVOR  2
#define SF_INFINITE    8    // weapon spawn flag "Infinite items"

public Plugin myinfo =
{
	name = "[L4D2] Items For 5+ Survivors",
	author = "L4D2 8-Player Co-op Manager",
	description = "Scales weapon, throwable and healing item spawns to the number of survivors",
	version = PLUGIN_VERSION,
	url = ""
};

enum Category
{
	Cat_None = -1,
	Cat_Weapon,
	Cat_Throwable,
	Cat_Healing,
	Cat_Count
}

static const char g_sCatName[][] = { "weapons", "throwables", "healing" };

static const char g_sWeapons[][] =
{
	"weapon_spawn", "weapon_melee_spawn", "weapon_chainsaw_spawn",
	"weapon_pistol_spawn", "weapon_pistol_magnum_spawn",
	"weapon_smg_spawn", "weapon_smg_silenced_spawn", "weapon_smg_mp5_spawn",
	"weapon_pumpshotgun_spawn", "weapon_shotgun_chrome_spawn", "weapon_autoshotgun_spawn", "weapon_shotgun_spas_spawn",
	"weapon_rifle_spawn", "weapon_rifle_desert_spawn", "weapon_rifle_ak47_spawn", "weapon_rifle_sg552_spawn",
	"weapon_hunting_rifle_spawn", "weapon_sniper_military_spawn", "weapon_sniper_awp_spawn", "weapon_sniper_scout_spawn",
	"weapon_grenade_launcher_spawn", "weapon_rifle_m60_spawn"
};
static const char g_sThrowables[][] = { "weapon_molotov_spawn", "weapon_pipe_bomb_spawn", "weapon_vomitjar_spawn" };
static const char g_sHealing[][] = { "weapon_first_aid_kit_spawn", "weapon_pain_pills_spawn", "weapon_adrenaline_spawn", "weapon_defibrillator_spawn" };

ConVar g_cvEnable, g_cvCat[Cat_Count], g_cvMax;

// per spawn entity, by index (the reference catches index reuse)
int g_iRef[2049];
Category g_iCat[2049];
int g_iBase[2049];       // count the map gave it
int g_iGranted[2049];    // total pickups it has been given so far (base + extras)
float g_fRoll[2049];     // fixed per spawn, so fractional extras don't flicker as the count changes
int g_iApplied;          // most survivors seen this round; never goes down

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
	CreateConVar("l4d2_8p_items_version", PLUGIN_VERSION, "Items For 5+ Survivors version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_items_enable", "1", "Scale item spawns to the number of survivors (1 = on, 0 = off).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvCat[Cat_Weapon] = CreateConVar("l4d2_8p_items_weapons", "1", "Scale weapon piles, pistols and melee weapons.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvCat[Cat_Throwable] = CreateConVar("l4d2_8p_items_throwables", "1", "Scale molotovs, pipe bombs and bile jars.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvCat[Cat_Healing] = CreateConVar("l4d2_8p_items_healing", "1", "Scale first aid kits, pills, adrenaline and defibrillators.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvMax = CreateConVar("l4d2_8p_items_max", "4.0", "Largest multiplier (8 survivors = 2.0, 16 = 4.0).", FCVAR_NOTIFY, true, 1.0, true, 8.0);
	AutoExecConfig(true, "l4d2_8p_items");

	g_cvEnable.AddChangeHook(OnSettingChanged);
	for (int c = 0; c < view_as<int>(Cat_Count); c++) g_cvCat[c].AddChangeHook(OnSettingChanged);
	g_cvMax.AddChangeHook(OnSettingChanged);

	HookEvent("round_start", Event_RoundStart, EventHookMode_PostNoCopy);
	RegAdminCmd("sm_8pitems", Cmd_Items, ADMFLAG_GENERIC, "Shows how the item spawns are scaled right now.");
	CreateTimer(1.0, Timer_Check, _, TIMER_REPEAT);

	// loaded mid-map: take what's there now as the base
	int e = -1;
	while ((e = FindEntityByClassname(e, "weapon_*")) != -1)
		Track(e);
}

public void OnMapStart()
{
	g_iApplied = 0;
}

void Event_RoundStart(Event event, const char[] name, bool dontBroadcast)
{
	g_iApplied = 0;
}

void OnSettingChanged(ConVar convar, const char[] oldValue, const char[] newValue)
{
	ApplyAll();
}

Category CategoryOf(const char[] cls)
{
	if (strncmp(cls, "weapon_", 7) != 0) return Cat_None;
	for (int i = 0; i < sizeof(g_sWeapons); i++) if (StrEqual(cls, g_sWeapons[i])) return Cat_Weapon;
	for (int i = 0; i < sizeof(g_sThrowables); i++) if (StrEqual(cls, g_sThrowables[i])) return Cat_Throwable;
	for (int i = 0; i < sizeof(g_sHealing); i++) if (StrEqual(cls, g_sHealing[i])) return Cat_Healing;
	return Cat_None;
}

public void OnEntityCreated(int entity, const char[] classname)
{
	if (entity > 0 && entity <= 2048 && CategoryOf(classname) != Cat_None)
		SDKHook(entity, SDKHook_SpawnPost, OnSpawnPost);
}

void OnSpawnPost(int entity)
{
	// the map's keyvalues are in by now, but give the director a frame to settle what it keeps
	RequestFrame(Frame_Track, EntIndexToEntRef(entity));
}

void Frame_Track(int ref)
{
	int e = EntRefToEntIndex(ref);
	if (e != INVALID_ENT_REFERENCE) Track(e);
}

void Track(int e)
{
	if (e <= MaxClients || e > 2048 || !IsValidEntity(e)) return;
	char cls[48];
	GetEntityClassname(e, cls, sizeof(cls));
	Category cat = CategoryOf(cls);
	if (cat == Cat_None || !HasEntProp(e, Prop_Data, "m_itemCount")) return;
	if (GetEntProp(e, Prop_Data, "m_spawnflags") & SF_INFINITE) return;
	int count = GetEntProp(e, Prop_Data, "m_itemCount");
	if (count <= 0) return;

	g_iRef[e] = EntIndexToEntRef(e);
	g_iCat[e] = cat;
	g_iBase[e] = count;
	g_iGranted[e] = count;
	g_fRoll[e] = GetURandomFloat();
	Apply(e, Multiplier());
}

int Survivors()
{
	int n = 0;
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_SURVIVOR) n++;
	return n;
}

/** survivors / 4 for the most survivors seen this round, between 1 and the cap. */
float Multiplier()
{
	if (!g_cvEnable.BoolValue) return 1.0;
	int n = Survivors();
	if (n < g_iApplied) n = g_iApplied;
	float m = n / 4.0;
	if (m < 1.0) return 1.0;
	return m > g_cvMax.FloatValue ? g_cvMax.FloatValue : m;
}

/** Raises a spawn to its share for multiplier m. Only ever adds pickups. */
void Apply(int e, float m)
{
	if (!g_cvCat[g_iCat[e]].BoolValue) return;
	float want = g_iBase[e] * m;
	int target = RoundToFloor(want);
	if (g_fRoll[e] < want - target) target++;
	if (target <= g_iGranted[e]) return;
	SetEntProp(e, Prop_Data, "m_itemCount", GetEntProp(e, Prop_Data, "m_itemCount") + target - g_iGranted[e]);
	g_iGranted[e] = target;
}

void ApplyAll()
{
	int n = Survivors();
	if (n > g_iApplied) g_iApplied = n;
	float m = Multiplier();
	for (int e = MaxClients + 1; e <= 2048; e++)
		if (g_iRef[e] != 0 && EntRefToEntIndex(g_iRef[e]) == e) Apply(e, m);
}

Action Timer_Check(Handle timer)
{
	if (g_cvEnable.BoolValue && Survivors() > g_iApplied) ApplyAll();
	return Plugin_Continue;
}

Action Cmd_Items(int client, int args)
{
	int spawns[Cat_Count], base[Cat_Count], granted[Cat_Count], left[Cat_Count];
	for (int e = MaxClients + 1; e <= 2048; e++)
	{
		if (g_iRef[e] == 0 || EntRefToEntIndex(g_iRef[e]) != e) continue;
		int c = view_as<int>(g_iCat[e]);
		spawns[c]++;
		base[c] += g_iBase[e];
		granted[c] += g_iGranted[e];
		left[c] += GetEntProp(e, Prop_Data, "m_itemCount");
	}
	ReplyToCommand(client, "[8P items] %s, %d survivors (most this round %d), multiplier %.2f",
		g_cvEnable.BoolValue ? "on" : "off", Survivors(), g_iApplied, Multiplier());
	for (int c = 0; c < view_as<int>(Cat_Count); c++)
		ReplyToCommand(client, "[8P items] %s%s: %d spawns, map gave %d pickups, now %d (%d still on the map)",
			g_sCatName[c], g_cvCat[c].BoolValue ? "" : " (off)", spawns[c], base[c], granted[c], left[c]);
	return Plugin_Handled;
}
