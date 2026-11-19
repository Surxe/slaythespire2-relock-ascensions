using RelockAscensions.Core;

namespace RelockAscensions.Core.Tests;

public class AscensionEpochsTests
{
    [Theory]
    [InlineData("CHARACTER.IRONCLAD", "IRONCLAD4_EPOCH")]
    [InlineData("CHARACTER.SILENT", "SILENT4_EPOCH")]
    [InlineData("CHARACTER.REGENT", "REGENT4_EPOCH")]
    [InlineData("CHARACTER.DEFECT", "DEFECT4_EPOCH")]
    [InlineData("CHARACTER.NECROBINDER", "NECROBINDER4_EPOCH")]
    [InlineData("CHARACTER.FOOL", "FOOL4_EPOCH")]
    public void Vanilla_characters_map_to_their_4_epoch(string character, string epoch) =>
        Assert.Equal(epoch, AscensionEpochs.For(character));

    [Theory]
    [InlineData("CHARACTER.HELLOSPIRE-PALADIN")]
    [InlineData("CHARACTER.RANDOM_CHARACTER")]
    [InlineData("character.ironclad")]
    public void Other_characters_have_no_gate(string character) => Assert.Null(AscensionEpochs.For(character));

    [Fact]
    public void Blitz_epoch_id() => Assert.Equal("BLITZ_EPOCH", AscensionEpochs.Blitz);
}
