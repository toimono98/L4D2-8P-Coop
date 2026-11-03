/**
 * [L4D2] Server Mod List (8-Player Co-op Manager)
 *
 * Tells the 8-Player Co-op app on a joining player's PC which Workshop add-ons this game uses, so it can download
 * the ones they're missing. The host's app writes the list to cfg/sourcemod/l4d2_8p_mods.cfg (Workshop item
 * numbers: "r" in front for the required ones, "c" for the host's cosmetic ones), and this publishes it as public
 * server rules (A2S_RULES), split over a few cvars because a config line has a length limit. It changes nothing in game.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>

#define PLUGIN_VERSION "1.0.0"
#define PARTS 8

public Plugin myinfo =
{
	name = "[L4D2] Server Mod List",
	author = "L4D2 8-Player Co-op Manager",
	description = "Publishes the Workshop add-ons this game uses, for joining players' downloads",
	version = PLUGIN_VERSION,
	url = ""
};

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
	CreateConVar("l4d2_8p_mods_version", PLUGIN_VERSION, "Server Mod List version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	char name[32];
	for (int i = 1; i <= PARTS; i++)
	{
		FormatEx(name, sizeof(name), "l4d2_8p_mods_%d", i);
		// public (in the server's rules), written by the app: Workshop item numbers, r = required, c = cosmetic
		CreateConVar(name, "", "Workshop add-ons this game uses (part of the list; the app writes it).", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	}
	AutoExecConfig(true, "l4d2_8p_mods");
}
