/**
 * [L4D2] Skip The Opening Cutscene (8-Player Co-op Manager)
 *
 * The first chapter of each campaign opens with a cutscene: the camera flies in while the survivors talk, and
 * nobody can move until it's over. The map runs it from logic_relay relay_intro_start and ends it through
 * relay_intro_survivor_cameras, then relay_intro_finished (view back to the players, survivors released, the
 * director told the intro is over). This lets the map start its intro, then cancels the rest of it and runs the
 * map's own ending straight away, so everyone starts playing as soon as they load. Maps without these relays
 * (most custom campaigns) are left alone. l4d2_8p_skipintro_enable 0 shows the cutscenes again.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>

#define PLUGIN_VERSION "1.0.0"

public Plugin myinfo =
{
	name = "[L4D2] Skip The Opening Cutscene",
	author = "L4D2 8-Player Co-op Manager",
	description = "The first chapter's intro cutscene ends as soon as it starts",
	version = PLUGIN_VERSION,
	url = ""
};

ConVar g_cvEnable;

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
	CreateConVar("l4d2_8p_skipintro_version", PLUGIN_VERSION, "Skip The Opening Cutscene version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_skipintro_enable", "1", "1 = the first chapter's opening cutscene is skipped, 0 = it plays.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	AutoExecConfig(true, "l4d2_8p_skipintro");
	HookEntityOutput("logic_relay", "OnTrigger", OnRelay);
}

void OnRelay(const char[] output, int caller, int activator, float delay)
{
	if (!g_cvEnable.BoolValue || !IsValidEntity(caller)) return;
	char name[64];
	GetEntPropString(caller, Prop_Data, "m_iName", name, sizeof(name));
	// a moment for the intro's own setup (survivor positions, the director's intro state) before ending it;
	// a logic_relay isn't networked, so caller is already an entity reference
	if (StrEqual(name, "relay_intro_start", false)) CreateTimer(0.5, SkipIntro, caller, TIMER_FLAG_NO_MAPCHANGE);
}

Action SkipIntro(Handle timer, int startRef)
{
	int start = EntRefToEntIndex(startRef);
	if (start != INVALID_ENT_REFERENCE) AcceptEntityInput(start, "CancelPending");   // the camera flights, dialogue and timers still to come

	int scene = FindNamed("logic_choreographed_scene", "lcs_intro");
	if (scene != -1) AcceptEntityInput(scene, "Cancel");

	// the map's own way out of the intro: cameras off, then relay_intro_finished
	int finish = FindNamed("logic_relay", "relay_intro_survivor_cameras");
	if (finish == -1) finish = FindNamed("logic_relay", "relay_intro_finished");
	if (finish == -1) return Plugin_Stop;
	AcceptEntityInput(finish, "Trigger");

	char map[64];
	GetCurrentMap(map, sizeof(map));
	LogMessage("Skipped the opening cutscene on %s", map);
	return Plugin_Stop;
}

int FindNamed(const char[] classname, const char[] target)
{
	int ent = -1;
	char name[64];
	while ((ent = FindEntityByClassname(ent, classname)) != -1)
	{
		GetEntPropString(ent, Prop_Data, "m_iName", name, sizeof(name));
		if (StrEqual(name, target, false)) return ent;
	}
	return -1;
}
