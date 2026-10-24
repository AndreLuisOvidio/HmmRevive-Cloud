# CLAUDE.md

Guidance for Claude working in this repository.

## What this is

**HMM Revive**: an unofficial fan mod that brings back **Heavy Metal Machines** (Hoplon, servers shut down in 2022)
for private matches among friends, with no dependency on Hoplon's backend ("Swordfish", dead).

- A **headless match server** built at runtime from the game's own shipped server code (the server scene was never
  shipped, so the mod rebuilds it), with bots.
- **Clients connect directly** to it using the game's built-in dev mode (`SkipSwordfish` / `DirectMatch`), usually
  over Tailscale/ZeroTier/LAN.
- A **launcher** (`HMM-Revive.exe`) with a local web UI: host/discover/join lobbies, team/car/skin pick, draft,
  spectators, rematch.

Target game build: Steam `Release.15.00.250` — Unity 2018.4.24f1, Mono, **legacy .NET 3.5 runtime**.
Older targets, via `HmmRevive.Legacy` (Unity 4.6.8, Mono, no Zenject): the **November 2017 build** (`2017`) and the
**September 2017 build** (`2017sep`, `#if Y2017SEP`). See "Old builds (2017)" below.

Read `docs/HOW-IT-WORKS.md` first for the reverse-engineering findings (config system, dev LAN mode, why the server
bootstrap is needed). `CHANGELOG.md` explains recent behavior.

## This repository: HMM Revive Cloud

This is **HMM Revive Cloud** (`AndreLuisOvidio/HmmRevive-Cloud`), a parallel version of TheRealRazbi's HMM Revive
(`TheRealRazbi/HmmRevive`, the original) that adds **cloud lobbies** (Host → In the cloud). Releases, updates and issues
of this version live here, never in Razbi's repository.

