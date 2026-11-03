# L4D2 8-Player Co-op

Two small Windows programs for playing Left 4 Dead 2 co-op with up to 8 survivors (16 on a dedicated server):

- **L4D2-8P-Manager.exe** for the host: installs and checks the server mods (Metamod, SourceMod, L4DToolZ, l4dmultislots and the 5+ survivor fixes), then starts the server and your game.
- **L4D2-8P-Join.exe** for friends: paste the host's invite, check the server, join. No mods needed.

**[Download the latest release](../../releases/latest)** and run **`L4D2-8P-Setup.exe`**: it asks whether you're hosting or joining a friend, then installs the right app with Start menu and Desktop shortcuts (no admin needed; to remove it, use the Uninstall shortcut in its folder or Start menu, or Windows' Installed apps). The same file works for everyone, so it's the one to send friends. `L4D2-8P-Manager.exe` and `L4D2-8P-Join.zip` are there too if you'd rather not install anything.

The full guide (setup, hosting modes, bot fill, item scaling, the 8-player HUD, troubleshooting) is in **[L4D2EightPlayer/README.md](L4D2EightPlayer/README.md)**.

Built-in server plugins (SourcePawn, compiled on install): `L4D2EightPlayer/Plugins`.

## Building

Needs the .NET SDK (any recent version; the apps target .NET Framework 4.8, which ships with Windows 10 and 11).

```
dotnet build L4D2EightPlayer -c Release
dotnet build L4D2EightPlayer.Join -c Release
dotnet build L4D2EightPlayer.Setup -c Release
```

`tools/release.ps1` builds all three, compiles the plugins, runs the self-tests (including a test install through the Setup) and packages a release (`-Publish` also tags it and creates the GitHub release).

MIT licensed; see [LICENSE](LICENSE).
