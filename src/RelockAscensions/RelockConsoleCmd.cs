using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using RelockAscensions.Core;

namespace RelockAscensions;

/// <summary>
/// Dev-console front-end (spec 10.7, console route). The game's console discovers this class
/// in loaded mods; open it with the backtick key. It only calls <see cref="RelockApi"/>:
/// previews never write, and <c>confirm</c> applies the last preview.
/// </summary>
public sealed class RelockConsoleCmd : AbstractConsoleCmd
{
    private static readonly string[] Subcommands = { "target", "state", "set", "recompute", "confirm", "cancel" };

    // Last preview awaiting confirmation. Cleared on confirm, cancel or a new preview.
    private static Preview? _pending;

    public override string CmdName => "relock";

    public override string Args => "<target|state|set <scope> <max>|recompute|confirm|cancel>";

    public override string Description =>
        "Lower or raise ascension levels. 'set' and 'recompute' preview; 'confirm' writes the last preview.";

    public override bool IsNetworked => false;

    public override bool DebugOnly => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        try
        {
            return (args.Length == 0 ? "" : args[0].ToLowerInvariant()) switch
            {
                "target" => Ok($"Target: {RelockApi.DescribeTarget().Label}"),
                "state" or "status" => State(),
                "set" => Set(args),
                "recompute" => Show(RelockApi.PreviewRecompute()),
                "confirm" => Confirm(),
                "cancel" => Cancel(),
                _ => Fail($"Usage: relock {Args}"),
            };
        }
        catch (Exception e)
        {
            Log.Error($"relock command failed: {e}");
            return Fail($"Error: {e.Message}");
        }
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            return CompleteArgument(Subcommands, Array.Empty<string>(), args.Length == 0 ? "" : args[0]);
        }
        if (args.Length == 2 && args[0].Equals("set", StringComparison.OrdinalIgnoreCase)
            && RelockApi.ReadState() is { } state)
        {
            return CompleteArgument(ScopeNames.ShortNames(state.Scopes), args[..1], args[1]);
        }
        return base.GetArgumentCompletions(player, args);
    }

    private static CmdResult State()
    {
        if (RelockApi.ReadState() is not { } state)
        {
            return Fail("Could not read state; see the log.");
        }
        var lines = new List<string> { $"Target: {RelockApi.DescribeTarget().Label}" };
        lines.AddRange(state.Scopes.Select(s => $"{s.Scope}: max {s.Max}, selected level {s.Preferred} (cap {s.Cap})"));
        if (state.Random is { } r)
        {
            lines.Add($"{r.CharacterId}: selected level {r.Preferred} (no max of its own; kept at or below the highest character max)");
        }
        return Ok(string.Join("\n", lines));
    }

    private static CmdResult Set(string[] args)
    {
        if (args.Length != 3 || !int.TryParse(args[2], out var newMax))
        {
            return Fail("Usage: relock set <scope> <max>   e.g. relock set ironclad 5, relock set mp 0, relock set silent 10");
        }
        if (RelockApi.ReadState() is not { } state)
        {
            return Fail("Could not read state; see the log.");
        }
        if (ScopeNames.Resolve(args[1], state.Scopes) is not { } scope)
        {
            if (ScopeNames.MatchCharacterId(args[1], RelockApi.KnownCharacterIds()) is { } unplayed)
            {
                // The game only adds a character's stats the first time it is played.
                return Fail($"{unplayed} has no ascension progress on this profile yet; select it once in character select first.");
            }
            return Fail($"Unknown scope '{args[1]}'. Try: {string.Join(", ", ScopeNames.ShortNames(state.Scopes))}");
        }
        return Show(RelockApi.PreviewManual(new[] { new RelockRequest(scope, newMax) }));
    }

    private static CmdResult Show(Preview preview)
    {
        _pending = null;
        if (!preview.Ok)
        {
            return Fail(preview.Format());
        }
        if (preview.Plan.IsEmpty)
        {
            return Ok(preview.Format());
        }
        _pending = preview;
        return Ok(preview.Format() + "\n\nType 'relock confirm' to write, or 'relock cancel'.");
    }

    private static CmdResult Confirm()
    {
        if (_pending is not { } preview)
        {
            return Fail("Nothing to confirm. Run 'relock set' or 'relock recompute' first.");
        }
        _pending = null;
        var result = RelockApi.Apply(preview.Plan);
        return new CmdResult(result.Status is ApplyStatus.Applied or ApplyStatus.NothingToDo, result.Message);
    }

    private static CmdResult Cancel()
    {
        var had = _pending is not null;
        _pending = null;
        return Ok(had ? "Cancelled; nothing was written." : "Nothing pending.");
    }

    private static CmdResult Ok(string msg) => new(true, msg);

    private static CmdResult Fail(string msg) => new(false, msg);
}
