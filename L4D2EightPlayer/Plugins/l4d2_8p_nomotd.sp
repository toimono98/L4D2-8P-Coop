/**
 * [L4D2] Skip The Welcome Screen (8-Player Co-op Manager)
 *
 * When a player joins, the server opens its message-of-the-day panel ("This third-party server provided by"),
 * and the player has to close it before playing. This stops the server from sending that panel, so players
 * go straight into the game. l4d2_8p_nomotd_enable 0 brings it back.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>

#define PLUGIN_VERSION "1.0.0"

public Plugin myinfo =
{
	name = "[L4D2] Skip The Welcome Screen",
	author = "L4D2 8-Player Co-op Manager",
	description = "Players join without the server's MOTD panel",
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
	CreateConVar("l4d2_8p_nomotd_version", PLUGIN_VERSION, "Skip The Welcome Screen version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_nomotd_enable", "1", "1 = players join without the server's welcome (MOTD) panel, 0 = show it.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	AutoExecConfig(true, "l4d2_8p_nomotd");

	UserMsg vgui = GetUserMessageId("VGUIMenu");
	if (vgui == INVALID_MESSAGE_ID) SetFailState("This game has no VGUIMenu user message.");
	HookUserMessage(vgui, OnVGUIMenu, true);
}

/** VGUIMenu: panel name, show flag, key/values. The welcome screen is the "info" panel. */
Action OnVGUIMenu(UserMsg msg_id, BfRead msg, const int[] players, int playersNum, bool reliable, bool init)
{
	if (!g_cvEnable.BoolValue) return Plugin_Continue;
	char name[32];
	msg.ReadString(name, sizeof(name));
	return StrEqual(name, "info") ? Plugin_Handled : Plugin_Continue;
}