- **Remotes:** `fork` = AndreLuisOvidio/HmmRevive-Cloud (push here), `origin` = TheRealRazbi/HmmRevive (read only).
- **Branches:** `main` (tracks `fork/main`) is HMM Revive Cloud, start new work from it. `razbi-main` tracks Razbi's
  `main` (1.5). `cloud-lobbies-on-preview` is the head of the pull request offered to Razbi
  (TheRealRazbi/HmmRevive#6): do not commit Cloud-only changes there.
- **Name and updates:** players see "HMM Revive Cloud"; the launcher updates from this repository's releases
  (`UpdateReleasesUrl` in `tools/Launcher/Launcher.csproj`, by repository id, so a rename keeps working).
- **The cloud side** (Go orchestrator at `https://api.hmm.ovidio.dev`, container images, Azure Bicep, admin panel at
  `/admin`) is the separate private repository `AndreLuisOvidio/HmmRevive-server`, checked out next to this one as
  `../HmmRevive-server`. Its `.specs/STATE.md` (decisions AD-001 to AD-009, handoff) and `infra/runbook.md` describe how it
  is deployed and run.

## Legal hard rules (never break)

- **Never commit or ship Hoplon code or game files**: no decompiled code, no game DLLs/assets, no patched DLLs.
  `.gitignore` blocks `*.dll`, `*.exe`, `decompiled/`, `instance*/`, `build/` — don't work around it.
- Decompile with ILSpy (`ilspycmd`) into `decompiled/` (gitignored) for reference only. Don't paste large chunks of
  decompiled code into source files, comments, docs or commit messages.
- The patcher only ever works on a **copy** (`./instance`, hardlinked). The user's Steam install is never written to.
  Never write through a hardlink: delete the destination file, then copy (see `Patcher/Program.cs`).
- `tools/make_friend_kit.sh` runs `tools/check_public.py` (maintainer-only, not in the repo) as a leak check when present.

## Layout

| path | what |
|---|---|
| `mod/HmmRevive/` | The mod DLL (net35) loaded into the Steam build. Compiled against the game's own assemblies (`GameManaged`). |
| `mod/HmmRevive.Legacy/` | Mod for old builds (Unity 4.6.8). `#if Y2017SEP` = September 2017, `#if Y2016` reserved for 2016 (planned). `balance-2017.txt` ships with it. |
| `tools/Patcher/` | net8.0 + **Mono.Cecil**. Hardlinks the game into an instance, copies mod DLLs, IL-patches `Assembly-CSharp-firstpass.dll`. `Legacy.cs` = patch table for old builds. |
| `tools/Launcher/` | net48 console exe `HMM-Revive.exe`: tiny HTTP server (`Http.cs`) serving `web/` (embedded resources) on 127.0.0.1:9690, plus the lobby on TCP/UDP 9697. |
| `tools/Launcher/web/` | Launcher UI: `index.html`, `app.js`, `style.css`, `i18n.js` (English + pt-BR). |
| `tools/Launcher.Tests/` | xUnit (net48) tests of the launcher: lobby routes, password, host token, cloud client, progress, game download, updater source. `dotnet test tools/Launcher.Tests`. Not in the kit. |
| Cloud files in `tools/Launcher/` | `Cloud.cs` (orchestrator client), `CloudHosting.cs` (host side, regions), `CloudProgress.cs` (creation bar), `RemoteHost.cs`, `CloudHost.cs` / `CloudGuard.cs` / `CloudHeartbeat.cs` (`--cloud-host`: the lobby inside the cloud container), `GameDownload.cs` (the container's game download state), `Ping.cs`. |
| `tools/friend-kit/` | Scripts/READMEs packaged in the release zip (`setup/host/play/stop .bat/.ps1`, `README.txt`, `LEIA-ME.txt`). |
| `launcher/` | Old .bat/.ps1 host/join menus for running from the repo; `skins.txt` = car/skin catalog (`carId TAB car TAB number TAB skin`). |
| `tools/*.sh`, `tools/run_*.ps1` | Build, smoke test, run server/client, log summary. |

## How the pieces fit

1. **Patcher** (`Patcher <gameDir> <instanceDir> <modDir>`): detects the build by sha256 prefix of the original
   `Assembly-CSharp-firstpass.dll` (`Builds`), hardlinks all files (copies if on another drive), copies mod DLLs into
   `HMM_Data/Managed`, then rewrites the firstpass DLL. First output line is `build: <id>`.
2. **Injection**: `Infra.PreStart.Awake` gets a call to `HmmRevive.Entry.Init()` first. Every other hook is a static
   IL edit in the **patch table** in `tools/Patcher/Program.cs` (`Prologue`, `SkipIf`, and direct call redirects).
   **Harmony/MonoMod do not work on this Mono 2.0 runtime** — don't suggest them.
3. **Mod** (`mod/HmmRevive`):
   - `Entry.cs` — parses `--hmmrevive-*` command-line flags (server mode, headless, car/skin/team, score, bots,
     repair, chaos…), installs test crypto keys, and `Log` (writes `hmmrevive-server-<port>.log` /
     `hmmrevive-client-<pid>.log` next to `HMM.exe`).
   - `Hooks.cs` — static entry points called from patched game methods.
   - `ServerBootstrap.cs` — turns the client `0StarterScene` into Hoplon's unshipped `MainServer` scene (DI context,
     `NetworkClient`→`NetworkServer`, server GameStates, server installers).
   - `CarChoice.cs` (car/skin/team/spectator via the login name `name#car#skin#team`), `CarSwap.cs` (mid-match
     `/car`, `/skin`, chat commands), `Cooldowns.cs` (lobby ability cooldowns), `CarStats.cs` (lobby HP/move/damage %),
     `BallSpeed.cs`, `Emotes.cs`, `ImageDump.cs` (skin pictures for the launcher), `OutOfCombatRepair.cs`, `MatchEnd.cs`, `LocalPrefs.cs` (options saved locally
     instead of Swordfish), `Watchdog.cs`, plus test/diagnostic helpers (`HealScan.cs`, `TestShots.cs`, `UpgradeDump.cs`).
4. **Game config** is overridden on the command line, never by editing game files:
   `HMM.exe BeginConfig [Section] Key=Value ... EndConfig` (e.g. `[Debug] SkipSwordfish=true`, `[Server] IP= Port=`).
5. **Launcher**: `Program.cs` (local API `/api/*`), `Lobby.cs` (host-side lobby, `/lobby/*`, UDP discovery),
   `Session.cs` (client side of a lobby), `Game.cs` (paths, settings, catalog, starting `HMM.exe`, network
   addresses, firewall), `Draft.cs` (ban/pick draft), `Builds.cs` (supported game copies).

## Sync points (change one → change the other)

- Patch table in `tools/Patcher/Program.cs` ⇄ hook signatures in `mod/HmmRevive/Hooks.cs` (and the other mod classes
  it references by name: `CarChoice`, `CarSwap`, `LocalPrefs`). Hooks are looked up **by type and method name** as strings.
- Old builds: `tools/Patcher/Legacy.cs` ⇄ `mod/HmmRevive.Legacy/Hooks.cs`.
- Build hash table: `tools/Patcher/Legacy.cs` (`Builds.Known`) ⇄ `tools/Launcher/Builds.cs` (`Known`).
- Server/client command lines: `tools/run_server.ps1` / `run_client.ps1` ⇄ `tools/Launcher/Game.cs` ⇄ `Entry.cs` flag parsing
  ⇄ `tools/friend-kit/*.ps1`.
- Every user-facing launcher string goes in `web/i18n.js` in **both English and pt-BR**.
- User-facing docs come in pairs: `README.md` (Portuguese, the main one in HMM Revive Cloud) ⇄ `README.en.md`, `friend-kit/README.txt` ⇄ `friend-kit/LEIA-ME.txt`.
- `VERSION` (MAJOR.MINOR) is the single source of truth: baked into all three assemblies via the csproj files. The lobby
  **rejects players on a different version**, so protocol changes need a version bump and a `CHANGELOG.md` entry.

## Build & test

Windows only (Git Bash + PowerShell, .NET 8 SDK). The game must be installed; set `HMM_GAME_DIR` (scripts) /
`GameManaged` (mod csproj) if it isn't in the default Steam folder. Old builds: `HMM_2017_DIR`, `HMM_2017SEP_DIR`
(env or `tools/local.env`) = an **unmodified** copy of that build (never an HMM Revive instance: the patcher refuses it).

```bash
tools/build.sh                       # mod + patcher + launcher, refresh ./instance (kills HMM.exe running from it)
HMM_INSTANCE=instance-dev tools/build.sh   # second instance, leaves ./instance alone
tools/test_match.sh 1 150            # smoke test: headless server + 1 hidden/muted client, then prints logs
INSTANCE=instance-dev PORT=9797 tools/test_match.sh ...
tools/serverlog.sh                   # summary: mod log, game log, Unity exceptions
tools/make_friend_kit.sh [preview [N [variant]]]   # build/HMM-Revive-friend-kit-<version>[-preview.N[-variant]].zip
UPDATE_RELEASES_URL=https://api.github.com/repos/<owner>/<repo>/releases tools/make_friend_kit.sh   # another update source
dotnet test tools/Launcher.Tests     # launcher unit/integration tests (about 170)
```

`tools/local.env` (per PC, gitignored) sets `HMM_GAME_DIR` and `GameManaged` to an **unmodified** game folder; `build.sh`
reads it only after building the mod, so source it first (`. tools/local.env && tools/build.sh`). On this PC the game is
`E:/SteamLibrary/steamapps/common/Heavy Metal Machines`; build an extra instance on E: (`HMM_INSTANCE=E:/...`) so it is
hardlinked. Add `HMM_2017SEP_DIR` when building a kit so it keeps the September 2017 support.

```powershell
tools/run_server.ps1 -Players 2 -SameTeam -RedBots 4 -BluBots 2
tools/run_client.ps1 -Name Player -Ip 100.x.y.z -Car stingray
```

Verification = `dotnet test tools/Launcher.Tests` for launcher logic (write the test first for new lobby/draft/cloud
behavior), plus build succeeds + `test_match.sh` shows the server listening, clients connecting and no new errors in
`hmmrevive-*.log` / `server_unity_<port>.log` for anything that touches the mod or the game. The server takes ~20–30 s
to listen. The mod and the game itself still have no unit tests.

Logs (in the instance folder): `hmmrevive-server-<port>.log`, `hmmrevive-client-<pid>.log`,
`server_unity_<port>.log`, `logs/log-*.txt` (game log).

## Releasing a version (and the cloud)

`VERSION` gates lobbies (a 1.7 lobby refuses 1.6 players) **and** the cloud: the orchestrator only starts a version
listed in `HMM_VERSIONS`, and each version needs its own server image and game archive in every region. So:

- **Launcher-only change** (lobby UI, draft, host rules, page): bump `VERSION` when the lobby protocol changes, add the
  `CHANGELOG.md` entry, build the kit, release. For cloud lobbies of the new version the cloud steps below are still
  needed (the server image holds the launcher).
- **Mod or game change** (`mod/HmmRevive`, `tools/Patcher`): always a new `VERSION`. The cloud server runs the mod from
  the version's game archive, so a changed mod under an unchanged version would leave the cloud on the old mod (the
  push script skips a region whose archive has the same size).

