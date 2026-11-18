using RelockAscensions.Core;
using static RelockAscensions.Core.Tests.Fx;

namespace RelockAscensions.Core.Tests;

public class ScopeNamesTests
{
    private static readonly ScopeState[] Scopes =
    {
        Char(Ironclad, 10, 10), Char("CHARACTER.HELLOSPIRE-PALADIN", 0, 0), Mp(10, 10), Blitz(5, 5),
    };

    [Theory]
    [InlineData("CHARACTER.IRONCLAD")]
    [InlineData("character.ironclad")]
    [InlineData("ironclad")]
    [InlineData("  IRONCLAD ")]
    public void Resolves_characters(string input) =>
        Assert.Equal(Scope.Character(Ironclad), ScopeNames.Resolve(input, Scopes));

    [Fact]
    public void Resolves_modded_character_entry() =>
        Assert.Equal(Scope.Character("CHARACTER.HELLOSPIRE-PALADIN"), ScopeNames.Resolve("hellospire-paladin", Scopes));

    [Theory]
    [InlineData("mp")]
    [InlineData("Multiplayer")]
    public void Resolves_multiplayer(string input) =>
        Assert.Equal(Scope.Multiplayer, ScopeNames.Resolve(input, Scopes));

    [Fact]
    public void Resolves_blitz() => Assert.Equal(Scope.Blitz, ScopeNames.Resolve("blitz", Scopes));

    [Fact]
    public void Blitz_absent_on_build_is_null() =>
        Assert.Null(ScopeNames.Resolve("blitz", new[] { Char(Ironclad, 10, 10) }));

    [Theory]
    [InlineData("watcher")]
    [InlineData("random_character")]
    [InlineData("")]
    public void Unknown_is_null(string input) => Assert.Null(ScopeNames.Resolve(input, Scopes));

    [Fact]
    public void Short_names() =>
        Assert.Equal(new[] { "ironclad", "hellospire-paladin", "multiplayer", "blitz" }, ScopeNames.ShortNames(Scopes));

    [Fact]
    public void Matches_known_character_not_in_save()
    {
        var known = new[] { "CHARACTER.IRONCLAD", "CHARACTER.SILENT" };
        Assert.Null(ScopeNames.Resolve("silent", Scopes));
        Assert.Equal("CHARACTER.SILENT", ScopeNames.MatchCharacterId("Silent", known));
        Assert.Null(ScopeNames.MatchCharacterId("watcher", known));
    }
}
