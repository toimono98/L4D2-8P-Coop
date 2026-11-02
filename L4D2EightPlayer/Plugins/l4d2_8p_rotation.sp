/**
 * [L4D2] Campaign Rotation (8-Player Co-op Manager)
 *
 * A dedicated server sits on the finale after the rescue. This moves on to another campaign once the credits
 * have run (finale_win), and optionally after a number of wipes on a finale.
 *
 * Modes (l4d2_8p_rotation_mode):
 *   0 Stay     do nothing (the game's own behaviour)
 *   1 Next     the next campaign in the list
 *   2 Random   any other campaign
 *   3 Vote     the players vote between a few campaigns (also any time with !mapvote)
 *
 * The list is data/l4d2_8p_rotation.txt, written by the Manager: the official campaigns, matched by their
 * map prefix (c1m = Dead Center), then the custom campaigns installed on the server, matched by their maps.
 * Campaigns whose first map the server doesn't have are skipped. Co-op and realism only.
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <left4dhooks>

#define PLUGIN_VERSION "1.0.0"
#define MAX_CAMPAIGNS  64
#define VOTE_CHOICES   5

public Plugin myinfo =
{
	name = "[L4D2] Campaign Rotation",
	author = "L4D2 8-Player Co-op Manager",
	description = "Moves to another campaign after a finale: next, random or by vote",
	version = PLUGIN_VERSION,
	url = ""
};

enum
{
	Mode_Stay,
	Mode_Next,
	Mode_Random,
	Mode_Vote
}

static const char g_sModeName[][] = { "stay", "next", "random", "vote" };

ConVar g_cvMode, g_cvDelay, g_cvWipes, g_cvVoteTime;

int g_iCount;
char g_sTitle[MAX_CAMPAIGNS][64];
char g_sFirst[MAX_CAMPAIGNS][64];
char g_sPrefix[MAX_CAMPAIGNS][16];      // official: "c1m"
char g_sMaps[MAX_CAMPAIGNS][512];       // custom: " map1 map2 … "

int g_iWipes;
int g_iChosen = -1;                     // vote result waiting for the finale
bool g_bChanging;
int g_iVoteChoice[VOTE_CHOICES + 1];    // menu item -> campaign (last item: random)
bool g_bVoteForFinale;                  // the vote decides the change now (not just the next finale)

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
	CreateConVar("l4d2_8p_rotation_version", PLUGIN_VERSION, "Campaign Rotation version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvMode = CreateConVar("l4d2_8p_rotation_mode", "1", "After a finale: 0 = stay, 1 = next campaign, 2 = random, 3 = players vote.", FCVAR_NOTIFY, true, 0.0, true, 3.0);
	g_cvDelay = CreateConVar("l4d2_8p_rotation_delay", "15", "Seconds after the rescue (credits) before changing campaign.", FCVAR_NOTIFY, true, 3.0, true, 120.0);
	g_cvWipes = CreateConVar("l4d2_8p_rotation_wipes", "0", "Move on after this many wipes on a finale (0 = never).", FCVAR_NOTIFY, true, 0.0, true, 50.0);
	g_cvVoteTime = CreateConVar("l4d2_8p_rotation_vote_time", "20", "How long a campaign vote stays open, in seconds.", FCVAR_NOTIFY, true, 10.0, true, 60.0);
	AutoExecConfig(true, "l4d2_8p_rotation");

	HookEvent("finale_win", Event_FinaleWin, EventHookMode_PostNoCopy);
	HookEvent("mission_lost", Event_MissionLost, EventHookMode_PostNoCopy);
	RegConsoleCmd("sm_mapvote", Cmd_MapVote, "Vote for the next campaign.");
	RegAdminCmd("sm_nextcampaign", Cmd_NextCampaign, ADMFLAG_CHANGEMAP, "Move on to the next campaign now, as after a finale.");
	RegAdminCmd("sm_8protation", Cmd_Status, ADMFLAG_GENERIC, "Shows the campaign list and what happens after this finale.");
	LoadList();
}

public void OnMapStart()
{
	g_bChanging = false;
	LoadList();
	// a new campaign starts the wipe count and the vote over
	if (L4D_IsFirstMapInScenario())
	{
		g_iWipes = 0;
		g_iChosen = -1;
	}
}

void LoadList()
{
	g_iCount = 0;
	char path[PLATFORM_MAX_PATH];
	BuildPath(Path_SM, path, sizeof(path), "data/l4d2_8p_rotation.txt");
	KeyValues kv = new KeyValues("rotation");
	if (!kv.ImportFromFile(path) || !kv.GotoFirstSubKey())
	{
		delete kv;
		return;
	}
	do
	{
		if (g_iCount >= MAX_CAMPAIGNS) break;
		kv.GetString("title", g_sTitle[g_iCount], sizeof(g_sTitle[]));
		kv.GetString("map", g_sFirst[g_iCount], sizeof(g_sFirst[]));
		kv.GetString("prefix", g_sPrefix[g_iCount], sizeof(g_sPrefix[]));
		char maps[512];
		kv.GetString("maps", maps, sizeof(maps));
		FormatEx(g_sMaps[g_iCount], sizeof(g_sMaps[]), " %s ", maps);
		if (g_sFirst[g_iCount][0]) g_iCount++;
	}
	while (kv.GotoNextKey());
	delete kv;
}

/** The list index of the campaign this map belongs to, or -1. */
int Current()
{
	char map[64], padded[72];
	GetCurrentMap(map, sizeof(map));
	FormatEx(padded, sizeof(padded), " %s ", map);
	for (int i = 0; i < g_iCount; i++)
	{
		if (g_sPrefix[i][0] && strncmp(map, g_sPrefix[i], strlen(g_sPrefix[i]), false) == 0) return i;
		if (StrContains(g_sMaps[i], padded, false) != -1) return i;
	}
	return -1;
}