Steps, after the code is committed on `main`:

1. `. tools/local.env && HMM_INSTANCE=E:/hmm-cloud<ver>-instance tools/build.sh` (mod, patcher, `build/launcher`).
2. In `../HmmRevive-server`: `python tools/make_overlay.py E:/hmm-cloud<ver>-instance game-base overlays/<ver>`.
3. `wsl bash tools/push_images.sh hmmrevive3056c83eee --version <ver> --game-storage brazilsouth=sthmmgbro25i4c,eastus=sthmmguso25i4c,spaincentral=sthmmgeso25i4c`
   (publishes `game/<ver>.tar` to the three regions, then `hmm-server:<ver>`; about 30 minutes).
4. Add the version to the orchestrator: `az containerapp update -g GrupoHmm -n hmm-orchestrator --set-env-vars
   HMM_VERSIONS=<old>,<ver>` and the same in `infra/apps.parameters.json`; create a test lobby of the new version.
5. Kit: `HMM_2017SEP_DIR=... tools/make_friend_kit.sh`, tag `v<ver>` on `main`, `gh release create v<ver> <zip>
   --repo AndreLuisOvidio/HmmRevive-Cloud --latest`. Kits update themselves from that release.
6. Later, drop old versions from `HMM_VERSIONS` once players moved.

