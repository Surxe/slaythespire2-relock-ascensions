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

## Decisions and remaining work

See [spec section 10](docs/spec.md#10-decisions) for how each design decision was resolved and
what is still open (front-end route, release details, remaining in-game checks).