bool Playable(int i)
{
	return i >= 0 && i < g_iCount && IsMapValid(g_sFirst[i]);
}

int NextAfter(int cur)
{
	for (int step = 1; step <= g_iCount; step++)
	{
		int i = ((cur < 0 ? -1 : cur) + step) % g_iCount;
		if (i != cur && Playable(i)) return i;
	}
	return -1;
}

int RandomOther(int cur)
{
	int pool[MAX_CAMPAIGNS], n = 0;
	for (int i = 0; i < g_iCount; i++) if (i != cur && Playable(i)) pool[n++] = i;
	return n == 0 ? -1 : pool[GetRandomInt(0, n - 1)];
}

bool CoopMode()
{
	return L4D_GetGameModeType() == GAMEMODE_COOP;
}

void Event_FinaleWin(Event event, const char[] name, bool dontBroadcast)
{
	if (!CoopMode() || g_cvMode.IntValue == Mode_Stay) return;
	MoveOn("after the rescue");
}

void Event_MissionLost(Event event, const char[] name, bool dontBroadcast)
{
	if (!CoopMode() || g_cvMode.IntValue == Mode_Stay || g_cvWipes.IntValue == 0 || !L4D_IsMissionFinalMap()) return;
	g_iWipes++;
	if (g_iWipes >= g_cvWipes.IntValue)
	{
		PrintToChatAll("\x04[8P]\x01 %d wipes on this finale: moving on to another campaign.", g_iWipes);
		MoveOn("after the wipes");
	}
	else
		PrintToChatAll("\x04[8P]\x01 Finale wipe %d of %d before the server moves on.", g_iWipes, g_cvWipes.IntValue);
}

/** Picks the next campaign for the mode (or starts the vote), then changes after the delay. */
void MoveOn(const char[] why)
{
	if (g_bChanging || g_iCount == 0) return;
	int cur = Current();
	if (g_cvMode.IntValue == Mode_Vote && g_iChosen < 0)
	{
		g_bVoteForFinale = true;
		if (StartVote(cur)) return;   // the vote's end calls Schedule
	}
	int next = g_iChosen >= 0 && Playable(g_iChosen) ? g_iChosen
		: g_cvMode.IntValue == Mode_Random ? RandomOther(cur) : NextAfter(cur);
	Schedule(next, why);
}

void Schedule(int next, const char[] why)
{
	if (next < 0)
	{
		LogMessage("No other playable campaign in data/l4d2_8p_rotation.txt; staying.");
		return;
	}
	g_bChanging = true;
	float delay = g_cvDelay.FloatValue;
	PrintToChatAll("\x04[8P]\x01 Next campaign: \x05%s\x01 in %d seconds.", g_sTitle[next], RoundToNearest(delay));
	LogMessage("%s: next campaign %s (%s) in %.0f s", why, g_sTitle[next], g_sFirst[next], delay);
	CreateTimer(delay, Timer_Change, next, TIMER_FLAG_NO_MAPCHANGE);
}

Action Timer_Change(Handle timer, int next)
{
	g_iWipes = 0;
	g_iChosen = -1;
	ForceChangeLevel(g_sFirst[next], "8P campaign rotation");
	return Plugin_Stop;
}

// ---------------------------------------------------------------- vote