Deploying to Azure, pushing and releasing are outward actions: ask the owner before each.

## Code conventions

- Mod code targets **net35** with `LangVersion latest`: no APIs newer than .NET 3.5 (no `Task`/`async`,
  no `string.Join(string, IEnumerable<string>)`, no `Tuple`, etc.). Launcher is **net48** (no NuGet; `System.Web.Extensions`
  for JSON). Patcher is net8.0.
- Server-only behavior must be guarded with `Entry.ServerMode`; clients must stay unchanged otherwise.
- Hooks must never throw into game code: wrap in try/catch and `Log.Error`.
- Use `Log.Info/Error` (mod) and `Program.Log` (launcher), not `Debug.Log`.
- Comments explain *why* and cite the game type/method touched; keep them concise and in English.
- `PathMap` in csproj files keeps the builder's user path out of DLLs — keep it.

## Gotchas

- Paths with accented/non-ASCII letters crash the game's native logger: the kit must live in an ASCII path.
- `-nographics` crashes in `GetDedicatedVideoMemory`; it's skipped via `SkipWhenHeadless`.
- The client also needs the mod: Lidgren busy-waits on a server public key that only Swordfish login set — the mod
  installs Hoplon's test crypto key pair.
- The in-match shop is gone from the shipped build; cars keep base stats.
- Ports: game UDP 9696, lobby TCP+UDP 9697, launcher UI 9690 (localhost only). No real authentication — anyone who
  reaches the ports can join.
- `.claude/settings.local.json` is per-PC and gitignored.

## Old builds (2017)

`mod/HmmRevive.Legacy` + `tools/Patcher/Legacy.cs`. Same idea as the Steam mod, very different game code.

**Building and testing one by hand** (`tools/build.sh` also does it when `HMM_2017SEP_DIR` is set):

```bash
dotnet build mod/HmmRevive.Legacy -c Release -p:GameManaged="<2017sep copy>/HMM_Data/Managed" -p:DefineConstants=Y2017SEP
mkdir -p build/mod/2017sep && cp mod/HmmRevive.Legacy/bin/Release/net35/HmmReviveLegacy.dll build/mod/2017sep/
build/patcher/Patcher.exe "<2017sep copy>" "<instance dir>" build/mod   # instance on the copy's drive (hardlinks)
```

`tools/run_*.ps1` and `test_match.sh` are Steam-only. Start the old build like `Game.StartLegacyServer` /
`StartLegacyClient` do: server `HMM.exe -batchmode -nographics --hmmrevive-server ... BeginConfig [Debug]
SkipSwordfish=true IsDebug=true [Game] PlayerCount=1 RedTeamBotsCount=.. [Server] Port=9797 EndConfig`; client
`HMM.exe --hmmrevive-connect ... BeginConfig [Debug] SkipSwordfish=true PlayerName=X AutoTest=true [Server] IP=127.0.0.1
Port=9797 [Game] SkipTutorial=true ... EndConfig`. Use a port other than 9696 so a running kit isn't disturbed.

