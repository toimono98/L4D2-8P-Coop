/**
 * [L4D2] Keep Verified Players (8-Player Co-op Manager)
 *
 * When the dedicated server runs on the same PC as a player's game, both sign in to that PC's Steam client
 * as Left 4 Dead 2, and about a minute after joining Steam reports the player's ticket as invalid. The
 * engine then drops them with "No Steam logon" (STEAMAUTH failure code 8). The same drop hits players after
 * a reconnect (https://github.com/ValveSoftware/Source-1-Games/issues/7758).
 *
 * This skips that drop only for a player Steam already verified when they joined. A player whose ticket was
 * never valid is still dropped, and so is a VAC ban or a licence problem. Lost connections still time out.
 *
 * Hook and signature adapted from "[L4D2] Block No Steam Logon" by blueblur (GPL-3.0):
 * https://github.com/blueblur0730/modified-plugins
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <dhooks>

#define PLUGIN_VERSION "1.0.0"
#define GAMEDATA       "l4d2_8p_steamauth"

public Plugin myinfo =
{
	name = "[L4D2] Keep Verified Players",
	author = "L4D2 8-Player Co-op Manager (hook by blueblur)",
	description = "Stops 'No Steam logon' kicks for players Steam already verified",
	version = PLUGIN_VERSION,
	url = "https://github.com/blueblur0730/modified-plugins"
};

// EAuthSessionResponse values that end in "No Steam logon"
#define AUTH_USER_NOT_CONNECTED 1
#define AUTH_TICKET_CANCELED    6
#define AUTH_TICKET_USED        7
#define AUTH_TICKET_INVALID     8

ConVar g_cvEnable;
int g_iNameOffset = -1;

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
	CreateConVar("l4d2_8p_steamauth_version", PLUGIN_VERSION, "Keep Verified Players version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_steamauth_enable", "1", "Keep players Steam already verified when it later reports their ticket as invalid (1 = on).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	AutoExecConfig(true, "l4d2_8p_steamauth");

	GameData gd = new GameData(GAMEDATA);
	if (gd == null) SetFailState("Missing gamedata/%s.txt", GAMEDATA);
	g_iNameOffset = gd.GetOffset("CBaseClient->m_Name");
	DynamicDetour detour = DynamicDetour.FromConf(gd, "CSteam3Server::OnValidateAuthTicketResponseHelper");
	delete gd;
	if (detour == null || g_iNameOffset < 0) SetFailState("This L4D2 build doesn't match the gamedata (signature or offset not found).");
	if (!detour.Enable(Hook_Pre, OnAuthResponse)) SetFailState("Could not hook CSteam3Server::OnValidateAuthTicketResponseHelper.");
}

MRESReturn OnAuthResponse(DHookParam params)
{
	Address baseClient = params.Get(1);
	int response = params.Get(2);
	if (baseClient == Address_Null || !g_cvEnable.BoolValue) return MRES_Ignored;
	if (response != AUTH_USER_NOT_CONNECTED && response != AUTH_TICKET_CANCELED && response != AUTH_TICKET_USED && response != AUTH_TICKET_INVALID)
		return MRES_Ignored;

	char name[MAX_NAME_LENGTH];
	ReadString(baseClient + view_as<Address>(g_iNameOffset), name, sizeof(name));

	// Only a player who is in the game and already passed Steam validation. Names can repeat, so every
	// human with this name must qualify.
	int matches, verified;
	for (int c = 1; c <= MaxClients; c++)
	{
		if (!IsClientConnected(c) || IsFakeClient(c)) continue;
		char other[MAX_NAME_LENGTH];
		GetClientName(c, other, sizeof(other));
		if (!StrEqual(name, other)) continue;
		matches++;
		if (IsClientInGame(c) && IsClientAuthorized(c)) verified++;
	}

	if (matches == 0 || verified != matches)
	{
		LogMessage("Steam auth response %d for \"%s\": not a verified player, leaving the drop to the engine", response, name);
		return MRES_Ignored;
	}
	LogMessage("Steam auth response %d for verified player \"%s\": kept in the game (would have been 'No Steam logon')", response, name);
	return MRES_Supercede;
}

void ReadString(Address addr, char[] buffer, int size)
{
	int i;
	for (; i < size - 1; i++)
	{
		buffer[i] = view_as<char>(LoadFromAddress(addr + view_as<Address>(i), NumberType_Int8));
		if (buffer[i] == '\0') return;
	}
	buffer[i] = '\0';
}
