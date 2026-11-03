"""Builds a fake left4dead2 folder full of skin add-ons, for the skin picker's tests (L4D2-8P-Coop.exe --skins-test).

    python tools/make_skin_fixtures.py <out folder>

Every VPK holds placeholder files at the real game paths, so the picker classifies them the way it would real
skins: a Nick model skin, a Nick texture-only skin, a survivors + AK-47 pack (Workshop), a rifle pack with a
weapon script (Workshop), Hunter and common-infected skins, a script mod and a sound mod (not skins), a
campaign that also replaces the AK-47, and copies of the HUD add-on and the 8 Slots Lobby Mod (never touched).
A survivor-icons mod shares HUD portraits with the pack, and a HUD mod shares the HUD add-on's layout file.
The addonlist.txt mixes "1", "0", a doubled backslash and missing entries (which the game treats as on).
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "HudAddon"))
from build_addon import vpk  # noqa: E402  (the same VPK writer the HUD add-on uses)


def info(title):
    return ('"AddonInfo"\n{\n\t"addontitle" "%s"\n\t"addonversion" "1"\n}\n' % title).encode()


JPEG = b"\xff\xd8\xff\xe0placeholder\xff\xd9"
ADDONS = {
    "nick_model.vpk": {"addoninfo.txt": info("Nick Model Skin"), "addonimage.jpg": JPEG,
                       "models/survivors/survivor_gambler.mdl": b"mdl", "models/survivors/survivor_gambler.vvd": b"vvd"},
    "nick_texture.vpk": {"addoninfo.txt": info("Nick Texture Skin"),
                         "materials/models/survivors/gambler/gambler_body.vtf": b"vtf"},
    "workshop/1000000001.vpk": {"addoninfo.txt": info("Survivors and AK Pack"),
                                "models/survivors/survivor_gambler.mdl": b"mdl", "models/survivors/survivor_producer.mdl": b"mdl",
                                "models/survivors/survivor_coach.mdl": b"mdl", "models/survivors/survivor_mechanic.mdl": b"mdl",
                                "models/v_models/v_rifle_ak47.mdl": b"mdl",
                                "materials/vgui/s_panel_gambler.vtf": b"vtf", "materials/vgui/s_panel_producer.vtf": b"vtf"},
    "workshop/1000000002.vpk": {"addoninfo.txt": info("Rifle Pack"),
                                "models/v_models/v_rifle_ak47.mdl": b"mdl", "models/v_models/v_rifle.mdl": b"mdl",
                                "scripts/weapon_rifle_ak47.txt": b"WeaponData {}"},
    "hunter_skin.vpk": {"addoninfo.txt": info("Hunter Skin"), "materials/models/infected/hunter/hunter_01.vtf": b"vtf"},
    "commons_skin.vpk": {"addoninfo.txt": info("Commons Skin"),
                         "materials/models/infected/common/baggage_handler/common_baggagehandler_upper_01.vtf": b"vtf"},
    "script_mod.vpk": {"addoninfo.txt": info("Script Mod"), "scripts/vscripts/director_base_addon.nut": b"// nothing"},
    "sound_mod.vpk": {"addoninfo.txt": info("Sound Mod"), "sound/player/survivor/voice/gambler/hey01.wav": b"RIFF"},
    "campaign_with_ak.vpk": {"addoninfo.txt": info("Campaign With AK"), "missions/zzcamp.txt": b'"mission" {}',
                             "maps/zz_m1.bsp": b"VBSP", "models/v_models/v_rifle_ak47.mdl": b"mdl"},
    "l4d2_8p_hud.vpk": {"addoninfo.txt": info("8-Player HUD"), "models/v_models/v_rifle.mdl": b"mdl", "scripts/hudlayout.res": b"res"},
    # not skins, but they replace files other add-ons have: the survivor pack's HUD portraits, the 8-player HUD's layout
    "survivor_icons.vpk": {"addoninfo.txt": info("Survivor Icons"), "materials/vgui/s_panel_gambler.vtf": b"vtf",
                           "materials/vgui/s_panel_producer.vtf": b"vtf", "materials/vgui/select_zoey.vtf": b"vtf"},
    "hud_mod.vpk": {"addoninfo.txt": info("HUD Mod"), "scripts/hudlayout.res": b"res"},
    "workshop/2754956355.vpk": {"addoninfo.txt": info("8 Slots Lobby Mod"), "models/v_models/v_rifle.mdl": b"mdl"},
}

ADDONLIST = """"AddonList"
{
\t"nick_model.vpk"\t\t"1"
\t"nick_texture.vpk"\t\t"0"
\t"workshop\\\\1000000001.vpk"\t\t"1"
\t"workshop\\1000000002.vpk"\t\t"1"
\t"campaign_with_ak.vpk"\t\t"1"
\t"l4d2_8p_hud.vpk"\t\t"1"
}
"""


def main():
    out = os.path.join(sys.argv[1], "left4dead2")
    for rel, files in ADDONS.items():
        path = os.path.join(out, "addons", *rel.split("/"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        vpk(files, path)
    # a Workshop thumbnail next to the pack, the way Steam stores them
    with open(os.path.join(out, "addons", "workshop", "1000000001.jpg"), "wb") as f:
        f.write(JPEG)
    with open(os.path.join(out, "addonlist.txt"), "w", newline="\n") as f:
        f.write(ADDONLIST)
    print("fixtures in", out)


if __name__ == "__main__":
    main()
