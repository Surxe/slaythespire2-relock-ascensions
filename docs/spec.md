# Spec: Relock Ascensions (STS2 mod)

Status: core, adapter (relocking and unlocking), a main-menu front-end and a dev-console
front-end are implemented (see the README). Based on [research.md](research.md). This spec
defines the logic and the seams that let further UIs be added without touching it. Section 10
records how each open decision was resolved.

## 1. Goal

Let the player lower ascension levels that were unlocked earlier by other means (cheat or
unlock mods, or progress from another device), per scope, on the current beta branch. For
convenience the mod also raises them; relocking stays the selling point and the name.

Two ways to set a value:

- **Manual relock:** the player chooses a new max for one scope, from 0 to its cap.
- **Recompute from history:** the mod proposes the max the local run history can prove,
  for every scope at once.

Both move a max **either way**, up to the scope's ceiling (4.2). Neither touches anything at
load.

## 2. Non-goals

- Unlocks other than the ascension epoch (that is Unlock the Spire's job). Raising a vanilla
  character reveals its ascension epoch (4.3), which the game needs to honour the max; the mod
  never reveals any other epoch (`BlitzEpoch` included) and never hides one.
- Changing tutorials, achievements or cards.
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
record ScopeState(Scope Scope, int Max, int Preferred, int Cap,
                  string? HiddenEpoch = null, bool RaiseLocked = false)
    Ceiling => RaiseLocked ? Max : Cap
enum RunMode { Standard, Daily, Dread, Blitz, Unknown }
record RunRecord(long StartTime, RunMode Mode, bool IsCustom, bool Win, int Ascension,
                 IReadOnlyList<string> Characters)
```

`HiddenEpoch` is the epoch that gates the scope's ascension when it is not yet revealed
(vanilla characters only; `AscensionEpochs` maps each to its `<CHAR>4_EPOCH`). `RaiseLocked` is
true for Blitz while `BlitzEpoch` is hidden: the Blitz buttons are disabled then, and the mod
does not unlock Blitz.

`RunRecord` is a plain copy of the history file's fields, built by the adapter. The core
classifies each run (it is tested; the adapter is not):

- Daily, Dread, custom and unknown-mode runs are skipped.
- Blitz runs (any player count) belong to the `Blitz` scope.
- Other runs with one player belong to that player's `Character` scope; with more than one
  player, to `Multiplayer`.
- Abandoned runs are **not** skipped: starting a run at `A` still proves `A` was unlocked.
  They can never be wins, so they never advance a scope. The validated replay included them.

### 4.2 Validation rules

- A new max must satisfy `0 <= newMax <= Ceiling`. Requests above the cap are rejected
  (`AboveCap`), not clamped; raising a `RaiseLocked` scope is rejected (`RaiseLocked`, "unlock
  Blitz first"). Lowering a `RaiseLocked` scope is allowed.
- A new max may be 0 (full relock).
- Blitz and Multiplayer use the same rules with their own caps.

### 4.3 Manual relock

`Plan(states, requests) -> Plan`, where `Plan` is:

```
record Change(Scope Scope, int OldMax, int NewMax, int OldPreferred, int NewPreferred,
              string? RevealEpoch = null)
record Plan(IReadOnlyList<Change> Changes, IReadOnlyList<Rejection> Rejections)
```

Each change sets `NewMax`, then sets `NewPreferred = min(OldPreferred, NewMax)`, so a raise
leaves preferred unchanged.

A change that raises a scope with a `HiddenEpoch` to a new max above 0 carries
`RevealEpoch = HiddenEpoch`. The game ignores a character's stored max until that epoch is
revealed; revealing it also unlocks the character's three potions (research, "Ascension
epochs"). The preview says so. Lowering never touches epochs.

### 4.4 Recompute from history

Evidence replay, as validated on the real profile (research section "History replay"):

1. Process eligible runs in `start_time` order, per scope.
2. A run at ascension `A` proves `A` unlocked: `evidence = max(evidence, A)`.
3. A win with `A == evidence` (and `evidence < cap`) advances: `evidence += 1`.

This gives a **lower bound** per scope, or **no evidence** when the scope has no eligible runs.

Plan rule, per scope:

- **No evidence:** no change. Never report 0 for a scope with no data.
- **Otherwise:** propose `NewMax = min(evidence, Ceiling)` when that differs from the current
  max, lower or higher, with preferred and epochs as in 4.3.

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
- Epochs: `SaveManager.IsEpochRevealed(id)` sets each vanilla character's `HiddenEpoch` and
  Blitz's `RaiseLocked` (`BLITZ_EPOCH`). Multiplayer has no gate.
- Blitz is read and written through reflection (`AccessTools.PropertyGetter/Setter` on
  `ProgressState`), so the mod still loads on builds without Blitz. If the property is absent,
  the Blitz scope is omitted from the plan.
- History: `RunHistorySaveManager` for the active profile. Each file goes through its
  `LoadHistory(name)` (migrations included), then is mapped to `RunRecord`. Files ending in
  `.backup` are never read.

### 5.2 Writing

1. Re-read the current state. Discard any plan whose `OldMax` or `OldPreferred` no longer
   matches, whose `RevealEpoch` no longer matches the scope's `HiddenEpoch` (an epoch was
   revealed elsewhere), or whose raise is now above the ceiling (the save changed since the
   preview was built).
2. Apply the max and preferred changes to `ProgressState`.
3. Reveal each `RevealEpoch` with `SaveManager.ObtainEpochOverride(id, Revealed)`, which
   creates the entry when missing. Never `RevealEpoch`: it throws on a missing entry and sends
   a metric. `EpochState.Revealed` is resolved by name.
4. Call `SaveManager.Instance.SaveProgressFile()`.

On failure every in-memory change is undone and nothing is saved. An epoch's prior state (and
obtain date) is put back with `ObtainEpochOverride`; an entry the mod created is removed from
`ProgressState._epochs` by reflection, since the game has no public way to delete one.

The adapter never builds file paths and never chooses between vanilla and modded. Whatever
the game has loaded is what gets written.

### 5.3 Compatibility

- Target: beta `v0.113.1`. The adapter checks `ReleaseInfo` and logs a warning on other
  versions; it refuses to write when a required member is missing.
- Harmony patches are not used. The mod does not patch the game. (Unlock the Spire patches
  heavily; that is not needed here.)
- The main-menu UI needs no patch either. The only game touchpoints are public API and scene
  names: the scene tree's `node_added` signal, then `NGame.RootSceneContainer`'s
  `child_entered_tree`; the `MainMenuTextButtons` container with its `CompendiumButton`
  (copied as the template) and `QuitButton` (insert position); the main menu's
  `SubmenuStack`; the scenes `modding_screen.tscn`, `modding_screen_row.tscn` and
  `paginator.tscn` and their node names; and `NGenericPopup`, `NModalContainer` and the
  `main_menu_ui` loc table. If a name moves in an update, the button is skipped or the screen
  reports that it could not open, with a logged error, and the console still works.
- The UI classes subclass game classes (`NSubmenu`, `NPaginator`). Godot's C# source
  generators are referenced to make them Godot scripts (no Godot install needed to build).

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
  Scopes, Model                    ReadState(Progress)          Dev-console command (built)
  Validation                       ReadHistory(runs)            Main-menu button (built)
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
  Regent, Necrobinder = 10; Defect = 9; Multiplayer = 10). Scopes with no eligible runs (Blitz,
FOOL, HELLOSPIRE-*: never played solo) have **no evidence** and are left unchanged (4.4); on
the real profile their saved value is 0, on an unlocked fork they stay at the cap. This
  needs real run data. Decide where that fixture lives (section 10).
- **Adapter tests:** not automated. They run in the game.
- **In-game acceptance:** section 9, on a fork profile only.

## 9. Acceptance criteria

1. With no mod action, the mod writes nothing: no write is logged by the mod and the ascension
   fields are unchanged after loading and switching profiles. (Byte-identity cannot be checked:
   the game itself rewrites the active profile's `progress.save` on quit.)
2. A manual relock on a fork profile changes only the requested scope, plus preferred
   clamping, and nothing else in `progress.save`.
3. A recompute on the fork profile, built from a copy of `profile1` history, reproduces the
   golden table in section 8 (scopes with no evidence keep their unlocked value).
4. A scope with no eligible runs is left unchanged.
5. A recompute raises a scope to its evidence when history earned more than is unlocked
   (clamped to the ceiling), and never touches a scope with no eligible runs.
6. Cancelling the preview writes nothing.
7. The mod does not crash, and writes nothing, when a required member is missing.
8. The mod works whether SavesMerger is enabled or not, and writes to the same profile the
   game shows.
9. A raise changes only its scope (plus preferred clamping elsewhere) and, for a vanilla
   character whose ascension epoch was hidden, that one epoch, which becomes revealed.
10. A Blitz raise is refused while Blitz is locked (`BlitzEpoch` hidden); lowering still works.
11. Relocking never hides an epoch: a character relocked to 0 keeps its epoch revealed.

## 10. Decisions

Resolved (implemented defaults; revisit if testing shows otherwise):

1. **Writes during an active run:** blocked (`RunManager.IsInProgress`). The mod edits the same
   in-memory `ProgressState` the game saves, so a write would not be lost, but a run holds its own
   unlock state; blocking is simplest. In-game refusal not yet tested.
2. **Recompute overshoot on incomplete history:** the preview is the only guard (option a). It
   shows evidence and eligible-run count per scope, so a thinly covered scope is visible before
   confirming.
3. **Backups before write:** none. A file backup would mean building save paths, which this spec
   avoids (5.2). Every write logs `scope: old -> new`, which serves as the undo record.
4. **Fixture location:** synthetic fixtures in the repo; the golden test reads real saves from
   `RELOCK_GOLDEN_PROFILE` and skips when unset. No real saves are committed.
5. **Random preferred:** clamped to the highest Character max after the plan.
6. **Multiplayer vs. character maxes:** independent scopes, as the game treats them.
7. **UI route:** both. The dev console (`relock`) covers manual and recompute. The main menu
   gets a **relock screen**, an editor that combines the two routes. Every scope starts at its
   unlocked level (`RelockEditor`), the player moves any of them between 0 and the ceiling or
   takes the run-history values (item 10), and the edited values go through the manual
   planner. Save shows the changes in the game's confirmation popup and writes on confirm.
   - Entry: a copy of the game's own menu button, added to the game's button column from a
     scene-tree signal before the menu runs `_Ready`. No Harmony patch is needed. It matches
     the stock entries, hides with them, and stacks with other mods' entries instead of
     overlapping.
   - Screen: the game's Mods screen and Mods rows with the mod's scripts swapped in, and the
     settings paginator for the values.
   - Strings: shipped in the mod's PCK as `main_menu_ui` and `settings_ui` loc tables (the
     latter for the BaseLib page's labels), merged by the game.
   - Shared library: considered and rejected for the button. BaseLib has a mod-config screen,
     not a main-menu button API. DevMode's KitLib has a main-menu button registry, but only
     inside that mod. (The startup freezes once blamed on BaseLib came from the Steam overlay.)
   - Settings: optional BaseLib page. One setting, **Hide main menu button** (the console
     command still works), lives in `Settings` in the main assembly, default off. BaseLib stays
     a soft dependency: the manifest declares none, and no type in `RelockAscensions.dll`
     references BaseLib, because the loader calls `GetTypes()` on it and a `ModConfig`
     subclass would fail to load without BaseLib. The `SimpleModConfig` subclass lives in
     `RelockAscensions.BaseLibConfig.dll`, shipped alongside and loaded by the mod at the first
     main menu (when every mod is loaded, so load order does not matter) only if BaseLib is in
     the process. Its properties forward to `Settings`; BaseLib loads and saves them in
     `mod_configs/RelockAscensions.cfg`. Without BaseLib the setting keeps its default.
   - Raising (unlocking): item 10.
10. **Unlocking.** The mod also raises ascensions; relocking stays the selling point and the
    name. Background: research, "Ascension epochs (unlocking)".
    - **Range:** every scope goes from 0 to its cap (modded characters: 10). The planner
      rejects values above the cap (`AboveCap`); a change can go either way.
    - **Preferred:** unchanged when a max goes up; lowering still clamps it (4.3).
    - **Epochs:** raising a vanilla character above 0 also reveals its ascension epoch
      (`<CHAR>4_EPOCH`) through `ObtainEpochOverride(id, Revealed)` when it is not revealed.
      That epoch also unlocks the character's three potions; accepted, and stated in the
      confirm popup and the player-facing docs. Modded characters have no epoch gate.
      Relocking never hides an epoch. The staleness check covers epoch state.
    - **Blitz:** raisable only when `BlitzEpoch` is revealed (Blitz mode unlocked). The mod
      never reveals `BlitzEpoch`. While Blitz is locked (`RaiseLocked`) the screen hides its
      row, as the game hides a character never selected; the planner still allows lowering it
      (console). The player-facing docs say Blitz must be unlocked first.
    - **Recompute can raise:** the plan proposes `NewMax = evidence` (within the ceiling)
      whenever it differs from the current max (no evidence: still no change). Acceptance 5 is
      reversed. The console's `relock recompute` follows.
    - **Console:** `relock set <scope> <max>` accepts 0 to the cap.
    - **Screen:** rows open at the current unlocked level. **Reset** returns every row there
      (discards edits). A **Use Run History** button sets each row to what its runs earned (up
      or down, within the ceiling; rows with no runs stay), with help text in the right-hand
      info panel while it is hovered or focused. A row's name is green above its unlocked
      level and gold below. The pending line and confirm popup are direction-neutral; the popup
      groups raises ("Up") and relocks ("Down"), with one line naming every character whose
      epoch the save will reveal. Reset, Use Run History and Save sit centered in the
      list box, pinned to its bottom.
      The right-hand panel notes that Blitz and every character except Ironclad must be
      unlocked in the game before their Ascension can be raised, and that a character appears
      once selected in character select and Blitz once unlocked. For the hovered or focused row
      it adds what the row does not show: the run count behind "earned" and why it can be too
      low, whether raising unlocks the character's Ascension and potions, and what Save would
      change (max, and the selected level when it drops). No "earned more than unlocked" nudge.
    - **Player-facing docs (later):** FAQ "Can I unlock ascensions too? A: Yes! While the
      selling point is relocking ascensions and many other mods feature unlocking, this mod
      does too for convenience.", plus the potion side effect and the Blitz prerequisite.

Still open:

8. **Release.** `min_game_version` in the manifest (the loader warns when it is absent), author
   and version placeholders, and how to publish (Workshop or manual).
9. **Remaining in-game checks.** The relock screen (button placement next to Unlock the
   Spire, screen layout and row fit, mouse and controller paging, Reset, confirm popup, apply
   and refresh, back button), unlocking (item 10: arrows up to the cap, 5 for Blitz, and
   stopping at the current level while Blitz is locked; row colors; the Use Run History
   button's placement and its help on hover and controller focus; the grouped popup with its
   epoch line; after saving, character select shows the raised max for a character whose epoch
   was hidden and its potions are unlocked on the timeline; relocking it back to 0 leaves the
   epoch revealed; `relock set ironclad 10` + `relock confirm`; `relock recompute` proposing
   raises; acceptance 9 to 11), acceptance 3 (recompute on an unlocked fork, using the
   running-game fork procedure in research), refusal of `relock confirm` during a run, and
   acceptance 8 (SavesMerger off: the target should read `profileN (modded saves)`).
## 11. Build and deploy (proposed)

- Solution with the core as a class library (`net9.0` or `net10.0`, whichever the game
  runtime uses), its unit tests, and the mod project that references the game DLLs
  (`sts2.dll`, `GodotSharp.dll`, `0Harmony.dll`) from a local copy. Those DLLs are not checked
  in; the build reads them from a path set in a local props file.
- Output: `RelockAscensions.dll`, a manifest (`RelockAscensions.json`) and
  `RelockAscensions.pck` (localization only, written by `scripts/pack_pck.py`) in the `mods/`
  folder, following the loader's layout (section "Mod loading" in research).
- Deploy by copying the built files into the game folder. Mod-folder placement must be
  done by the player or by a small install script, never by editing the game install
  directly from `dev`.