bool StartVote(int cur)
{
	if (IsVoteInProgress()) return false;
	int clients[MAXPLAYERS + 1], n = 0;
	for (int c = 1; c <= MaxClients; c++) if (IsClientInGame(c) && !IsFakeClient(c)) clients[n++] = c;
	if (n == 0) return false;

	// a few other campaigns, the next one first, plus "surprise me"
	int picks = 0;
	int next = NextAfter(cur);
	if (next >= 0) g_iVoteChoice[picks++] = next;
	int tries = 0;
	while (picks < VOTE_CHOICES && tries++ < 200)
	{
		int r = RandomOther(cur);
		if (r < 0) break;
		bool dup = false;
		for (int k = 0; k < picks; k++) if (g_iVoteChoice[k] == r) dup = true;
		if (!dup) g_iVoteChoice[picks++] = r;
	}
	if (picks == 0) return false;

	Menu menu = new Menu(VoteHandler, MENU_ACTIONS_DEFAULT);
	menu.SetTitle("Next campaign?");
	for (int k = 0; k < picks; k++)
	{
		char id[8];
		IntToString(g_iVoteChoice[k], id, sizeof(id));
		menu.AddItem(id, g_sTitle[g_iVoteChoice[k]]);
	}
	menu.AddItem("-1", "Surprise me (random)");
	menu.ExitButton = false;
	menu.DisplayVote(clients, n, g_cvVoteTime.IntValue);
	PrintToChatAll("\x04[8P]\x01 Vote for the next campaign (%d s).", g_cvVoteTime.IntValue);
	return true;
}

int VoteHandler(Menu menu, MenuAction action, int param1, int param2)
{
	if (action == MenuAction_End)
		delete menu;
	else if (action == MenuAction_VoteCancel)
		Decide(-2);   // nobody voted (or the vote was cancelled): next in order
	else if (action == MenuAction_VoteEnd)
	{
		char id[8];
		menu.GetItem(param1, id, sizeof(id));
		int pick = StringToInt(id);
		Decide(pick < 0 ? RandomOther(Current()) : pick);
	}
	return 0;
}

/** -2: nobody voted. Stores the winner; changes now when the finale is already over. */
void Decide(int pick)
{
	int cur = Current();
	if (pick == -2 || !Playable(pick)) pick = NextAfter(cur);
	g_iChosen = pick;
	if (pick >= 0) PrintToChatAll("\x04[8P]\x01 The vote picked \x05%s\x01.", g_sTitle[pick]);
	if (g_bVoteForFinale) Schedule(pick, "after the vote");
}

Action Cmd_MapVote(int client, int args)
{
	if (g_cvMode.IntValue != Mode_Vote) { ReplyToCommand(client, "[8P] Campaign voting is off on this server."); return Plugin_Handled; }
	if (IsVoteInProgress()) { ReplyToCommand(client, "[8P] A vote is already running."); return Plugin_Handled; }
	g_bVoteForFinale = false;
	if (!StartVote(Current())) ReplyToCommand(client, "[8P] There's no other campaign to vote for.");
	return Plugin_Handled;
}

Action Cmd_NextCampaign(int client, int args)
{
	if (g_cvMode.IntValue == Mode_Vote) g_bVoteForFinale = true;
	int cur = Current();
	if (g_cvMode.IntValue == Mode_Vote && g_iChosen < 0 && StartVote(cur)) { ReplyToCommand(client, "[8P] Started the campaign vote."); return Plugin_Handled; }
	int next = g_iChosen >= 0 && Playable(g_iChosen) ? g_iChosen : g_cvMode.IntValue == Mode_Random ? RandomOther(cur) : NextAfter(cur);
	g_bChanging = false;
	Schedule(next, "admin");
	return Plugin_Handled;
}

Action Cmd_Status(int client, int args)
{
	int cur = Current();
	ReplyToCommand(client, "[8P rotation] mode %s, %d campaigns, this one: %s, finale map: %d, wipes %d/%d, vote result: %s",
		g_sModeName[g_cvMode.IntValue], g_iCount, cur >= 0 ? g_sTitle[cur] : "(not in the list)", L4D_IsMissionFinalMap(),
		g_iWipes, g_cvWipes.IntValue, g_iChosen >= 0 ? g_sTitle[g_iChosen] : "-");
	for (int i = 0; i < g_iCount; i++)
		ReplyToCommand(client, "[8P rotation]   %d. %s (%s)%s%s", i + 1, g_sTitle[i], g_sFirst[i], Playable(i) ? "" : " - not on this server", i == cur ? " <- now" : "");
	int next = g_cvMode.IntValue == Mode_Random ? -1 : NextAfter(cur);
	if (next >= 0) ReplyToCommand(client, "[8P rotation] next in order: %s", g_sTitle[next]);
	return Plugin_Handled;
}
