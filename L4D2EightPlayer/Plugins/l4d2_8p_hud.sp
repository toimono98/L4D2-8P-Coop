/**
 * [L4D2] 8-Player Survivor HUD
 *
 * The stock HUD shows a player and the first four other survivors (by player slot); anyone after that has no
 * panel. This lists exactly those survivors in a column under the weapon slots:
 *  - their health and state, through the game's scripted HUD
 *    (the game-rules entity's m_*ScriptedHUD* props, drawn by each client's own game; written directly, the
 *    way fbef0102's l4d2_cs_kill_hud does). It stays visible while Tab is held, below the scoreboard's four
 *    rows. (A per-player SourceMod panel for Tab was tried and dropped: the game draws it behind the
 *    scoreboard.)
 *
 * Two looks (l4d2_8p_hud_style):
 *  0  plain text that works for everyone: "Name  hp+temp  ||||||||--  STATE", in a centred 4:3 area.
 *  1  for players who all have the 8-Player HUD add-on (l4d2_8p_hud.vpk, from the Manager or Join app):
 *     a dark box per survivor holding their name and state (health, or SMOKED / POUNCED / RIDDEN /
 *     CHARGED / BILED / LEDGE / DOWN / DEAD), a health bar coloured by state, and the game's own icon for
 *     each item they carry, anywhere on screen. The add-on gives every scripted-HUD box a fixed job and look
 *     (text box, bar of one colour, icons), so a survivor is drawn with several boxes, and items are matched
 *     to boxes that can show them; g_iTextSlots/g_iBarSlots/g_iBoxFl/g_iBoxIm must match
 *     HudAddon/build_addon.py. Without the add-on these show as stray grey boxes.
 *
 * Clients only draw scripted-HUD slots in "challenge mode" (the state a mutation or a game-mode script
 * sets), so while the strip is on this sets the game rules' m_bChallengeModeActive, and clears it again
 * when the strip goes off or the plugin unloads. Like a mutation, that can keep some achievements from
 * unlocking during the game.
 *
 * Requires Left 4 DHooks (temporary health and the game mode type).
 */
#pragma semicolon 1
#pragma newdecls required

#include <sourcemod>
#include <sdktools>
#include <left4dhooks>

#define PLUGIN_VERSION "1.1.0"

public Plugin myinfo =
{
	name = "[L4D2] 8-Player Survivor HUD",
	author = "L4D2 8-Player Co-op Manager",
	description = "Health and state of every survivor in 5+ survivor co-op",
	version = PLUGIN_VERSION,
	url = ""
};

// Scripted-HUD values (the game's HUD_FLAG_* / slot constants)
#define HUD_FLAG_NOBG           64
#define HUD_FLAG_ALIGN_LEFT     256
#define HUD_FLAG_TEAM_SURVIVORS 1024
#define HUD_FLAG_TEXT           8192   // without it the slot never draws
#define HUD_FLAG_NOTVISIBLE     16384

#define TEAM_SURVIVOR 2
#define MAX_SHOWN     8
#define HUD_SLOTS     15

// Plain style: eight HUD slots, skipping 6 (the ticker, which scrolls).
static const int g_iSlots[MAX_SHOWN] = { 0, 1, 2, 3, 4, 5, 7, 8 };

