using PokePic.Core.Model;

namespace PokePic.Core.Tests;

public class BindingSyntaxTests
{
    [Fact]
    public void Interpolated_text_is_split_into_segments()
    {
        var t = BindingSyntax.ParseText("Lv. {level} / {species.name:upper}", out var error);

        Assert.Null(error);
        Assert.Equal(
            [
                new LiteralSegment("Lv. "),
                new BindingSegment(new Binding("level")),
                new LiteralSegment(" / "),
                new BindingSegment(new Binding("species.name", "upper")),
            ],
            t!.Segments);
    }

    [Fact]
    public void Single_binding_with_negation_is_recognized()
    {
        Assert.True(BindingSyntax.TryParseSingleBinding("{!hasType2}", out var binding, out _));
        Assert.Equal(new Binding("hasType2", Negate: true), binding);
    }

    [Fact]
    public void Plain_text_is_not_a_single_binding()
    {
        Assert.False(BindingSyntax.TryParseSingleBinding("#FF0000", out _, out _));
        Assert.False(BindingSyntax.TryParseSingleBinding("{{literal}}", out _, out _));
    }

    [Fact]
    public void Text_with_a_binding_and_literals_is_not_a_single_binding()
    {
        Assert.True(BindingSyntax.TryParseSingleBinding("{a} and {b}", out var binding, out var error));
        Assert.Null(binding);
        Assert.NotNull(error);
    }

    [Fact]
    public void Doubled_braces_are_literal()
    {
        var t = BindingSyntax.ParseText("{{x}}", out _);
        Assert.Equal("{x}", t!.LiteralText);
    }

    [Theory]
    [InlineData("Lv. {level")]
    [InlineData("oops }")]
    [InlineData("{}")]
    [InlineData("{bad key}")]
    [InlineData("{level:}")]
    public void Malformed_text_is_rejected(string text)
    {
        Assert.Null(BindingSyntax.ParseText(text, out var error));
        Assert.NotNull(error);
    }
}

public class RgbaTests
{
    [Theory]
    [InlineData("#F00", 255, 0, 0, 255)]
    [InlineData("#F008", 255, 0, 0, 136)]
    [InlineData("#EF4179", 239, 65, 121, 255)]
    [InlineData("#00000080", 0, 0, 0, 128)]
    public void Valid_colors_are_parsed(string text, byte r, byte g, byte b, byte a)
    {
        Assert.True(Rgba.TryParse(text, out var color));
        Assert.Equal(new Rgba(r, g, b, a), color);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Invalid_colors_are_rejected(string text) => Assert.False(Rgba.TryParse(text, out _));

    [Fact]
    public void ToString_omits_opaque_alpha() => Assert.Equal("#EF4179", new Rgba(239, 65, 121).ToString());
}
