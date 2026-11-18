# Relock Ascensions (Slay the Spire 2 mod)

Lowers ascension levels that were unlocked earlier by other means (unlock mods, another
device), per character, multiplayer and Blitz. Two routes: a manual relock of one scope, or a
recompute that proposes what the local run history can prove. Both only ever lower a max.

Design: [docs/spec.md](docs/spec.md). Background: [docs/research.md](docs/research.md).

**Status:** core logic, game adapter, a main-menu relock screen and a dev-console command are
built. Nothing runs at load; every action starts from the screen or the `relock` command.

## Relock screen

**Relock Ascensions** sits in the main menu's button column, above Quit, and opens a screen built
from the game's own Mods screen. It has one row per character, plus Multiplayer and Blitz. Each
row shows what is unlocked and what your run history earned (`Unlocked 10, earned 9`), and the
settings screen's `< 9 >` arrows start at the earned level. Move any row down, or back up to what
is unlocked. **Reset** returns every row to the earned level. **Save** lists the changes in the
game's confirmation popup and writes them only when you confirm. Rows with no runs start at their
unlocked level.

Nothing here patches the game (no Harmony). The button is a copy of the game's own menu button
in the game's own column, added when the main menu enters the scene tree, so it animates, takes
focus and hides like the stock entries. Other mods that add entries there stack with it. The
screen instantiates the game's scenes (Mods screen, Mods row, settings paginator) and swaps in
the mod's scripts, so fonts, textures, back button, scrolling and controller focus are the
game's. Unlock the Spire's icon sits under the profile button, away from the column.

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
| `src/RelockAscensions/Ui/` | Main-menu front-end: `MainMenuHook` (adds the button), `RelockScreen` (the editor submenu), `AscensionPaginator`, `SceneParts` (game scenes it is built from), `Popups`, `Strings`. |
| `src/RelockAscensions/localization/` | The mod's localization, packed into `RelockAscensions.pck` and merged by the game into its `main_menu_ui` table. Other languages fall back to English. |
| `scripts/` | `test.sh`, `build.sh`, `install.sh`, `pack_pck.py` (writes the PCK; no Godot install needed). |

## Build and test

```bash
cp local.props.example local.props   # set Sts2ManagedDir to the folder with sts2.dll
scripts/test.sh                      # core tests; no game files needed
scripts/build.sh                     # Release build + PCK, staged in dist/RelockAscensions/
```

Golden replay test (spec 8): point it at a profile's saves directory. Real saves are never
committed.

```bash
RELOCK_GOLDEN_PROFILE=/path/to/profile1 scripts/test.sh
```

Install (run by the account that owns the game install, not `dev`):

```bash
scripts/install.sh "/path/to/Slay the Spire 2"   # dll, json, pck -> <game>/mods/RelockAscensions/
```

## Front-end seam

A front-end only talks to `RelockAscensions.RelockApi`:

1. `DescribeTarget()` shows what will be written, e.g. `profile1 (shared vanilla saves)`.
2. `ReadState()` lists scopes for a picker; `PreviewManual(requests)` or `PreviewRecompute()`
   returns a `Preview` (call `Format()` for `scope: old -> new` text, or build a
   `PreviewSummary` for structured rows). Neither writes. `ReadEditor()` returns one
   `EditorRow` per scope (unlocked, earned, suggested) for an editor UI; its edits become
   requests through `RelockEditor.Requests`. `CharacterTitle(scope)` gives the
   game's localized name for a character.
3. After the player confirms, `Apply(preview.Plan)`. It re-reads the save, discards the plan if
   anything moved since the preview, refuses during a run, and saves through
   `SaveManager.SaveProgressFile()`.

## Decisions and remaining work

See [spec section 10](docs/spec.md#10-decisions) for how each design decision was resolved and
what is still open (front-end route, release details, remaining in-game checks).
