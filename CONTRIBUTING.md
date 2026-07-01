# Contributing to LanSpire

Thanks for your interest in contributing! This mod is a community project and all contributions are welcome.

## Prerequisites

- **.NET SDK 9.0+**
- **Slay the Spire 2** game files (for build references)
- Windows (the mod targets the Windows x86_64 game build)

## Setup

1. Clone the repo:

```bash
git clone https://github.com/Madlezz/LanSpire.git
cd LanSpire
```

2. Tell the build where your game is installed. Either:

```bash
# Option A: environment variable
export STS2_GAME_DIR="/path/to/Slay the Spire 2"

# Option B: local config file (not committed to git)
echo '/path/to/Slay the Spire 2' > .sts2-game-dir
```

3. Build:

```bash
cd LanSpire
dotnet build -c Release
```

Output: `bin/Release/net9.0/LanSpire.dll`

## Pre-commit Hook

The repo ships a pre-commit hook in `.githooks/pre-commit` that:

1. Rejects em dash (`--`, U+2014) and en dash (`-`, U+2013) in `.cs` and `.md` files
2. Builds the project in Release mode and rejects on errors

To enable it:

```bash
git config core.hooksPath .githooks
```

If you skip this, builds will still work but you lose the guardrails.

## Code Style

- **No em/en dashes.** Use regular hyphens (`-`) or double hyphens (`--`) in code and docs. The pre-commit hook enforces this.
- **Nullable is disabled** in the `.csproj`. Be defensive with null checks, especially in reflection paths.
- **No implicit usings.** All `using` statements must be explicit.
- **Bilingual strings** use `Strings.T("Chinese", "English")`. If you add user-facing text, provide both languages.

## Project Structure

```
LanSpire/
  LanSpire.csproj       - Project file (references game DLLs)
  LanSpire.json         - Mod manifest (loaded by game's mod loader)
  Plugin.cs             - Entry point, Harmony patch orchestration
  Helpers/
    LanConfig.cs        - File-based JSON config reader/writer
    NetworkHelpers.cs   - IP parsing, endpoint validation
    PatchHelper.cs      - Logging, error reporting
    Strings.cs          - Bilingual string helper
  Patches/
    LanMultiplayerPatches.cs          - Partial class root
    LanMultiplayerPatches.Menu.cs     - Host/Join LAN button injection
    LanMultiplayerPatches.Platform.cs - Player identity, name sync
    LanMultiplayerPatches.Settings.cs - In-game settings screen
    LanMultiplayerPatches.Bootstrap.cs- Multiplayer mode bootstrap
    SteamSkipPatches.cs               - Steam platform skip
    JoinHost.cs          - Join flow, scan, retry, connection history
    JoinScreen.cs        - Join screen UI
    HostFlow.cs          - Host flow, IP display, VPN detection
    SyncStallRecovery.cs - Resilience: timeout, stall recovery
    Toast.cs             - Toast notifications
    PlayerCount.cs       - In-game player count indicator
```

## Testing

Unit tests cover pure-logic helpers that don't depend on Godot or the game runtime:

```bash
export STS2_GAME_DIR="/path/to/Slay the Spire 2"
cd LanSpireTests
dotnet test
```

Current coverage:
- `NetworkHelpers.TryParseEndpoint` -- IP:port parsing, inline port extraction, validation
- `NetworkHelpers.TryExtractInlinePort` -- inline port extraction from "IP:port" format
- `NetworkHelpers.NormalizeText` -- text normalization and trimming

Before submitting:

1. Build succeeds: `dotnet build -c Release` (in `LanSpire/`)
2. Tests pass: `dotnet test` (in `LanSpireTests/`)
3. Manual test: copy `bin/Release/net9.0/LanSpire.dll` + `LanSpire.json` to your game's `mods/LanSpire/` folder
4. Launch the game and verify the LAN buttons appear in Multiplayer
5. Test host + join with a second player if possible

## Filing Issues

Use [GitHub Issues](https://github.com/Madlezz/LanSpire/issues). Include:

- STS2 game version
- LanSpire version
- What you expected vs what happened
- Relevant lines from `godot.log` (messages tagged `[LanSpire]`)

## License

By contributing, you agree your contributions are licensed under the MIT license, same as the rest of the project.
