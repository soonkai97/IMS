using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

public class BusinessRulesTests
{
    private sealed class FakeRule(bool broken, string message) : IBusinessRule
    {
        public string Message => message;
        public bool IsBroken() => broken;
    }

    [Fact]
    public void Ensure_UnbrokenRule_DoesNothing()
    {
        BusinessRules.Ensure(new FakeRule(broken: false, "never shown"));
    }

    [Fact]
    public void Ensure_BrokenRule_ThrowsDomainExceptionWithTheRulesMessage()
    {
        var ex = Assert.Throws<DomainException>(() => BusinessRules.Ensure(new FakeRule(broken: true, "Nope.")));

        Assert.Equal("Nope.", ex.Message);
    }

    // ----- ReasonRequiredRule -----
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("because", false)]
    public void ReasonRequiredRule_IsBrokenWhenReasonIsBlank(string? reason, bool broken)
    {
        var rule = new ReasonRequiredRule(reason, "Reason needed.");

        Assert.Equal(broken, rule.IsBroken());
        Assert.Equal("Reason needed.", rule.Message);
    }

    // ----- DocumentHasValidLinesRule -----
    [Fact]
    public void DocumentHasValidLinesRule_NoLines_IsBrokenWithNoLinesMessage()
    {
        var rule = new DocumentHasValidLinesRule([]);

        Assert.True(rule.IsBroken());
        Assert.Equal(Messages.NoLines, rule.Message);
    }

    [Fact]
    public void DocumentHasValidLinesRule_PositiveLines_IsNotBroken()
    {
        var rule = new DocumentHasValidLinesRule([NewLine(NewProduct(), DocumentType.Receipt, 1m, 1m)]);

        Assert.False(rule.IsBroken());
    }

    [Fact]
    public void DocumentHasValidLinesRule_ALineWithZeroQuantity_IsBroken()
    {
        // Line.Create refuses qty <= 0, so a zero line can only appear through persistence; simulate that here.
        var bad = NewLine(NewProduct(), DocumentType.Receipt, 1m, 1m).WithPrivate("Qty", 0m);
        var rule = new DocumentHasValidLinesRule([NewLine(NewProduct(), DocumentType.Receipt, 1m, 1m), bad]);

        Assert.True(rule.IsBroken());
        Assert.Equal(Messages.LineQtyPositive, rule.Message);
    }
}
