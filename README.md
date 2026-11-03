# L4D2 8-Player Co-op

One small Windows app for playing Left 4 Dead 2 co-op with up to 8 survivors (16 on a dedicated server). It has two sides, and **HOST | JOIN** at the top switches between them:

- **Host**: installs and checks the server mods (Metamod, SourceMod, L4DToolZ, l4dmultislots and the 5+ survivor fixes), then starts the server and your game.
- **Join**, for friends: paste the host's invite, check the server, join. No mods needed.

Both sides have a **skin picker**: choose which skin add-on you see for each survivor, weapon, item and infected. Packs ask first, and your picks are put back every time you launch.

**[Download the latest release](../../releases/latest)** and run **`L4D2-8P-Setup.exe`**: it asks whether you're hosting or joining a friend, then installs the app, set to open on that side, with Start menu and Desktop shortcuts (no admin needed; to remove it, use the Uninstall shortcut in its folder or Start menu, or Windows' Installed apps). On a first install, **Show me around** (ticked on the last page) opens the app with a one-minute tour of the parts you'll use; **? TOUR** at the top right brings it back. The same file works for everyone, so it's the one to send friends. `L4D2-8P-Coop.exe` is there too if you'd rather not install anything. Upgrading from 1.10 or earlier (two apps)? Run the new Setup once: it replaces both, and your settings carry over.

The full guide (setup, hosting modes, bot fill, item scaling, the 8-player HUD, troubleshooting) is in **[L4D2EightPlayer/README.md](L4D2EightPlayer/README.md)**.

Built-in server plugins (SourcePawn, compiled on install): `L4D2EightPlayer/Plugins`.

## Building

Needs the .NET SDK (any recent version; the app targets .NET Framework 4.8, which ships with Windows 10 and 11).

```
dotnet build L4D2EightPlayer -c Release
dotnet build L4D2EightPlayer.Setup -c Release
```

`tools/release.ps1` builds both, compiles the plugins, runs the self-tests (including a test install through the Setup) and packages a release (`-Publish` also tags it and creates the GitHub release).

MIT licensed; see [LICENSE](LICENSE).