// Add-on style: what the add-on made of each box (HudAddon/build_addon.py SLOTS).
#define ADDON_ROWS 4
static const int g_iTextSlots[ADDON_ROWS] = { 0, 1, 2, 3 };   // dark box, white text
enum { Bar_Ok, Bar_Hurt, Bar_Red, Bar_Count }
static const int g_iBarSlots[Bar_Count][3] = { { 4, 5, 7 }, { 8, -1, -1 }, { 9, -1, -1 } };
static const int g_iBarFallback[Bar_Count][2] = { { Bar_Hurt, Bar_Red }, { Bar_Red, Bar_Ok }, { Bar_Hurt, Bar_Ok } };
// Inventory items with an icon (the game's own icon_* textures).
enum { Item_Kit, Item_Defib, Item_Fire, Item_Boom, Item_Pills, Item_Adren, Item_Molotov, Item_Pipe, Item_Bile, Item_Count }
// Every box can show the item of its Fl panel (icon boxes only) or of its Im panel, which the add-on parks
// IM_DROP below the box: placing the box that much higher shows the Im icon and pushes Fl off the screen.
static const int g_iBoxFl[HUD_SLOTS] = { -1, -1, -1, -1, -1, -1, Item_Kit, -1, -1, -1, Item_Kit, Item_Pills, Item_Pills, Item_Molotov, Item_Pipe };
static const int g_iBoxIm[HUD_SLOTS] = { Item_Pipe, Item_Adren, Item_Defib, Item_Kit, Item_Pipe, Item_Molotov, Item_Fire, Item_Pills,
                                         Item_Pills, Item_Molotov, Item_Boom, Item_Defib, Item_Adren, Item_Bile, Item_Bile };
#define IM_DROP    (2000.0 / 480.0)   // build_addon.py IM_DROP, as a fraction of the screen height

// Add-on layout, in pixels of a 2560x1440 screen (turned into fractions of the screen).
#define ROW_W      320.0
#define ROW_H      52.0
#define ROW_GAP    6.0
#define TEXT_PAD   9.0    // build_addon.py MARGIN["text"]: 3 HUD units
#define BAR_INSET  10.0
#define BAR_TOP    40.0
#define BAR_H      6.0
#define ICON_SIZE  28.0
#define ICON_TOP   10.0
#define ICON_GAP   4.0
#define TEXT_EM    23.0   // the scripted HUD's font (Stubble bold) draws about 23 px per em at 1440p

// Advance widths (1000 per em) of Stubble bold for ASCII 32..126, to fit names in the room left of the icons.
static const int g_iTextWidth[95] = {
	321, 233, 318, 522, 624, 707, 590, 228, 302, 302, 364, 519, 252, 384, 206, 347,
	600, 402, 600, 559, 600, 498, 591, 492, 596, 499, 206, 252, 522, 512, 522, 492,
	733, 653, 637, 642, 637, 504, 505, 672, 619, 248, 496, 672, 541, 861, 592, 734,
	564, 703, 648, 605, 606, 598, 551, 937, 580, 610, 549, 265, 338, 281, 426, 492,
	284, 573, 523, 561, 600, 588, 336, 534, 545, 243, 314, 575, 242, 842, 563, 599,
	561, 510, 435, 508, 342, 520, 533, 707, 496, 543, 475, 343, 233, 357, 522,
};

ConVar g_cvItDuration;

