"""Builds the 8-Player HUD client add-on, l4d2_8p_hud.vpk, that each player drops into left4dead2/addons.

It overrides two of the game's files (copies of the originals are in game/) and adds a few materials:
  scripts/hudlayout.res                the scripted-HUD area covers the whole screen instead of a centred 4:3
  resource/ui/hud/hudscriptedmode.res  every scripted-HUD box gets a fixed job and look (SLOTS below)
  materials/vgui/hud/l4d2_8p_*.vmt     a see-through image, and item icons that reuse the game's own
                                       textures (the large left half of each icon_*.vtf)

What the game won't take from an add-on: fonts. Its client scheme (fonts, font files) is read at startup,
before add-ons are mounted, so text stays in the scripted HUD's own font (Stubble bold, fixed size).

A box keeps its look, so the server (l4d2_8p_hud.sp) shows a survivor with several boxes: a dark text box
that also holds the bar, a bar box in the colour of their state, and icon boxes for their items.
g_iSlot* / g_iBox* in the plugin must match SLOTS.

usage: build_addon.py
"""
import os
import re
import struct
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(HERE, "game")
# Embedded in the Manager and the Join app (Core\HudAddon.cs installs it).
BUILD = os.path.join(HERE, "..", "L4D2EightPlayer", "Addon")

CLEAR = "l4d2_8p_clear"
# icon: (game texture, whether it holds a large and a small copy side by side)
ICON_TEXTURES = {
    "kit": ("vgui/hud/icon_first_aid_kit", True),
    "defib": ("vgui/hud/icon_defibrillator", True),
    "fire": ("vgui/hud/icon_upgradepack_incendiary", True),
    "boom": ("vgui/hud/icon_upgradepack_explosive", True),
    "pills": ("vgui/hud/icon_painpills", True),
    "adren": ("vgui/hud/icon_adrenaline", True),
    "molotov": ("vgui/hud/icon_molotov", True),
    "pipe": ("vgui/hud/icon_pipebomb", True),
    "bile": ("vgui/hud/icon_vomitjar", False),
}

# Every box has two image panels. Fl is the box's job (dark text box, bar colour or an icon); Im holds a
# second icon, IM_DROP units below the box, so it's off-screen while the box does its job. The server shows
# the Im icon instead by placing the box IM_DROP higher, which pushes Fl off the top of the screen. That way
# a spare text or bar box can still show an item, and each icon box can show either of two items.
IM_DROP = 2000   # HUD units (480 = screen height), so the same on every screen shape

# slot: (kind, text colour, Fl fill, Fl image, Im image)
TEXT_FG, TEXT_BG = "236 236 236 255", "8 8 8 168"
GREEN, YELLOW, RED = "127 209 42 255", "232 185 23 255", "216 50 42 255"
# The six icon boxes cover every item between them, each rarer item paired with a common one that has other
# boxes to go to; spare text and bar boxes add more of the common ones. With 3 survivors, text box 3 is free
# and 2 of the 5 bars, which 2 depending on health: so pills and molotov each sit on bars of two different
# colours, and one of them is always free.
SLOTS = {
    0: ("text", TEXT_FG, TEXT_BG, None, "pipe"), 1: ("text", TEXT_FG, TEXT_BG, None, "adren"),
    2: ("text", TEXT_FG, TEXT_BG, None, "defib"), 3: ("text", TEXT_FG, TEXT_BG, None, "kit"),
    4: ("bar_ok", TEXT_FG, GREEN, None, "pipe"), 5: ("bar_ok", TEXT_FG, GREEN, None, "molotov"),
    7: ("bar_ok", TEXT_FG, GREEN, None, "pills"),
    8: ("bar_hurt", TEXT_FG, YELLOW, None, "pills"),
    9: ("bar_red", TEXT_FG, RED, None, "molotov"),
    6: ("icon", TEXT_FG, None, "kit", "fire"), 10: ("icon", TEXT_FG, None, "kit", "boom"),
    11: ("icon", TEXT_FG, None, "pills", "defib"), 12: ("icon", TEXT_FG, None, "pills", "adren"),
    13: ("icon", TEXT_FG, None, "molotov", "bile"), 14: ("icon", TEXT_FG, None, "pipe", "bile"),
}

CLEAR_VMT = '''"UnlitGeneric"
{
	"$basetexture"	"vgui/white"
	"$translucent"	"1"
	"$alpha"	"0"
	"$vertexalpha"	"1"
	"$ignorez"	"1"
}
'''
# Same settings as the game's own icon_*.vmt; for textures with two copies, only the large left half.
ICON_VMT = '''"UnlitGeneric"
{
	"$basetexture"	"%s"
	"$translucent"	"1"
	"$alpha"	"1"
	"$vertexalpha"	"1"
	"$vertexcolor"	"1"
	"$ignorez"	"1"
	"$no_fullbright"	"1"
%s}
'''
HALF = '\t"$basetexturetransform"\t"center 0 0 scale 0.5 1 rotate 0 translate 0 0"\n'

ADDONINFO = '''"AddonInfo"
{
	"addonSteamAppID"	"550"
	"addontitle"		"8-Player HUD"
	"addonversion"		"1.0"
	"addontagline"		"Health bars and items for the survivors the stock HUD has no room for"
	"addonauthor"		"L4D2 8-Player Co-op Manager"
	"addonDescription"	"Restyles the game's scripted HUD for the 8-Player Survivor HUD server plugin: full-screen positions, health bars coloured by state, item icons."
	"addonContent_Script"	"0"
	"addonContent_HUD"	"1"
}
'''


def read(name):
    with open(os.path.join(GAME, name), "rb") as f:
        return f.read().decode("latin1")


