# Research: relocking ascensions in Slay the Spire 2

Investigation done before any mod code was written. Goal of the mod: **re-lock** ascension
levels that were unlocked earlier (e.g. via cheat/unlock mods), per character and per mode,
on the beta branch.

All findings come from decompiling the game's managed assembly (`sts2.dll`) with
`ilspycmd` and from real save data / logs on the Linux workstation.

| Item | Value |
|---|---|
| Game build studied | `v0.113.1` (commit `d09d48e5`), Steam branch `public-beta`, buildid `26660232` |
| Comparison build | `v0.107.1` (public branch, Windows install) |
| Decompiler | `ilspycmd` 11.1 (`~/.dotnet/tools/ilspycmd`, not on PATH) |
| Reference mod | "Unlock the Spire" `v1.0.3` (Workshop `3748608067`, author lriuui0x0) |
| Save-path mod | "SavesMerger" `1.0.0` (Workshop `3747532120`) |

## Contents

- [Working setup](#working-setup)
- [Ascension data model](#ascension-data-model)
- [How the game advances ascension](#how-the-game-advances-ascension)
- [Random character](#random-character)
- [Mod loading](#mod-loading)
- [Save tree: vanilla vs modded](#save-tree-vanilla-vs-modded)
- [SavesMerger](#savesmerger)
- [Steam Cloud](#steam-cloud)
- [Reference mod: Unlock the Spire](#reference-mod-unlock-the-spire)
- [Profiles and switching](#profiles-and-switching)
- [Run history](#run-history)
- [History replay (recompute route)](#history-replay-recompute-route)
- [Design implications](#design-implications)
- [Startup freeze (Steam overlay)](#startup-freeze-steam-overlay)
- [Debug procedure: profile fork](#debug-procedure-profile-fork)
- [Open items](#open-items)

## Working setup

- The game is installed in the `ethan` user's Steam library
  (`~/.steam/debian-installation/steamapps/common/Slay the Spire 2`), which the `dev` user
  cannot read. Saves live in `~/.local/share/SlayTheSpire2/steam/<steamid>/`.
- **Running the game is not needed to build the mod.** Decompiling and compiling only need
  the managed DLLs. `ethan` copies them (and saves/logs, read-only) into shared scratch with
  helper scripts; `dev` decompiles and builds there. Only testing needs a game launch.
- The game is Godot 4 + .NET (C#). The managed game code is `data_sts2_<platform>/sts2.dll`;
  Harmony (`0Harmony.dll`) ships with the game.

## Ascension data model

All ascension progress is in the per-profile `progress.save` (plain JSON via
`System.Text.Json`; no checksum, signing or encryption). In code: `ProgressState`
(runtime) / `SerializableProgress` (on disk) / `CharacterStats` (per character).

| Scope | Fields (JSON) | Cap | Notes |
|---|---|---|---|
| Standard singleplayer | `character_stats[].max_ascension` / `preferred_ascension` | 10 | per character |
| Multiplayer | `max_multiplayer_ascension` / `preferred_multiplayer_ascension` | 10 | shared by all characters |
| Blitz (SP and MP) | `max_blitz_ascension` / `preferred_blitz_ascension` | 5 | shared by all characters; beta-only |
| Dread | none | n/a | always runs at A10; only win streaks are stored (per character + `RotatingCharacter`) |
| Daily | none | n/a | never touches ascension |

- Caps are `AscensionManager.maxAscensionAllowed = 10` and `maxAscensionAllowedBlitz = 5`.
  On load, `ProgressState` clamps out-of-range values.
- Game modes: `enum GameMode { None, Standard, Daily, Dread, Blitz }`. Blitz and Dread do not
  exist in public v0.107.1.
- `character_stats` also holds pseudo-characters (`CHARACTER.RANDOM_CHARACTER`,
  `RotatingCharacter`) and **modded characters** (e.g. `CHARACTER.FOOL`,
  `CHARACTER.HELLOSPIRE-*`). Relock targets must be enumerated from the save, not hard-coded.
- The ascension selector also requires the character's ascension **epoch** to be revealed;
  without it, the effective max is 0 regardless of the stored value.

## How the game advances ascension

`ProgressSaveManager.UpdateWithRunData(run, victory)` runs after every run. On a victory that
is not Daily and not a custom game:

- `GameMode.Blitz` -> `IncrementBlitzAscension` (checked first, regardless of player count).
- else singleplayer (`Players.Count == 1`) -> `IncrementSingleplayerAscension` on that
  character's stats.
- else multiplayer -> `IncrementMultiplayerAscension`.

Each increment is the same rule: **if `run.Ascension == current max` and max < cap, then
max += 1 and preferred = max.** Winning below your max never advances it.

Dread: a singleplayer Dread win does reach `IncrementSingleplayerAscension`, but Dread runs are
recorded at A10, so it can only match when the max is already 10 (the cap). Dread therefore
never changes ascension.

## Random character

- `StartRunLobby` resolves Random to a real character **before** the run starts, then clamps
  the run ascension to that character's own max.
- History records the real character and the actual ascension; there is no "was random"
  marker. On victory the real character advances by the normal rule.
- `RANDOM_CHARACTER`'s own stats only matter for `preferred_ascension`; the lobby shows the
  max across all characters as Random's max. After a relock, Random's preferred value must be
  clamped to the new highest per-character max.

## Mod loading

The game has a built-in loader, `MegaCrit.Sts2.Core.Modding.ModManager`:

- Sources: `<game>/mods/`, `<game>/mods_STEAMTEST/`, and subscribed Steam Workshop items.
- A mod is `<id>.json` (manifest) + `<id>.dll` and/or `<id>.pck`.
- If a type has `[ModInitializer("Method")]`, that static method is called; otherwise the
  loader calls `Harmony.PatchAll` on the assembly.
- Enabled/disabled state lives in `settings.save` (`mod_settings.mod_list`). Mods not present
  in the list default to enabled.
- `ModManager.IsRunningModded()` is true if any mod is **Loaded or Failed**. A launch where
  every mod failed to load still counts as modded (observed in logs).

## Save tree: vanilla vs modded

`UserDataPathProvider`:

```
user://steam/<steamid>/            <- vanilla account dir
user://steam/<steamid>/modded/     <- used when IsRunningModded
  profile.save                     <- which profile slot is active
  settings.save                    (account-level, vanilla dir)
  profile{1..3}/saves/
    progress.save
    prefs.save
    current_run.save, current_run_mp.save
    history/<start_time>.run
```

- `UserDataPathProvider.IsRunningModded` is set once at startup, right after mods load
  (`OneTimeInitialization`), from `ModManager.IsRunningModded()`.
- **First-time copy** (`ModManager.CopyUnmoddedSaveFilesIfNeeded`) copies vanilla saves to
  `modded/` only when all of these hold: a mod loaded, the settings mod list was empty before
  this launch (first modded launch ever), and `modded/` does not exist. It is one-way and
  one-time. On this machine the mod list is already populated, so it will never run again.
- Consequence observed: launching modded without SavesMerger showed **empty** profiles,
  because nothing was copied. Vanilla progress was untouched.

## SavesMerger

Four Harmony patches plus an initializer:

- `UserDataPathProvider.get_IsRunningModded` -> always `false`.
- `UserDataPathProvider.set_IsRunningModded` -> forces the value to `false`.
- `UserDataPathProvider.GetProfileDir` -> returns `profile{id}`.
- `Initialize()` runs `PatchAll` then sets `IsRunningModded = false`.

It copies, merges and deletes nothing; it only keeps the game on the vanilla paths.

**Broken on beta v0.113.1.** The log shows
`HarmonyException: Ambiguous match for ... GetProfileDir`. Beta added an overload
`GetProfileDir(int, bool? forceModState)`, and the patch names only the method. `PatchAll`
throws, and the initializer's final line never runs. It still works because Harmony had
already applied the getter/setter patches, which are sufficient on their own (the profile
path derives from the patched getter). Confirmed in the log:
`Profile-scoped data path initialized: user://steam/<id>/profile1`. This depends on patch
order; the upstream fix is to specify argument types in the `GetProfileDir` patch.

Note: the first-time copy names `modded/` explicitly through `forceModState: true` and checks
the unpatched `ModManager.IsRunningModded()`, so SavesMerger does not prevent it. It would just
create an unused `modded/` copy.

## Steam Cloud

- `CloudSaveStore` syncs every save file except `.backup` and `.pre-repair`, including the
  `modded/` tree and `history/`.
- The cloud keeps at most 100 files / 5 MB per history directory, dropping the oldest from the
  **cloud** only; local copies remain.
- Observed: a modded launch pulled `modded/profile1` from the cloud (dated 2026-07-26, from
  another device). A vanilla launch pulled 3 runs played on another device that day.
- So "this machine's history" means cloud-synced history from all devices (within quota) plus
  older local-only runs.
- Deleting a profile in-game removes it from the cloud too (`SaveManager.DeleteProfile`).
  Deleting files on disk can be undone by the next cloud sync.
- **Startup sync deletes local-only files.** `CloudSaveStore.SyncCloudToLocal` runs on every
  launch (whenever Steam is initialized, regardless of the cloud toggle) and deletes any local
  save file that does not exist in the cloud ("Deleting ... because it does not exist on
  remote"). Files copied into a profile slot with the game closed are therefore wiped on the next
  launch. Observed on 2026-11-17: a forked `profile3` (progress + 137 history files) was deleted
  this way, and switching to the slot created a fresh default `progress.save`.
- **Quit rewrites the active profile.** `NGame.Quit` always calls `SaveProgressFile()` (and
  prefs/profile saves) for the active profile, so its `progress.save` changes on disk even when
  nothing in it changed.
- Files the game writes during a session (via `SaveProgressFile` etc.) are uploaded immediately
  through the remote store; files only copied onto disk are not.

## Reference mod: Unlock the Spire

Driven by a click; nothing happens at load.

- `NMainMenu._Ready` prefix adds an `UnlockButton` next to `%ChangeProfileButton`;
  `NMainMenu.OnSubmenuStackChanged` hides it while submenus are open.
- Pressing it calls `Unlocker.UnlockAll()` on `SaveManager.Instance.Progress`:
  - marks all cards/relics/potions/events/acts as seen,
  - adds placeholder `FightStats` for every monster/encounter,
  - reveals all epochs (`ObtainEpochOverride(id, EpochState.Revealed)`),
  - clears `PendingCharacterUnlock`,
  - sets every character's stats, **including `RANDOM_CHARACTER`**, to max/preferred 10,
  - sets multiplayer to 10/10; Blitz to 5/5 and `TotalUnlocks` to 18, both via reflection
    (`AccessTools.PropertySetter`) so it still works on builds without those fields,
  - disables tutorials (`EnableFtues = false` + marks a fixed FTUE key list complete),
  - `SaveManager.Instance.SaveProgressFile()`.
- Runtime patches gated on `Unlocker.IsUnlocked`. That flag is set when a profile loads
  (`ProgressSaveManager.LoadProgress` postfix) if everything has been discovered:
  - transpiles `NumberOfRuns` checks in `NMainMenu.SingleplayerButtonPressed` and
    `NMultiplayerSubmenu.OnHostPressed` to bypass the "play one run first" gate,
  - forces the Random button, Compendium (`SaveManager.IsCompendiumAvailable`) and Bestiary
    (`NBestiary.CanBeShown` + entry visibility transpiler) available.
- Takeaways for this mod: same entry point style (main-menu, user-triggered), same API
  (`SaveManager.Instance.Progress` + `SaveProgressFile()`), and use reflection for
  fields that might not exist in a given build.

## Profiles and switching

- Three slots (`profile1..3`); the active slot is in `profile.save`.
- `SaveManager.SwitchProfileId(int)` switches; `SaveManager.ProfileIdChanged`
  (`event Action<int>`) fires afterwards. A UI can listen to refresh a preview without writing.
- The progress save path is `ProgressSaveManager.GetProgressPathForProfile(id, forceModState)`.
  History is under `RunHistorySaveManager.GetHistoryPath(id, forceModState)`.

## Run history

- One JSON file per run: `profileN/saves/history/<start_time>.run` (`RunHistory`, schema v11
  on beta). The game only reads `*.run`; `*.run.backup` files are crash-recovery copies and
  must be ignored.
- Relevant fields: `game_mode`, `is_custom_game` (omitted when false in older files),
  `win`, `was_abandoned`, `ascension`, `build_id`, `start_time`,
  `players[].character`, `players[].id`.
- API: `RunHistorySaveManager.LoadAllRunHistoryNames()` / `LoadHistory(fileName)` (goes
  through the migration manager).

## History replay (recompute route)

Rules are evaluated against the **current** mode + cap logic, not each build's historical
rules (accepted: too much to maintain).

### Naive replay fails

Replaying from 0 with only "win at max -> +1" gave 0 for every scope on the real profile. Local
history starts 2026-04-26, already at A5-A6; the early climb happened on another device or
fell outside the cloud quota.

### Evidence replay works

In chronological order, for each run that is not Daily, not custom and not Dread, using its
scope (Blitz / singleplayer character / multiplayer):

1. **Played at A proves A was unlocked**: `max = max(max, A)`. The lobby only allows unlocked
   ascensions, and multiplayer runs are capped at the party's lowest unlock.
2. **Win at max advances**: if `win && A == max && max < cap`, then `max += 1`.

Validated on the real `profile1` (134 runs, 10 builds v0.104.0 -> v0.113.1, 2026-04-26 ->
2026-11-15). The result **exactly matched** both the saved values and what the vanilla game
showed in-game:

| Scope | Saved | Replay | Last evidence |
|---|---|---|---|
| Ironclad | 10 | 10 | won A9, 2026-06-16 (v0.107.0) |
| Silent | 10 | 10 | won A9, 2026-06-17 (v0.107.0) |
| Regent | 10 | 10 | won A9, 2026-06-20 (v0.107.1) |
| Necrobinder | 10 | 10 | won A9, 2026-06-21 (v0.107.1) |
| Defect | 9 | 9 | won A8, 2026-06-04 (v0.106.1) |
| Multiplayer | 10 | 10 | played A10, 2026-06-12 |
| Blitz | 0 | 0 | no runs |
| FOOL / HELLOSPIRE-* / Random | 0 | 0 | FOOL's wins were all multiplayer |

`profile1` was never touched by Unlock the Spire (Random max 0, Blitz 0), so it is a
known-good baseline.

### Properties and limits

- The result is a **lower bound**: the most the local history can prove. Missing runs (other
  devices beyond the cloud quota, deleted files) can only make it lower.
- Therefore it may only ever **lower** a stored max, never raise it.
- A scope with **no evidence** should report "unknown", not 0, so progress from other
  devices is not wiped.
- Wins produced by debug/console commands, if recorded in history, would count. Unlock the
  Spire writes no history.

## Design implications

- **No action at load.** Scanning and relocking happen only when the user asks, so they can
  switch profiles in-game first. Unlock the Spire works the same way.
- **No save-tree detection.** Read and change `SaveManager.Instance.Progress`, persist with
  `SaveProgressFile()`, and load history through the game's run-history manager. These already
  point at the save the game has loaded: vanilla `profileN` with SavesMerger, `modded/profileN`
  without it, or whatever a half-working SavesMerger produced. "Relock modded only" therefore
  means "relock the save currently loaded", which is the one shown in the menus.
- Use paths only to show the target (e.g. "will write: profile1 (shared vanilla saves)"),
  taken from the game's own path functions. Never use them to decide where to write.
- **Layering**, since the UI/input route is undecided:
  - *Core* (pure C#, no game/Godot types, unit-tested): scopes (character / multiplayer /
    Blitz; Dread and Daily rejected), caps, relock-request validation (only lowers, within
    0..cap), preferred-ascension clamping (including Random), and the evidence replay.
    Real `.run` files serve as test fixtures.
  - *Game adapter*: maps `ProgressState` / `CharacterStats` / run history to and from the core
    model; Blitz fields accessed via reflection; saves.
  - *Front-ends* (later, thin): dev-console command, main-menu button/panel, config.
    "Recompute from history" is a convenience layer over the manual relock.
- Preview before writing: show old -> new per scope; apply only on confirmation.

## Startup freeze (Steam overlay)

Observed 2026-11-17 on v0.113.1 (native Linux build, KDE Wayland via XWayland, NVIDIA): an
intermittent black screen at startup that never recovers. The log stops at a different step each
time: mid cloud sync, after `controller_atlas` loads, after `Global stats received`, or after
`Preloading 'IntroLogo' assets...`.

**Cause: a Steam overlay deadlock, not BaseLib.** On a hung launch the game's main thread sits in
`recvmsg` on a Unix socket whose peer is `gameoverlayui`
(`/tmp/steam_chrome_overlay_uid1000_spid<pid>`), with both queues empty and zero CPU over
3 minutes. The overlay hook in the game blocks whichever frame it lands on, which is why the stop
point varies. This is a known open Steam client bug,
[steam-for-linux#13766](https://github.com/ValveSoftware/steam-for-linux/issues/13766) (we added
these findings there on 2026-11-18); it reproduces without mods.

- **Workaround:** disable the Steam overlay for the game (Properties > General). Five launches
  in a row with BaseLib then reached the menu.
- **BaseLib's role:** every hung launch had BaseLib (Workshop `3737335127`, plus
  BaseLibCompatibilityHelper) loaded, and BaseLib-only launches hung often. It adds about 5 s to
  startup (13.6 s to the main menu against about 8 s without it), which likely widens the window
  for the race. It is not the cause, and it is safe to use with the overlay off.
- Startup reaches the menu through a chain of awaits in `NGame.GameStartup`, each resumed on a
  later main-thread frame, so a blocked frame looks like "startup stopped at step X".

### Debugging a native hang

gdb is no use here: the game runs inside Steam's pressure-vessel container, so gdb cannot read
the binary and every frame is `??`. What works, run as the game's user while it is hung:

1. Find the game pid: of the processes matching `spire`, the one with the most threads (the
   first match is `steam-runtime-launcher`).
2. Take snapshots of `/proc/<pid>/task/*/{comm,stat,wchan}` about 30 s apart, plus the log.
   Compare with a healthy run on the main menu: there the main thread sleeps in
   `hrtimer_nanosleep` between frames and gains about 44 CPU ticks per 30 s.
   - `futex_do_wait` with flat CPU: lock deadlock. Rising CPU with a frozen log: spin.
     A socket read with flat CPU: waiting on another process.
3. For a socket wait: `/proc/<pid>/task/<tid>/syscall` gives the syscall number and fd,
   `ls -l /proc/<pid>/fd` maps the fd to `socket:[inode]`, and `ss -xp` shows the peer process.

The cloud-sync read path is fragile on its own: the sync reads up to 8 files concurrently via
`GodotFileIo.ReadFileAsync`, whose `FileAccessStream` has only a synchronous `Read`, so .NET
runs the reads on thread-pool threads calling Godot `FileAccess`. A local test tool (kept on a
local branch, not in this repo's history) patched that read to be synchronous and logged Debug
output for the sync. The "stuck" history-file read it found was most likely the overlay blocking
the frame, not the read itself.

## Debug procedure: profile fork

To test against realistic history without risking the real profile. Because of the startup
sync (see [Steam Cloud](#steam-cloud)), the copy must be made **while the game is running**:

1. Launch with mods and wait for the main menu, staying on profile 1.
2. With the game running, copy `profile1/saves/{progress.save,history/}` into `profile3/saves/`
   of the save tree in use (vanilla with SavesMerger).
3. Switch to profile 3 in-game (the game reads the copied save at the switch), check it shows
   profile 1's progress, then press Unlock the Spire's button.
4. Run the relock / recompute on profile 3 (`relock recompute`, `relock confirm`).
5. Expected result: the golden table (characters and multiplayer as in profile 1; scopes with no
   evidence keep their unlocked value).
6. Clean up by deleting profile 3 **in-game**, so it is removed from Steam Cloud too. The copied
   history files are never uploaded, so the next startup sync deletes any that remain.

Caution: with SavesMerger on, Unlock the Spire's button acts on the shared vanilla profile.
Only press it on the fork slot. Take a full copy of `steam/<steamid>/` first.

## Open items

- `modded/profile1` (from the cloud, 2026-07-26, one run) now exists locally. Decide whether to
  keep it or delete it in-game.
- SavesMerger's `GetProfileDir` patch is broken on beta. It's harmless today but depends on
  patch order; consider reporting it upstream or shipping a fixed fork.
- Unlock the Spire is not in the `settings.save` mod list, so it will likely load as
  enabled-by-default on the next modded launch.