ConVar g_cvEnable, g_cvShow, g_cvStyle, g_cvLayout, g_cvX, g_cvRowY, g_cvAddonX, g_cvAddonY, g_cvBg;
bool g_bLayoutSent, g_bHudShown, g_bChallengeForced;
bool g_bSlotWritten[HUD_SLOTS];

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
	CreateConVar("l4d2_8p_hud_version", PLUGIN_VERSION, "8-Player Survivor HUD version", FCVAR_NOTIFY | FCVAR_DONTRECORD);
	g_cvEnable = CreateConVar("l4d2_8p_hud_enable", "1", "Show the always-on survivor strip (1 = on, 0 = off).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvShow = CreateConVar("l4d2_8p_hud_show", "0", "Who to list: 0 = only survivors the stock HUD has no panel for, 1 = every survivor (the add-on look shows at most 4).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvStyle = CreateConVar("l4d2_8p_hud_style", "0", "Look: 0 = plain text, works for everyone; 1 = health bars and item icons, for when every player has the 8-Player HUD add-on (players without it see stray boxes).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvLayout = CreateConVar("l4d2_8p_hud_layout", "1", "Plain style layout: 1 = one column on the right, under the weapon slots; 0 = rows of four above the bottom panels.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvX = CreateConVar("l4d2_8p_hud_x", "0.828", "Plain style, layout 1: left edge of the column. HUD x spans a centred 4:3 area and the game clips boxes at its edges, so 0.828 puts the column against the right edge of that area, under the weapon slots, on any screen shape.", FCVAR_NOTIFY, true, 0.0, true, 0.83);
	g_cvRowY = CreateConVar("l4d2_8p_hud_y", "0.635", "Plain style: top of the list, as a fraction of screen height (the weapon slots end near 0.62; the stock panels start near 0.88).", FCVAR_NOTIFY, true, 0.0, true, 0.95);
	g_cvAddonX = CreateConVar("l4d2_8p_hud_addon_x", "0.866", "Add-on style: left edge of the column, as a fraction of the full screen width. 0.866 puts its right edge under the weapon slots' on 16:9 screens.", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	g_cvAddonY = CreateConVar("l4d2_8p_hud_addon_y", "0.619", "Add-on style: top of the column, as a fraction of screen height (the weapon slots end near 0.61).", FCVAR_NOTIFY, true, 0.0, true, 0.95);
	g_cvBg = CreateConVar("l4d2_8p_hud_background", "1", "Plain style: draw a dark box behind each survivor (1 = on, 0 = off).", FCVAR_NOTIFY, true, 0.0, true, 1.0);
	AutoExecConfig(true, "l4d2_8p_hud");
	g_cvItDuration = FindConVar("survivor_it_duration");

	g_cvEnable.AddChangeHook(OnLayoutCvarChanged);
	g_cvStyle.AddChangeHook(OnStyleCvarChanged);
	g_cvLayout.AddChangeHook(OnLayoutCvarChanged);
	g_cvX.AddChangeHook(OnLayoutCvarChanged);
	g_cvRowY.AddChangeHook(OnLayoutCvarChanged);
	g_cvBg.AddChangeHook(OnLayoutCvarChanged);

	HookEvent("round_start", Event_RoundStart, EventHookMode_PostNoCopy);
	RegAdminCmd("sm_8phud_check", Cmd_Check, ADMFLAG_ROOT, "Prints the current strip text.");
	// The update timer starts in OnMapStart (also called after a late load).
}

public void OnMapStart()
{
	g_bLayoutSent = false;
	g_bHudShown = false;
	g_bChallengeForced = false; // a new map has a new game-rules entity
	for (int i = 0; i < HUD_SLOTS; i++) g_bSlotWritten[i] = false;
	CreateTimer(0.5, Timer_Update, _, TIMER_REPEAT | TIMER_FLAG_NO_MAPCHANGE);
}

public void OnPluginEnd()
{
	HideStrip();
}

void OnLayoutCvarChanged(ConVar convar, const char[] oldValue, const char[] newValue)
{
	g_bLayoutSent = false;
}

void OnStyleCvarChanged(ConVar convar, const char[] oldValue, const char[] newValue)
{
	HideStrip(); // the two looks use the boxes differently
}

void Event_RoundStart(Event event, const char[] name, bool dontBroadcast)
{
	// A round restart can reset the scripted HUD: send the layout again.
	g_bLayoutSent = false;
}

// ----------------------------------------------------------------------------- survivors

/** Survivors in player-slot (client index) order, the order the stock HUD fills its teammate panels in. */
int CollectSurvivors(int[] list)
{
	int count;
	for (int c = 1; c <= MaxClients; c++)
		if (IsClientInGame(c) && GetClientTeam(c) == TEAM_SURVIVOR) list[count++] = c;
	return count;
}

/**
 * The survivors without a stock panel. Each player sees themselves plus the first four other survivors, so
 * for anyone in the first five slots the 6th onward have no panel. The list is the same for everyone, so if
 * a player sits in slot 6 or later, the 5th is missing for them too and is included.
 */
int HiddenSurvivors(const int[] all, int count, int[] out)
{
	int from = 5;
	for (int i = 5; i < count; i++)
		if (!IsFakeClient(all[i])) { from = 4; break; }
	int n;
	for (int i = from; i < count; i++) out[n++] = all[i];
	return n;
}


/** Player or bot name, quotes and backslashes removed. Extra bots copy existing characters, so the name is what tells them apart. */
void PlayerLabel(int client, char[] buffer, int maxlength)
{
	char name[64];
	GetClientName(client, name, sizeof(name));
	int n;
	for (int i = 0; name[i] != '\0' && n < 14 && n < maxlength - 1; i++)
	{
		if (name[i] == '"' || name[i] == '\\' || (name[i] >= 0 && name[i] < 32)) continue;
		buffer[n++] = name[i];
	}
	// don't leave half a UTF-8 character at the end
	while (n > 0 && (buffer[n - 1] & 0xC0) == 0x80) n--;
	if (n > 0 && (buffer[n - 1] & 0xC0) == 0xC0) n--;
	buffer[n] = '\0';
}

/** The pinning special's verb, or "" when nobody has hold of the survivor. */
void PinnedBy(int client, char[] buffer, int maxlength)
{
	buffer[0] = '\0';
	if (GetEntPropEnt(client, Prop_Send, "m_tongueOwner") > 0) strcopy(buffer, maxlength, "SMOKED");
	else if (GetEntPropEnt(client, Prop_Send, "m_pounceAttacker") > 0) strcopy(buffer, maxlength, "POUNCED");
	else if (GetEntPropEnt(client, Prop_Send, "m_jockeyAttacker") > 0) strcopy(buffer, maxlength, "RIDDEN");
	else if (GetEntPropEnt(client, Prop_Send, "m_pummelAttacker") > 0 || GetEntPropEnt(client, Prop_Send, "m_carryAttacker") > 0)
		strcopy(buffer, maxlength, "CHARGED");
}

/** e.g. "Nick  87+12  ||||||||--  B&W" */
void SurvivorLine(int client, bool withPing, char[] buffer, int maxlength)
{
	char name[16], who[32], state[16];
	PlayerLabel(client, name, sizeof(name));
	strcopy(who, sizeof(who), name); // players by name, bots by their own (character) name

	if (!IsPlayerAlive(client))
	{
		FormatEx(buffer, maxlength, "%s  DEAD", who);
		return;
	}

	int hp = GetClientHealth(client);
	int temp = RoundToFloor(L4D_GetTempHealth(client));
	bool incap = GetEntProp(client, Prop_Send, "m_isIncapacitated") != 0;
	bool ledge = GetEntProp(client, Prop_Send, "m_isHangingFromLedge") != 0;
	bool bw = GetEntProp(client, Prop_Send, "m_bIsOnThirdStrike") != 0;

	if (ledge) strcopy(state, sizeof(state), "LEDGE");
	else if (incap) strcopy(state, sizeof(state), "DOWN");
	else if (bw) strcopy(state, sizeof(state), "B&W");
	else state[0] = '\0';

	// Ten-step bar for permanent + temporary health out of 100 (an incapped survivor has 300 to bleed out).
	int total = incap ? hp / 3 : hp + temp;
	if (total > 100) total = 100;
	if (total < 0) total = 0;
	char bar[16];
	int filled = (total + 5) / 10;
	for (int i = 0; i < 10; i++) bar[i] = i < filled ? '|' : '-';
	bar[10] = '\0';

	char hpText[16];
	if (incap) FormatEx(hpText, sizeof(hpText), "%d", hp);
	else if (temp > 0) FormatEx(hpText, sizeof(hpText), "%d+%d", hp, temp);
	else FormatEx(hpText, sizeof(hpText), "%d", hp);

	char ping[16];
	ping[0] = '\0';
	if (withPing && !IsFakeClient(client)) FormatEx(ping, sizeof(ping), "  %dms", RoundToNearest(GetClientAvgLatency(client, NetFlow_Outgoing) * 1000.0));

	FormatEx(buffer, maxlength, "%s  %s  %s%s%s%s", who, hpText, bar, state[0] ? "  " : "", state, ping);
}

/** Covered in Boomer bile (vomited on or hit by a bile blast), which lasts survivor_it_duration seconds. */
bool Boomed(int client)
{
	// the time it hit; a send prop in some builds, server data only in others
	static int where = -1; // 0 none, 1 send, 2 data
	if (where < 0) where = HasEntProp(client, Prop_Send, "m_vomitStart") ? 1 : (HasEntProp(client, Prop_Data, "m_vomitStart") ? 2 : 0);
	if (where == 0) return false;
	float start = GetEntPropFloat(client, where == 1 ? Prop_Send : Prop_Data, "m_vomitStart");
	float duration = g_cvItDuration != null ? g_cvItDuration.FloatValue : 20.0;
	return start > 0.0 && GetGameTime() - start < duration;
}

/** The Item_* in that inventory slot (2 throwable, 3 kit/defib/ammo pack, 4 pills/adrenaline), or -1. */
int ItemOf(int client, int slot)
{
	int weapon = GetPlayerWeaponSlot(client, slot);
	if (weapon <= 0) return -1;
	char cls[40];
	GetEdictClassname(weapon, cls, sizeof(cls));
	if (StrEqual(cls, "weapon_first_aid_kit")) return Item_Kit;
	if (StrEqual(cls, "weapon_defibrillator")) return Item_Defib;
	if (StrEqual(cls, "weapon_upgradepack_incendiary")) return Item_Fire;
	if (StrEqual(cls, "weapon_upgradepack_explosive")) return Item_Boom;
	if (StrEqual(cls, "weapon_pain_pills")) return Item_Pills;
	if (StrEqual(cls, "weapon_adrenaline")) return Item_Adren;
	if (StrEqual(cls, "weapon_molotov")) return Item_Molotov;
	if (StrEqual(cls, "weapon_pipe_bomb")) return Item_Pipe;
	if (StrEqual(cls, "weapon_vomitjar")) return Item_Bile;
	return -1;
}

// ----------------------------------------------------------------------------- strip (scripted HUD)

bool HudAllowed()
{
	// Only in co-op style modes: versus/scavenge/mutations may run their own scripted HUD.
	int mode = L4D_GetGameModeType();
	return mode == GAMEMODE_COOP || mode == GAMEMODE_SURVIVAL;
}

/** Clients skip scripted-HUD slots unless challenge mode is on; turn it on only if we have to. */
void EnsureHudDrawn()
{
	if (GameRules_GetProp("m_bChallengeModeActive") != 0) return;
	GameRules_SetProp("m_bChallengeModeActive", 1, 1, 0, true);
	g_bChallengeForced = true;
}

void ReleaseHudDrawn()
{
	if (!g_bChallengeForced) return;
	GameRules_SetProp("m_bChallengeModeActive", 0, 1, 0, true);
	g_bChallengeForced = false;
}

int StripFlags()
{
	return HUD_FLAG_TEXT | HUD_FLAG_ALIGN_LEFT | HUD_FLAG_TEAM_SURVIVORS | (g_cvBg.BoolValue ? 0 : HUD_FLAG_NOBG);
}

void SetSlot(int slot, int flags, const char[] text)
{
	GameRules_SetProp("m_iScriptedHUDFlags", flags, _, slot, true);
	GameRules_SetPropString("m_szScriptedHUDStringSet", text, true, slot);
	g_bSlotWritten[slot] = flags != HUD_FLAG_NOTVISIBLE;
}

/** Sets a box's position and size, only sending what changed. */
void PlaceSlot(int slot, float x, float y, float w, float h)
{
	if (GameRules_GetPropFloat("m_fScriptedHUDPosX", slot) != x) GameRules_SetPropFloat("m_fScriptedHUDPosX", x, slot, true);
	if (GameRules_GetPropFloat("m_fScriptedHUDPosY", slot) != y) GameRules_SetPropFloat("m_fScriptedHUDPosY", y, slot, true);
	if (GameRules_GetPropFloat("m_fScriptedHUDWidth", slot) != w) GameRules_SetPropFloat("m_fScriptedHUDWidth", w, slot, true);
	if (GameRules_GetPropFloat("m_fScriptedHUDHeight", slot) != h) GameRules_SetPropFloat("m_fScriptedHUDHeight", h, slot, true);
}

/**
 * Plain style positions are fractions (x, y, width, height). y covers the full screen height; x covers a
 * centred 4:3 area and the game clips boxes to it, so on wide screens nothing reaches the far edges.
 */
void PlaceSlots()
{
	for (int i = 0; i < MAX_SHOWN; i++)
	{
		float x, y, w, h;
		if (g_cvLayout.IntValue == 1)
		{
			x = g_cvX.FloatValue; w = 0.165; h = 0.034;
			y = g_cvRowY.FloatValue + i * 0.038;
		}
		else
		{
			// The game draws each box about 3.5% wider than asked, so boxes this close would overlap
			// (a border line through the next box's text); 0.225 wide on a 0.243 step leaves a real gap.
			w = 0.225; h = 0.034;
			x = 0.02 + (i % 4) * 0.243;
			y = g_cvRowY.FloatValue + (i / 4) * 0.038;
		}
		PlaceSlot(g_iSlots[i], x, y, w, h);
	}
	g_bLayoutSent = true;
}

void HideStrip()
{
	if (!g_bHudShown) return;
	for (int s = 0; s < HUD_SLOTS; s++)
		if (g_bSlotWritten[s]) SetSlot(s, HUD_FLAG_NOTVISIBLE, "");
	ReleaseHudDrawn();
	g_bHudShown = false;
	g_bLayoutSent = false;
}

void UpdateStrip(const int[] list, int count)
{
	if (!g_bLayoutSent) PlaceSlots();
	EnsureHudDrawn();
	int flags = StripFlags();
	for (int i = 0; i < MAX_SHOWN; i++)
	{
		if (i < count)
		{
			char line[128];
			SurvivorLine(list[i], false, line, sizeof(line));
			SetSlot(g_iSlots[i], flags, line);
		}
		else if (g_bSlotWritten[g_iSlots[i]]) SetSlot(g_iSlots[i], HUD_FLAG_NOTVISIBLE, "");
	}
	g_bHudShown = true;
}

// ----------------------------------------------------------------------------- add-on style

float PxX(float px) { return px / 2560.0; }
float PxY(float px) { return px / 1440.0; }

/** A free bar box in that colour, else in the nearest free colour; -1 if none. */
int TakeBar(int colour, bool[] taken)
{
	for (int k = -1; k < 2; k++)
	{
		int c = k < 0 ? colour : g_iBarFallback[colour][k];
		for (int j = 0; j < 3; j++)
		{
			int s = g_iBarSlots[c][j];
			if (s >= 0 && !taken[s]) { taken[s] = true; return s; }
		}
	}
	return -1;
}

/** Width of text in the scripted HUD's font (1000 per em). */
int TextWidth(const char[] text)
{
	int w;
	for (int i = 0; text[i] != '\0'; i++)
	{
		int c = text[i] & 0xFF;
		if (c >= 32 && c < 127) w += g_iTextWidth[c - 32];
		else if (c >= 0xC0) w += 600; // first byte of a UTF-8 character
	}
	return w;
}

/**
 * What the row says after the name, and the bar's fill and colour. Pinned, hanging and down replace the
 * health number (the bar still shows it) and turn the bar red; Boomer bile replaces it but keeps the colour.
 */
bool RowState(int client, char[] state, int maxlength, float &fill, int &colour)
{
	fill = 0.0;
	colour = Bar_Ok;
	if (!IsPlayerAlive(client)) { strcopy(state, maxlength, "DEAD"); return false; }

	int hp = GetClientHealth(client);
	int temp = RoundToFloor(L4D_GetTempHealth(client));
	bool incap = GetEntProp(client, Prop_Send, "m_isIncapacitated") != 0;
	bool ledge = GetEntProp(client, Prop_Send, "m_isHangingFromLedge") != 0;
	if (incap || ledge)
	{
		fill = hp / 300.0; // 300 to bleed out
		colour = Bar_Red;
	}
	else
	{
		int total = hp + temp > 100 ? 100 : hp + temp;
		fill = total / 100.0;
		colour = total >= 40 ? Bar_Ok : (total >= 25 ? Bar_Hurt : Bar_Red);
	}

	char pinned[16];
	PinnedBy(client, pinned, sizeof(pinned));
	if (pinned[0] != '\0') { strcopy(state, maxlength, pinned); colour = Bar_Red; }
	else if (ledge) strcopy(state, maxlength, "LEDGE");
	else if (incap) strcopy(state, maxlength, "DOWN");
	else if (Boomed(client)) strcopy(state, maxlength, "BILED");
	else
	{
		if (temp > 0) FormatEx(state, maxlength, "%d+%d", hp, temp);
		else FormatEx(state, maxlength, "%d", hp);
		if (GetEntProp(client, Prop_Send, "m_bIsOnThirdStrike") != 0) StrCat(state, maxlength, " B&W");
	}
	return true;
}

// ---- items to boxes: a box can take an item if its Fl or Im icon is that item (bipartite matching)

#define MAX_ITEMS (ADDON_ROWS * 3)
int g_iItemType[MAX_ITEMS], g_iItemBox[MAX_ITEMS];
int g_iBoxItem[HUD_SLOTS];
bool g_bBoxFree[HUD_SLOTS], g_bBoxSeen[HUD_SLOTS];

bool BoxShows(int slot, int type) { return g_iBoxFl[slot] == type || g_iBoxIm[slot] == type; }

bool Augment(int item)
{
	int type = g_iItemType[item];
	for (int s = 0; s < HUD_SLOTS; s++)
	{
		if (!g_bBoxFree[s] || g_bBoxSeen[s] || !BoxShows(s, type)) continue;
		g_bBoxSeen[s] = true;
		if (g_iBoxItem[s] < 0 || Augment(g_iBoxItem[s]))
		{
			g_iBoxItem[s] = item;
			g_iItemBox[item] = s;
			return true;
		}
	}
	return false;
}

/** One row per survivor: a dark box with their name and state, a coloured bar, and an icon per item. */
void UpdateAddonStrip(const int[] list, int count)
{
	EnsureHudDrawn();
	bool taken[HUD_SLOTS];
	if (count > ADDON_ROWS) count = ADDON_ROWS;
	float x = g_cvAddonX.FloatValue;
	float rowY[ADDON_ROWS];
	char state[ADDON_ROWS][24];
	bool alive[ADDON_ROWS];

	// text boxes and bars first: those boxes are then not free for icons
	for (int i = 0; i < count; i++)
	{
		rowY[i] = g_cvAddonY.FloatValue + PxY(i * (ROW_H + ROW_GAP));
		taken[g_iTextSlots[i]] = true;
		float fill;
		int colour;
		alive[i] = RowState(list[i], state[i], sizeof(state[]), fill, colour);
		if (!alive[i] || fill <= 0.0) continue;
		int bar = TakeBar(colour, taken);
		if (bar < 0) continue;
		if (fill > 1.0) fill = 1.0;
		PlaceSlot(bar, x + PxX(BAR_INSET), rowY[i] + PxY(BAR_TOP), PxX((ROW_W - 2 * BAR_INSET) * fill), PxY(BAR_H));
		SetSlot(bar, HUD_FLAG_TEXT | HUD_FLAG_TEAM_SURVIVORS, "");
	}

	// items: kits and pills before throwables, so those win when boxes run out
	int items, itemRow[MAX_ITEMS], itemInv[MAX_ITEMS];
	static const int order[3] = { 3, 4, 2 };
	for (int k = 0; k < 3; k++)
		for (int i = 0; i < count; i++)
		{
			if (!alive[i]) continue;
			int type = ItemOf(list[i], order[k]);
			if (type < 0) continue;
			g_iItemType[items] = type;
			g_iItemBox[items] = -1;
			itemRow[items] = i;
			itemInv[items] = order[k];
			items++;
		}
	for (int s = 0; s < HUD_SLOTS; s++) { g_bBoxFree[s] = !taken[s]; g_iBoxItem[s] = -1; }
	for (int it = 0; it < items; it++)
	{
		for (int s = 0; s < HUD_SLOTS; s++) g_bBoxSeen[s] = false;
		Augment(it);
	}

	for (int i = 0; i < count; i++)
	{
		// icons right-aligned: pills/adrenaline, then kit/defib/ammo, then the throwable
		int shown, missing;
		for (int inv = 4; inv >= 2; inv--)
			for (int it = 0; it < items; it++)
			{
				if (itemRow[it] != i || itemInv[it] != inv) continue;
				int s = g_iItemBox[it];
				if (s < 0) { missing++; continue; }
				taken[s] = true;
				float right = x + PxX(ROW_W - BAR_INSET - shown * (ICON_SIZE + ICON_GAP));
				float y = rowY[i] + PxY(ICON_TOP) - (g_iBoxFl[s] == g_iItemType[it] ? 0.0 : IM_DROP);
				PlaceSlot(s, right - PxX(ICON_SIZE), y, PxX(ICON_SIZE), PxY(ICON_SIZE));
				SetSlot(s, HUD_FLAG_TEXT | HUD_FLAG_TEAM_SURVIVORS, "");
				shown++;
			}

		// "Name  STATE": the name is cut to fit the room left of the icons
		char name[16], text[64], tail[32];
		PlayerLabel(list[i], name, sizeof(name));
		if (missing > 0) FormatEx(tail, sizeof(tail), "  %s  +%d", state[i], missing);
		else FormatEx(tail, sizeof(tail), "  %s", state[i]);
		float room = ROW_W - 2 * TEXT_PAD - shown * (ICON_SIZE + ICON_GAP) - 4.0;
		int roomUnits = RoundToFloor(room / TEXT_EM * 1000.0) - TextWidth(tail);
		while (strlen(name) > 1 && TextWidth(name) > roomUnits)
		{
			int n = strlen(name) - 1;
			while (n > 1 && (name[n] & 0xC0) == 0x80) n--;
			name[n] = '\0';
		}
		FormatEx(text, sizeof(text), "%s%s", name, tail);

		int box = g_iTextSlots[i];
		// the add-on draws a text box's background TEXT_PAD wider on each side than its text
		PlaceSlot(box, x + PxX(TEXT_PAD), rowY[i], PxX(ROW_W - 2 * TEXT_PAD), PxY(ROW_H));
		SetSlot(box, HUD_FLAG_TEXT | HUD_FLAG_ALIGN_LEFT | HUD_FLAG_TEAM_SURVIVORS, text);
	}

	for (int s = 0; s < HUD_SLOTS; s++)
		if (!taken[s] && g_bSlotWritten[s]) SetSlot(s, HUD_FLAG_NOTVISIBLE, "");
	g_bHudShown = true;
}

// ----------------------------------------------------------------------------- timer

Action Timer_Update(Handle timer)
{
	int all[MAXPLAYERS + 1], list[MAXPLAYERS + 1];
	int total = CollectSurvivors(all), count;
	if (g_cvShow.BoolValue) { for (int i = 0; i < total; i++) list[i] = all[i]; count = total; }
	else count = HiddenSurvivors(all, total, list);

	if (g_cvEnable.BoolValue && HudAllowed() && count > 0)
	{
		if (g_cvStyle.IntValue == 1) UpdateAddonStrip(list, count);
		else UpdateStrip(list, count);
	}
	else HideStrip();
	return Plugin_Continue;
}

Action Cmd_Check(int client, int args)
{
	int all[MAXPLAYERS + 1], hidden[MAXPLAYERS + 1];
	int total = CollectSurvivors(all), n = HiddenSurvivors(all, total, hidden);
	for (int i = 0; i < total; i++)
	{
		char line[128];
		SurvivorLine(all[i], true, line, sizeof(line));
		bool listed = false;
		for (int k = 0; k < n; k++) if (hidden[k] == all[i]) listed = true;
		ReplyToCommand(client, "[8P HUD] slot %d: %s%s", i + 1, line, listed ? "   <- in the list (no stock panel)" : "");
	}
	return Plugin_Handled;
}
