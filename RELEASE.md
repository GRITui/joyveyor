# JoyVeyor — Release (v1.0.0, macOS)

Standalone macOS player build for the JoyVeyor conveyor-logistics puzzle.
Built from the C++ sim core (`core/`) + Unity 6 sandbox (`unity-project/`).

## Build

From the repo root, one command:

```sh
./build_player.sh
```

What it does, in order:
1. Rebuilds the C++ sim bridge and installs it where Unity loads it:
   `sh unity/build_dylib.sh` → `unity-project/Assets/Plugins/libjv_unity.dylib`
   (the dylib is gitignored — a clean checkout must regenerate it before the
   sim can load, so the script always rebuilds it).
2. Runs the headless Unity editor to set Player Settings and build the player:
   `Unity -batchmode -nographics -quit -projectPath unity-project
    -executeMethod ReleaseBuild.Run -buildTarget Standalone`

`ReleaseBuild.Run` (in `unity-project/Assets/Editor/ReleaseBuild.cs`) sets the
Player Settings and builds:

| Setting | Value |
|---|---|
| Product name | `JoyVeyor` |
| Company | `GRITui` |
| Bundle identifier | `com.gritui.joyveyor` |
| Bundle version | `1.0.0` |
| Application icon | `Assets/Art/icon_placeholder.png` (1024×1024 placeholder) |
| Target | `StandaloneOSX` (macOS, arm64 on Apple Silicon) |

## Output

```
dist/JoyVeyor.app        (~67 MB, macOS arm64)
```

`dist/` is gitignored; it is produced by the build, not committed.

Requirements: macOS with Xcode command-line tools (clang), CMake ≥ 3.16, and
the Unity 6 editor at the path hardcoded in `build_player.sh`
(`/Applications/Unity/Hub/Editor/6000.6.0f1/...`). Adjust `UNITY=` in the script
if your install differs.

## Launch

```sh
open dist/JoyVeyor.app
```

The player boots the Unity 6 engine (Metal on Apple Silicon) into the main
menu, then level select (15 levels, progress persisted). Player log:
`~/Library/Logs/GRITui/JoyVeyor/Player.log`.

## Known limitations

- **Menus present.** Main menu, level select (15 levels), pause/complete/failed
  overlays and onboarding hints ship in this build (see `docs/ux/` for the open
  UX audit findings).
- **No store metadata.** No App Store / Mac App Store metadata, no code
  signing, no notarization. The app runs locally but is not store-ready.
- **Single item type.** The sim renders one item type (MVP item model, see
  README §6 open decision 2).
- **Placeholder icon.** The application icon is a generated placeholder, not
  final art.
- **Audio not baked into this build.** The SFX/ambient WAV baker
  (`Assets/Editor/AudioSynth.cs`) is present but the baked audio is a Sprint 8
  deliverable; run `Joyveyor/Bake Audio` in the editor to generate it.

## Verification (this build)

- C++ core: all 11 test suites green (0 failures); PlayTest invariants=OK; HudTest + MenuTest PASS (re-verified on rebuild from be885c4).
- Unity build: `result=Succeeded`, 0 errors, 69,906,177 B.
- Launch: player process started, engine initialized on Metal (Apple M1),
  1920×1080 surface created, no errors/exceptions in `Player.log`, stable for
  70+ s.
