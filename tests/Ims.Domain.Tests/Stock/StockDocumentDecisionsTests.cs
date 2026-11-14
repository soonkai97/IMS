using static Ims.Domain.Tests.Stock.SelfReviewTable;
using static Ims.Domain.Tests.Stock.ShortStockTable;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09: the three small decisions, asked of the domain. The tables are the ones that <c>ReverseDocumentHandlerTests</c>, <c>ReviewDocumentHandlerTests</c> and <c>SubmitDocumentHandlerTests</c> pinned
/// against the handlers on <c>main</c>, so the same rows now describe <c>StockDocument.CanBeReturned</c>, the self-review rule inside <c>Approve</c> and <c>Reject</c>, and
/// <c>StockAvailabilityPolicy</c>.
/// </summary>
public class StockDocumentDecisionsTests
{
    public static TheoryData<string> Names() => new(ReturnableTable.All().Select(r => r.Name));
    public static TheoryData<int> Cases() => new(Enumerable.Range(0, SelfReviewTable.All().Count()));
    public static TheoryData<int> StockCases() => new(Enumerable.Range(0, ShortStockTable.All().Count()));

    // ----- can it be returned -----
    [Theory]
    [MemberData(nameof(Names))]
    public void ADocumentCanBeReturned_OnlyWhenItIsAPostedReceiptOrAPostedIssueForASaleAndNotAReturn(string name)
    {
        var row = ReturnableTable.All().Single(r => r.Name == name);

        Assert.Equal(row.Returnable, row.Build().CanBeReturned);
    }

    [Fact]
    public void WhatCreateReturnAccepts_IsWhatCanBeReturnedSays()
    {
        foreach (var row in ReturnableTable.All().Where(r => r.Returnable)) Assert.NotNull(StockDocument.CreateReturn(row.Build(), Creator, Now));
        foreach (var row in ReturnableTable.All().Where(r => !r.Returnable)) Assert.Throws<DomainException>(() => StockDocument.CreateReturn(row.Build(), Creator, Now));
    }

    // ----- self-review -----
    [Theory]
    [MemberData(nameof(Cases))]
    public void ApprovingYourOwnSubmission_IsAllowedOnlyWithTheSettingAndNeverForAReturn(int index)
    {
        var c = SelfReviewTable.All().ElementAt(index);
        var doc = Submitted(c.Kind);

        var thrown = Record.Exception(() => doc.Approve(c.Reviewer, c.SettingOn, Now));

        if (c.Allowed) Assert.Null(thrown);
        else Assert.Equal(Messages.SelfApproval, Assert.IsType<DomainException>(thrown).Message);
        Assert.Equal(c.Allowed ? DocumentStatus.Approved : DocumentStatus.Submitted, doc.Status);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RejectingYourOwnSubmission_IsAllowedOnlyWithTheSettingAndNeverForAReturn(int index)
    {
        var c = SelfReviewTable.All().ElementAt(index);
        var doc = Submitted(c.Kind);

        var thrown = Record.Exception(() => doc.Reject("count it again", c.Reviewer, c.SettingOn, Now));

        if (c.Allowed) Assert.Null(thrown);
        else Assert.Equal(Messages.SelfRejection, Assert.IsType<DomainException>(thrown).Message);
        Assert.Equal(c.Allowed ? DocumentStatus.Draft : DocumentStatus.Submitted, doc.Status);
    }

    // ----- short stock -----
    [Theory]
    [MemberData(nameof(StockCases))]
    public void ShortStock_IsRefusedFlaggedOrLeftAloneAsTheTableWrites(int index)
    {
        var c = ShortStockTable.All().ElementAt(index);
        var doc = c.Document();

        if (c.Expected == Outcome.Refused)
        {
            var refusal = Assert.Throws<InsufficientStockException>(() => StockAvailabilityPolicy.RequiresOverride(doc, c.OnHandByProduct(), c.NegativeAllowed));
            Assert.Equal((1, c.OnHand, Needed), (refusal.ProductId, refusal.Available, refusal.Requested));
        }
        else
        {
            Assert.Equal(c.Expected == Outcome.Override, StockAvailabilityPolicy.RequiresOverride(doc, c.OnHandByProduct(), c.NegativeAllowed));
        }
        Assert.Equal(c.Type == DocumentType.Issue, StockAvailabilityPolicy.Covers(doc));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ADocumentGoesToAnApprover_WhenTheRulesAskOrAnOverrideIsNeeded(bool rulesAsk, bool needsOverride, bool expected) =>
        Assert.Equal(expected, StockAvailabilityPolicy.RequiresApproval(rulesAsk, needsOverride));
}
