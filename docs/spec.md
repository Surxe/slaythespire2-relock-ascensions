# Spec: Relock Ascensions (STS2 mod)

Status: draft. Based on [research.md](research.md). The UI and input route are **not decided**;
this spec defines the logic and the seams that let a UI be added later without touching it.

## 1. Goal

Let the player lower ascension levels that were unlocked earlier by other means (cheat or
unlock mods, or progress from another device), per scope, on the current beta branch.

Two ways to set a value:

- **Manual relock:** the player chooses a new max for one scope.
- **Recompute from history:** the mod proposes the max the local run history can prove,
  for every scope at once.

Both only ever **lower** a max. Neither raises one, and neither touches anything at load.

## 2. Non-goals

- Raising ascension, or granting unlocks of any other kind (that is Unlock the Spire's job).
- Changing epochs, unlocks, tutorials, achievements or cards.
- Editing vanilla or modded save folders by path. All access goes through the game's save
  manager (see section 6).
- Dread and Daily, which have no ascension to relock (see section 3).
- Reproducing historical balance or unlock rules. Every replay uses the current rules.

## 3. Scopes

A **scope** is one independently capped ascension track. Scopes are enumerated from the
loaded save, not from a hard-coded list, so modded characters are included automatically.

| Kind | Identity | Source in save | Cap |
|---|---|---|---|
| `Character` | character id, e.g. `CHARACTER.IRONCLAD` | `character_stats[]` (`max_ascension`, `preferred_ascension`) | 10 |
| `Multiplayer` | fixed | `max_multiplayer_ascension`, `preferred_multiplayer_ascension` | 10 |
| `Blitz` | fixed | `max_blitz_ascension`, `preferred_blitz_ascension` | 5 |

Excluded:

- `CHARACTER.RANDOM_CHARACTER` and the rotating-character pseudo-entry. They have no max of
  their own. They are handled only by **clamping preferred** (section 5.3).
- Dread (no ascension field; always A10).
- Daily (never changes ascension).

Caps are read from the game's constants (`AscensionManager.maxAscensionAllowed`,
`maxAscensionAllowedBlitz`) in the adapter. The core takes caps as input, so a future cap
change needs no core change.

## 4. Core logic (pure, no game types)

The core is plain C# with no references to the game or Godot. It is unit-tested.

### 4.1 Model

```
enum ScopeKind { Character, Multiplayer, Blitz }
record Scope(ScopeKind Kind, string? CharacterId)     // CharacterId null unless Character
record ScopeState(Scope Scope, int Max, int Preferred, int Cap)
enum RunMode { Standard, Daily, Dread, Blitz, Unknown }
record RunRecord(long StartTime, RunMode Mode, bool IsCustom, bool Win, int Ascension,
                 IReadOnlyList<string> Characters)
```

`RunRecord` is a plain copy of the history file's fields, built by the adapter. The core
classifies each run (it is tested; the adapter is not):

- Daily, Dread, custom and unknown-mode runs are skipped.
- Blitz runs (any player count) belong to the `Blitz` scope.
- Other runs with one player belong to that player's `Character` scope; with more than one
  player, to `Multiplayer`.
- Abandoned runs are **not** skipped: starting a run at `A` still proves `A` was unlocked.
  They can never be wins, so they never advance a scope. The validated replay included them.

### 4.2 Validation rules

- A new max must satisfy `0 <= newMax <= Max` (current). Requests above the current max are
  rejected, not clamped. Raising is out of scope.
- A new max may be 0 (full relock).
- Blitz and Multiplayer use the same rules with their own caps.

### 4.3 Manual relock

`Plan(states, requests) -> Plan`, where `Plan` is:

```
record Change(Scope Scope, int OldMax, int NewMax, int OldPreferred, int NewPreferred)
record Plan(IReadOnlyList<Change> Changes, IReadOnlyList<Rejection> Rejections)
```

Each change sets `NewMax`, then sets `NewPreferred = min(OldPreferred, NewMax)`.

### 4.4 Recompute from history

Evidence replay, as validated on the real profile (research section "History replay"):

1. Process eligible runs in `start_time` order, per scope.
2. A run at ascension `A` proves `A` unlocked: `evidence = max(evidence, A)`.
3. A win with `A == evidence` (and `evidence < cap`) advances: `evidence += 1`.

This gives a **lower bound** per scope, or **no evidence** when the scope has no eligible runs.

Plan rule, per scope:

- **No evidence:** no change. Never report 0 for a scope with no data.
- **Evidence >= current max:** no change.
- **Evidence < current max:** propose `NewMax = evidence`, with preferred clamped as in 4.3.

Replay is a pure function of `(runs, caps)`, so the same input always produces the same plan.

### 4.5 Preferred clamping (applies to every plan)

- Each scope's preferred is at most its new max.
- `CHARACTER.RANDOM_CHARACTER`'s preferred is set to `min(its preferred, highest Character max)`.
  Its own max is not touched.

## 5. Game adapter

The only layer that touches the game. Everything here is thin mapping plus saving.

### 5.1 Reading

- Current state: `SaveManager.Instance.Progress` (`ProgressState`). Scopes come from
  `CharacterStats` and the multiplayer/Blitz properties.
