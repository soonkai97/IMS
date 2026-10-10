namespace Ims.Api.Tests.Tests.Logging;

/// <summary>Request-derived text must not be able to start a new log line.</summary>
public class LogSanitizerTests
{
    [Theory]
    [InlineData("/api/products\r\nINFO forged entry", "/api/productsINFO forged entry")]
    [InlineData("/api/x\nforged", "/api/xforged")]
    [InlineData("tab\there", "tab_here")]
    public void Line_breaks_are_removed_and_other_control_characters_replaced(string input, string expected) =>
        Assert.Equal(expected, LogSanitizer.Clean(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_becomes_an_empty_string(string? input) => Assert.Equal(string.Empty, LogSanitizer.Clean(input));

    [Fact]
    public void Long_values_are_cut_to_a_bounded_length() => Assert.Equal(256, LogSanitizer.Clean(new string('a', 5_000)).Length);

    [Fact]
    public void Ordinary_text_is_unchanged() => Assert.Equal("POST /api/documents/12/post", LogSanitizer.Clean("POST /api/documents/12/post"));
}
