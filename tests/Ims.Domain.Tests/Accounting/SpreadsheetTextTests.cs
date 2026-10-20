using Ims.Application;

namespace Ims.Domain.Tests.Accounting;

/// <summary>Text a person typed never reaches a spreadsheet cell as something that can run as a formula.</summary>
public class SpreadsheetTextTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+SUM(A1)", "'+SUM(A1)")]
    [InlineData("-5 mm washer", "'-5 mm washer")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\t=cmd", "'\t=cmd")]
    [InlineData("\r=cmd", "'\r=cmd")]
    [InlineData("=HYPERLINK(\"http://example.test\",\"x\")", "'=HYPERLINK(\"http://example.test\",\"x\")")]
    public void Text_that_could_run_as_a_formula_gets_a_leading_quote(string text, string expected) => Assert.Equal(expected, SpreadsheetText.Safe(text));

    [Theory]
    [InlineData("Steel bolt M8")]
    [InlineData("SKU-001")]
    [InlineData("A=B")]
    [InlineData("5-3")]
    [InlineData(" =x")]
    [InlineData("'=already quoted is text to the spreadsheet")]
    [InlineData("")]
    public void Other_text_is_unchanged(string text) => Assert.Equal(text, SpreadsheetText.Safe(text));

    [Fact]
    public void Nothing_stays_nothing() => Assert.Null(SpreadsheetText.Safe(null));
}
