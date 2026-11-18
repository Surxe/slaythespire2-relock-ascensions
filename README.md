# Relock Ascensions (Slay the Spire 2 mod)

Lowers ascension levels that were unlocked earlier by other means (unlock mods, another
device), per character, multiplayer and Blitz. Two routes: a manual relock of one scope, or a
recompute that proposes what the local run history can prove. Both only ever lower a max.

Design: [docs/spec.md](docs/spec.md). Background: [docs/research.md](docs/research.md).

**Status:** core logic, game adapter and a dev-console front-end are built. Nothing runs at
load; every action comes from the `relock` console command.

## Console command

Open the dev console with the backtick key (available whenever the game runs modded).

```
relock target                 show the profile that would be written
relock state                  every scope with max / selected level / cap (alias: status)
relock set <scope> <max>      preview a manual relock (scope: ironclad, mp, blitz, CHARACTER.X)
relock recompute              preview a recompute from run history, with evidence per scope
relock confirm                write the last preview
relock cancel                 drop the last preview
```

"Selected level" is the game's preferred ascension: the level character select starts on. A
relock lowers it only when it would end up above the new max.

## Layout

| Path | What |
|---|---|
| `src/RelockAscensions.Core/` | Pure C# (no game types): model, validation, manual relock, evidence replay, preferred clamping, staleness check, preview text. |
| `tests/RelockAscensions.Core.Tests/` | xUnit tests for the core, plus an opt-in golden replay test. |
| `src/RelockAscensions/` | The mod assembly: core sources compiled in, plus the game adapter (`GameAdapter`, `Compat`) and the front-end seam (`RelockApi`). |
| `scripts/` | `test.sh`, `build.sh`, `install.sh`. |

## Build and test

```bash
cp local.props.example local.props   # set Sts2ManagedDir to the folder with sts2.dll
scripts/test.sh                      # core tests; no game files needed
scripts/build.sh                     # Release build, staged in dist/RelockAscensions/
```

Golden replay test (spec 8): point it at a profile's saves directory. Real saves are never
committed.

```bash
RELOCK_GOLDEN_PROFILE=/path/to/profile1 scripts/test.sh
```

Install (run by the account that owns the game install, not `dev`):

```bash
scripts/install.sh "/path/to/Slay the Spire 2"   # -> <game>/mods/RelockAscensions/
```

## Front-end seam

A front-end only talks to `RelockAscensions.RelockApi`:

1. `DescribeTarget()` shows what will be written, e.g. `profile1 (shared vanilla saves)`.
2. `ReadState()` lists scopes for a picker; `PreviewManual(requests)` or `PreviewRecompute()`
   returns a `Preview` (call `Format()` for `scope: old -> new` text). Neither writes.
3. After the player confirms, `Apply(preview.Plan)`. It re-reads the save, discards the plan if
   anything moved since the preview, refuses during a run, and saves through
   `SaveManager.SaveProgressFile()`.

## Defaults taken for open decisions (spec 10)

| # | Decision | Implemented default |
|---|---|---|
| 1 | Writes during a run | Blocked (`RunManager.IsInProgress`). |
| 2 | Recompute overshoot | Preview is the only guard (option a). Evidence and run counts are shown per scope. |
| 3 | Backups before write | Not implemented; relies on the preview. Still open. |
| 4 | Fixture location | Synthetic fixtures in the repo; golden test reads real saves from `RELOCK_GOLDEN_PROFILE`. |
| 5 | Random preferred | Clamped to the highest Character max after the plan. |
| 6 | Multiplayer vs. character max | No coupling; each scope is independent. Still open. |
| 7 | UI route | Dev console (`relock`). Main-menu UI not built. |