def patch_hudlayout(text):
    m = re.search(r'(\tHudScriptedMode\s*\{)(.*?)(\n\t\})', text, re.S)
    assert m, "HudScriptedMode block not found"
    body = m.group(2)
    for key, value in (("xpos", "0"), ("ypos", "0"), ("wide", "f0"), ("tall", "f0")):
        body, n = re.subn(r'("%s"\s*)"[^"]*"' % key, r'\g<1>"%s"' % value, body)
        assert n == 1, key
    return text[:m.start(2)] + body + text[m.end(2):]


# The game sizes a box's backgrounds (ElemNNIm/Fl) as its text panel (ElemNNData, which the server places)
# grown by how much bigger they are in this file: 5 units each side across and 2 up and down in the stock
# file (15 x 6 px at 1440p). Bars and icons get none, so they come out exactly as placed; the text box gets
# a little room on the left and right, so its name doesn't touch the edge.
MARGIN = {"text": (3, 0)}   # (across, up/down) in the HUD's 640x480 units


def patch_scriptedmode(text):
    geometry = {}
    for m in re.finditer(r'\t"Elem(\d\d)Data"\s*\{(.*?)\}', text, re.S):
        geometry[int(m.group(1))] = {k: int(v) for k, v in re.findall(r'"(xpos|ypos|wide|tall)"\s*"(-?\d+)"', m.group(2))}

    def place(body, slot, kind, drop=0):
        g = geometry[slot]
        mx, my = MARGIN.get(kind, (0, 0))
        for key, value in (("xpos", g["xpos"] - mx), ("ypos", g["ypos"] - my + drop),
                           ("wide", g["wide"] + 2 * mx), ("tall", g["tall"] + 2 * my)):
            body, n = re.subn(r'("%s"\s*)"-?\d+"' % key, r'\g<1>"%d"' % value, body)
            assert n == 1, (slot, key)
        return body

    def icon(name):
        return "../vgui/hud/" + ("l4d2_8p_icon_" + name if name else CLEAR)

    def block(m):
        slot, part, body = int(m.group(2)), m.group(3), m.group(4)
        if slot not in SLOTS:
            return m.group(0)
        kind, fg, fill, fl_image, im_image = SLOTS[slot]
        kind = kind.split("_")[0]
        if part == "Data":
            body += '\t"fgcolor_override"\t"%s"\r\n\t\t"paintbackground"\t"0"\r\n\t' % fg
        elif part == "Im":
            # replaces the grey outline (the game resets this panel's colour itself, so colour can't hide it)
            body = re.sub(r'"image"\s*"[^"]*"', '"image"\t\t"%s"' % icon(im_image), body)
            body = place(body, slot, "icon", IM_DROP)
        elif part == "Fl":
            body = re.sub(r'"fillcolor"\s*"[^"]*"', '"fillcolor"\t"%s"' % (fill or "0 0 0 0"), body)
            if fl_image:
                body += '\t"image"\t\t"%s"\r\n\t' % icon(fl_image)
            body = place(body, slot, kind)
        return m.group(1) + body + m.group(5)

    out, n = re.subn(r'(\t"Elem(\d\d)(Data|Im|Fl)"\s*\{)(.*?)(\})', block, text, flags=re.S)
    assert n == 45, n
    return out


def vpk(files, out):
    """A single-file VPK v1 (what addons use): directory tree, then every file's data."""
    by_ext = {}
    for path, data in files.items():
        d, f = os.path.split(path)
        name, ext = os.path.splitext(f)
        by_ext.setdefault(ext.lstrip(".") or " ", {}).setdefault(d or " ", []).append((name, data))
    tree = bytearray()
    blob = bytearray()
    for ext in sorted(by_ext):
        tree += ext.encode() + b"\0"
        for d in sorted(by_ext[ext]):
            tree += d.encode() + b"\0"
            for name, data in sorted(by_ext[ext][d]):
                tree += name.encode() + b"\0"
                tree += struct.pack("<IHHIIH", zlib.crc32(data) & 0xFFFFFFFF, 0, 0x7FFF, len(blob), len(data), 0xFFFF)
                blob += data
            tree += b"\0"
        tree += b"\0"
    tree += b"\0"
    with open(out, "wb") as f:
        f.write(struct.pack("<III", 0x55AA1234, 1, len(tree)))
        f.write(tree)
        f.write(blob)


def main():
    os.makedirs(BUILD, exist_ok=True)

    def crlf(s):
        return s.replace("\n", "\r\n").encode("latin1")

    files = {
        "addoninfo.txt": crlf(ADDONINFO),
        "scripts/hudlayout.res": patch_hudlayout(read("hudlayout.res")).encode("latin1"),
        "resource/ui/hud/hudscriptedmode.res": patch_scriptedmode(read("hudscriptedmode.res")).encode("latin1"),
        "materials/vgui/hud/%s.vmt" % CLEAR: crlf(CLEAR_VMT),
    }
    for name, (texture, half) in ICON_TEXTURES.items():
        files["materials/vgui/hud/l4d2_8p_icon_%s.vmt" % name] = crlf(ICON_VMT % (texture, HALF if half else ""))
    out = os.path.join(BUILD, "l4d2_8p_hud.vpk")
    vpk(files, out)
    print("add-on: %s (%d bytes)" % (out, os.path.getsize(out)))
    for slot, (kind, _fg, _fill, fl_image, im_image) in sorted(SLOTS.items()):
        print("  %2d %-9s Fl %-8s Im %s" % (slot, kind, fl_image or "-", im_image))


if __name__ == "__main__":
    main()