- Blitz is read and written through reflection (`AccessTools.PropertyGetter/Setter` on
  `ProgressState`), so the mod still loads on builds without Blitz. If the property is absent,
  the Blitz scope is omitted from the plan.
- History: `RunHistorySaveManager` for the active profile. Each file goes through its
  `LoadHistory(name)` (migrations included), then is mapped to `RunRecord`. Files ending in
  `.backup` are never read.

### 5.2 Writing

1. Re-read the current state. Discard any plan whose `OldMax` no longer matches (the save
   changed since the preview was built).
2. Apply the changes to `ProgressState`.
3. Call `SaveManager.Instance.SaveProgressFile()`.

The adapter never builds file paths and never chooses between vanilla and modded. Whatever
the game has loaded is what gets written.

### 5.3 Compatibility

- Target: beta `v0.113.1`. The adapter checks `ReleaseInfo` and logs a warning on other
  versions; it refuses to write when a required member is missing.
- Harmony patches are not used. The mod does not patch the game. (Unlock the Spire patches
  heavily; that is not needed here.)

## 6. Safety

- **Nothing happens at load.** The mod never scans, replays or writes on startup, so the
  player can switch profiles first.
- **Explicit invocation only.** Every write follows an explicit player action.
- **Preview before write.** The player sees `scope: old -> new` for every change, and the
  write happens only on confirmation (the front-end provides confirmation; the core only
  produces the plan).
- **Shows its target.** Before the preview, the mod displays the active profile slot and its
  save-tree label as reported by the game (for example `profile1 (shared vanilla saves)` when
  SavesMerger is on). This is display only.
- **No write during an active run.** Open decision (section 10).

## 7. Layering and modules

```
Core (pure C#, tested)           Adapter (game)               Front-ends (later, thin)
  Scopes, Model                    ReadState(Progress)          Dev-console command
  Validation                       ReadHistory(runs)            Main-menu button/panel
  Manual relock -> Plan            ApplyPlan(Plan) + save       Config file
  Replay -> evidence -> Plan       Caps, release checks         ...each just builds requests
  Preferred clamp                  Reflection for Blitz         and shows a Plan
```

Front-ends may only: build `RelockRequest`s or ask for a recompute, show a `Plan`, and call
`ApplyPlan` after confirmation. They never edit state directly.

## 8. Testing

- **Core unit tests:** validation, manual relock, replay, preferred clamping, and the
  no-evidence rule. Test cases are small synthetic run sets; the spec does not commit the
  player's real saves.
- **Replay golden test:** the validated `profile1` result from research (Ironclad, Silent,
  Regent, Necrobinder = 10; Defect = 9; Multiplayer = 10; Blitz and the rest = 0). This
  needs real run data. Decide where that fixture lives (section 10).
- **Adapter tests:** not automated. They run in the game.
- **In-game acceptance:** section 9, on a fork profile only.

## 9. Acceptance criteria

1. With no mod action, the save is byte-identical before and after loading and switching
   profiles.
2. A manual relock on a fork profile changes only the requested scope, plus preferred
   clamping, and nothing else in `progress.save`.
3. A recompute on the fork profile, built from a copy of `profile1` history, reproduces the
   golden table in section 8.
4. A scope with no eligible runs is left unchanged.
5. A recompute never raises any value.
6. Cancelling the preview writes nothing.
7. The mod does not crash, and writes nothing, when a required member is missing.
8. The mod works whether SavesMerger is enabled or not, and writes to the same profile the
   game shows.

## 10. Open decisions

1. **Writes during an active run.** Does the game hold `progress.save` in memory during a run,
   so that a relock could be overwritten at run end or on the next save? Needs a test. The
   simplest rule is to block writes while a run is in progress.
2. **Recompute overshoots on incomplete history.** The replay is a lower bound, so missing runs
   (other devices beyond the cloud quota) make it relock too far. Options: (a) keep the
   preview as the only guard; (b) add a "recompute only when history covers N runs" check;
   (c) lower to evidence only for scopes where the player confirms each one.
3. **Backups before write.** Write our own timestamped copy of `progress.save` first, or rely
   on the game's `.backup` files? The game's copies are overwritten on each save, so they do
   not work as a long-term undo.
4. **Fixture location.** Real saves contain gameplay data and the player's run history. Keep
   them out of the repo and use synthetic fixtures, or keep a reduced golden fixture?
5. **Random preferred.** Clamp to the highest Character max (current proposal), or leave
   preferred alone and let the game clamp it at lobby time?
6. **Multiplayer cap of 10 across all characters.** Confirm the expected relock behavior when
   multiplayer is lowered below a character's Standard max.
7. **UI route.** Dev console, main-menu button, config file, or a combination. Deferred; the
   core/adapter split means this does not block the logic.

## 11. Build and deploy (proposed)

- Solution with the core as a class library (`net9.0` or `net10.0`, whichever the game
  runtime uses), its unit tests, and the mod project that references the game DLLs
  (`sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`) from a local copy. Those DLLs are not checked
  in; the build reads them from a path set in a local props file.
- Output: `RelockAscensions.dll` plus a manifest (`RelockAscensions.json`) in the
  `mods/` folder, following the loader's layout (section "Mod loading" in research).
- Deploy by copying the built files into the game folder. Mod-folder placement must be
  done by the player or by a small install script, never by editing the game install
  directly from `dev`.
