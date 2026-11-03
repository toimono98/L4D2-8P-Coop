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
 * Rejoining (after the game hung or was restarted) makes a new connection, and the invalid-ticket answer
 * can arrive before that player is back in the game. So the plugin also remembers, for as long as the
 * server runs, each Steam ID it saw verified and the address it played from; the same Steam ID coming back
 * from the same address is kept too.
 *
 * Hook and signature adapted from "[L4D2] Block No Steam Logon" by blueblur (GPL-3.0):
 * https://github.com/blueblur0730/modified-plugins
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <dhooks>

#define PLUGIN_VERSION "1.1.0"
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
StringMap g_Verified;   // "steamid|ip" of players Steam verified while this server has been running

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
	g_Verified = new StringMap();
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && IsClientAuthorized(c)) Remember(c);

	GameData gd = new GameData(GAMEDATA);
	if (gd == null) SetFailState("Missing gamedata/%s.txt", GAMEDATA);
	g_iNameOffset = gd.GetOffset("CBaseClient->m_Name");
	DynamicDetour detour = DynamicDetour.FromConf(gd, "CSteam3Server::OnValidateAuthTicketResponseHelper");
	delete gd;
	if (detour == null || g_iNameOffset < 0) SetFailState("This L4D2 build doesn't match the gamedata (signature or offset not found).");
	if (!detour.Enable(Hook_Pre, OnAuthResponse)) SetFailState("Could not hook CSteam3Server::OnValidateAuthTicketResponseHelper.");
}

// In the game with a validated Steam ID.
public void OnClientPostAdminCheck(int client)
{
	Remember(client);
}

void Remember(int client)
{
	char key[96];
	if (!IsFakeClient(client) && Key(client, key, sizeof(key), true)) g_Verified.SetValue(key, true);
}

// "steamid|ip"; validated asks for a Steam ID Steam confirmed, otherwise the one the client gave.
bool Key(int client, char[] key, int size, bool validated)
{
	char id[48], ip[48];
	if (!GetClientAuthId(client, AuthId_Steam2, id, sizeof(id), validated) || StrContains(id, "STEAM_", false) != 0) return false;
	if (!GetClientIP(client, ip, sizeof(ip))) return false;
	FormatEx(key, size, "%s|%s", id, ip);
	return true;
}

// Verified now, or verified earlier and back from the same address under the same Steam ID.
bool Known(int client)
{
	if (IsClientInGame(client) && IsClientAuthorized(client)) return true;
	char key[96];
	bool seen;
	return Key(client, key, sizeof(key), false) && g_Verified.GetValue(key, seen);
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

	// Only a player who already passed Steam validation (now, or earlier from the same address). Names can
	// repeat, so every human with this name must qualify.
	int matches, verified, rejoined;
	for (int c = 1; c <= MaxClients; c++)
	{
		if (!IsClientConnected(c) || IsFakeClient(c)) continue;
		char other[MAX_NAME_LENGTH];
		GetClientName(c, other, sizeof(other));
		if (!StrEqual(name, other)) continue;
		matches++;
		if (!Known(c)) continue;
		verified++;
		if (!IsClientInGame(c)) rejoined++;
	}

	if (matches == 0 || verified != matches)
	{
		LogMessage("Steam auth response %d for \"%s\": not a verified player, leaving the drop to the engine", response, name);
		return MRES_Ignored;
	}
	LogMessage("Steam auth response %d for verified player \"%s\"%s: kept in the game (would have been 'No Steam logon')", response, name,
		rejoined > 0 ? " (rejoining)" : "");
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