- `AutoTest=true` makes a test client pick a random car by itself. `--hmmrevive-hide-window` + `--hmmrevive-shots=N`
  gives pictures, but the game covers them with popups: "only one client per machine" if another HMM client runs, and
  a resolution warning below 1280x768 (use `-screen-height 768`). An idle client also gets the autopilot overlay. So
  controls (keys held, aim) can only be checked by a person playing; verify server-side logic through `Log.Info`.
- Bots: each team picks distinct cars; if a bot finds no available car, the match hangs in `ServerLoading`.
- `NullReferenceException`s in `CarHitFeedback.Start` / `CarSuspensionGroup.OnEnable` in the Unity log are the game's
  own; compare against a vanilla run before blaming the mod.

**Reading the 2017 code.** Without ILSpy, a tiny Mono.Cecil console (list types/fields/methods, dump a method's IL,
find callers) is enough. Game data: the 2017 bundles (`Content/Resources/*.hmm`, `UnityRaw` 4.6.8) keep type trees, so
UnityPy reads them; the car definitions (`CharacterInfo`, `GadgetInfo`) live in the player's `.assets` **without** type
trees, so read them at runtime instead (e.g. reflection over `Resources.FindObjectsOfTypeAll(typeof(CharacterInfo))`
from the mod, logged with `Log.Info`).

**Facts about the September 2017 build** (found by reading its code; check before relying on them for `2017`):

- Cars: ids 1-20 (no 12, 15-18); MotorHome (9) and LadyMuerte (11) exist but have `IsAnAvailableCharacter = false`.
- Keys, slots and `PlayerController.InputMap`: Q `CustomGadget0` (`G0`), W `CustomGadget1` (`G1`), R `CustomGadget2`
  (`G2`), E `BoostGadget` (`GBoost`), Space `BombGadget` (`Bomb`, takes the ball). `TrailGadget` (nitro) isn't fed by
  input. Default driving (FollowMouse): the car turns towards the mouse, mouse buttons = forward/back.
- Client input: `PlayerController.Feed` fills `Inputs` (`Dir` = steering, the mouse direction in FollowMouse;
  `MousePos`, `MouseDir`; `Gx.Pressed/Changed`), then `UpdateInput` applies it (`CarInput.Input`, then each gadget's
  `Pressed`, `Dir = MouseDir`, `Target = MousePos`) and the map is sent to the server. Rewriting `Inputs` at the end of
  `Feed` changes both what the client predicts and what the server gets. When changing a
  `Pressed`, set `Changed` too and keep the press for a few frames (~0.15 s) so the server sees it.
- Where a shot goes: `FXInfo.ShotPosAndDir.UseTarget`: false = the dummy's forward (`DummyForward`), true = towards
  the gadget's `Target`. `MortarWarning` lands at `Target`, clamped to `Range`.
- `GadgetBehaviour.ServerListenToGadgetUse` (event) fires when a gadget is used on the server.
- `CombatMovement.Push(dir, false, 0f, true)` adds `dir` straight to the car's velocity (units/s; top speed ~57).
- Copying a `GadgetInfo` with `Object.Instantiate` runs its `OnEnable`, which overwrites the static `GadgetInfo.Gadgets`
  (by `GadgetId`) and `GadgetInfo.EffectMap` (by `EffectId`, used to rebuild effect events from the network) with the
  copy's entries: put the original's back. The copy's `FXInfo`s are separate objects.
- `CharacterService` caches its car lists lazily (`_charactersByTypeId`, `_availableCharactersByInfoId`, ...): null
  them after changing availability. The server accepts a pick only if `HasCharacter(player, id)` (owned cars);
  otherwise `SelectCharacter` stores -1 and `PickCharacter` gives a random car.
- Car look: `PlayerCarFactory.CreateCar` instantiates the skin prefab (`CarGenerator`, `CarSkin`, an `FBX_*` child with
  the meshes); `CarGenerator.carComponentHub.Player` says whose it is. Car shader `HMM/Car Paint Metal` (`_MainTex`,
  `_NormalMap`, same property names as the Steam `HMM/Car_INGAME_Shader`); MotorHome uses `HMM/Custom Paint Shader`.
- Patcher helper in `Legacy.cs`: `Prologue`. Code at the end of a method must run before every `ret`; turn each `ret`
  into the first new instruction (and append a new `ret`) so branches that jumped to it still run the new code.
