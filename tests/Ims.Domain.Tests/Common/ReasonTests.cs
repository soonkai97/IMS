namespace Ims.Domain.Tests.Common;

/// <summary>
/// A reason (BR-02, OUT-02) is the text a person gives for rejecting, cancelling, reversing, closing short or overriding: kept trimmed, at least <see cref="Limits.ReasonMinLength"/> characters once trimmed.
/// The sentence for "none given" belongs to the action, so the caller passes it; the sentence for "too short" is the shared one. The order is fixed: nothing given is answered first, then too short.
/// </summary>
public class ReasonTests
{
    private const string Missing = "A reason is needed for this.";

    // ---- Required: one sentence when nothing is given, another when it is too short ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \r\n")]
    public void Required_WithNothingGiven_RefusesWithTheCallersSentence(string? text)
    {
        var ex = Assert.Throws<DomainException>(() => Reason.Required(text, Missing));

        Assert.Equal(Missing, ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("abcd")]
    [InlineData("  ab  ")]
    [InlineData("   abcd   ")]   // spaces do not count
    public void Required_WithATextThatIsTooShortOnceTrimmed_RefusesWithTheSharedSentence(string text)
    {
        var ex = Assert.Throws<DomainException>(() => Reason.Required(text, Missing));

        Assert.Equal(Messages.ReasonTooShort, ex.Message);
    }

    [Fact]
    public void Required_AnswersNothingGivenBeforeTooShort()
    {
        // blank is also "shorter than the minimum", so only the order tells the two sentences apart
        Assert.NotEqual(Missing, Messages.ReasonTooShort);
        Assert.Equal(Missing, Assert.Throws<DomainException>(() => Reason.Required("     ", Missing)).Message);
        Assert.Equal(Messages.ReasonTooShort, Assert.Throws<DomainException>(() => Reason.Required("  x  ", Missing)).Message);
    }

    [Fact]
    public void Required_TheMinimumIsTheLimit_CountedOnTheTrimmedText()
    {
        var atMinimum = new string('a', Limits.ReasonMinLength);
        var belowMinimum = new string('a', Limits.ReasonMinLength - 1);

        Assert.Equal(atMinimum, Reason.Required(atMinimum, Missing).Text);
        Assert.Equal(atMinimum, Reason.Required($"   {atMinimum}   ", Missing).Text);
        Assert.Throws<DomainException>(() => Reason.Required(belowMinimum, Missing));
        Assert.Throws<DomainException>(() => Reason.Required($"   {belowMinimum}   ", Missing));
        Assert.Equal(5, Limits.ReasonMinLength);
    }

    [Fact]
    public void Required_StoresTheTrimmedText_AndKeepsTheSpacesInside()
    {
        Assert.Equal("price looks  wrong", Reason.Required("  price looks  wrong \t", Missing).Text);
        Assert.Equal("price looks wrong", Reason.Required("price looks wrong", Missing).ToString());
    }

    // ---- Of: one sentence for both, where the action has only one (a draft cleared by someone else, a module switch) ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcd")]
    [InlineData("  ab  ")]
    public void Of_RefusesNothingGivenAndTooShortWithTheSameSentence(string? text)
    {
        var ex = Assert.Throws<DomainException>(() => Reason.Of(text, Missing));

        Assert.Equal(Missing, ex.Message);
    }

    [Theory]
    [InlineData("abcde", "abcde")]
    [InlineData("  abcde  ", "abcde")]
    [InlineData("\tmaintenance window ", "maintenance window")]
    public void Of_AcceptsTheMinimumOnceTrimmed_AndKeepsItTrimmed(string text, string expected)
    {
        Assert.Equal(expected, Reason.Of(text, Missing).Text);
    }

    // ---- A value: equal by its text, never changed, no way to build one that breaks the rule ----

    [Fact]
    public void TwoReasonsWithTheSameTrimmedText_AreEqual()
    {
        var left = Reason.Required("  wrong supplier ", Missing);
        var right = Reason.Of("wrong supplier", Missing);

        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, Reason.Of("wrong product", Missing));
    }

    [Fact]
    public void TheTextIsComparedExactly_NotIgnoringCase()
    {
        Assert.NotEqual(Reason.Of("Wrong supplier", Missing), Reason.Of("wrong supplier", Missing));
    }

    [Fact]
    public void ItIsAValueObject_WithNoPublicConstructor()
    {
        Assert.IsAssignableFrom<IValueObject>(Reason.Of("wrong supplier", Missing));
        Assert.Empty(typeof(Reason).GetConstructors());
    }

    [Fact]
    public void ADefaultReason_HasNoText_NeverNull()
    {
        Assert.Equal(string.Empty, default(Reason).Text);
        Assert.Equal(string.Empty, default(Reason).ToString());
    }
}
