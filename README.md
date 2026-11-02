# L4D2 8-Player Co-op

Two small Windows programs for playing Left 4 Dead 2 co-op with up to 8 survivors (16 on a dedicated server):

- **L4D2-8P-Manager.exe** for the host: installs and checks the server mods (Metamod, SourceMod, L4DToolZ, l4dmultislots and the 5+ survivor fixes), then starts the server and your game.
- **L4D2-8P-Join.exe** for friends: paste the host's invite, check the server, join. No mods needed.

**[Download the latest release](../../releases/latest)**: `L4D2-8P-Manager.exe` for hosts, `L4D2-8P-Join.zip` to share with friends.

The full guide (setup, hosting modes, bot fill, item scaling, the 8-player HUD, troubleshooting) is in **[L4D2EightPlayer/README.md](L4D2EightPlayer/README.md)**.

Built-in server plugins (SourcePawn, compiled on install): `L4D2EightPlayer/Plugins`.

## Building

Needs the .NET SDK (any recent version; the apps target .NET Framework 4.8, which ships with Windows 10 and 11).

```
dotnet build L4D2EightPlayer -c Release
dotnet build L4D2EightPlayer.Join -c Release
```

`tools/release.ps1` builds both, compiles the plugins, runs the self-tests and packages a release (`-Publish` also tags it and creates the GitHub release).

MIT licensed; see [LICENSE](LICENSE).
