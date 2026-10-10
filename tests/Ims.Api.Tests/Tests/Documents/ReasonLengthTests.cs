using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>UX 6.4 / PS-09 / EA-10: a reject, reversal or override reason has at least five characters once trimmed. The API enforces it, not only the dialog.</summary>
[Collection(ApiCollection.Name)]
public class ReasonLengthTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static readonly string TooShort = Messages.ReasonTooShort;

    [Theory]
    [InlineData("abc")]
    [InlineData("no")]
    [InlineData("  ab  ")]
    public async Task A_short_reject_reason_is_400_with_a_field_error_and_the_document_stays_submitted(string reason)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));

        var problem = (await s.Supervisor.Documents.RejectAsync(submitted.Id, reason)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([TooShort], problem.ErrorsFor("reason"));
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(submitted.Id)).Status);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("abcd")]
    [InlineData("  abc ")]
    public async Task A_short_reversal_reason_is_400_and_the_document_stays_posted(string reason)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 3, 1m));

        var problem = (await s.Supervisor.Documents.ReverseAsync(posted.Id, reason)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([TooShort], problem.ErrorsFor("reason"));
        Assert.Equal(DocumentStatus.Posted, (await s.GetAsync(posted.Id)).Status);
        Assert.Equal(3, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("  ab  ")]
    public async Task A_short_override_reason_is_400_and_the_document_stays_submitted(string reason)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 8)));

        var problem = (await s.Supervisor.Documents.ApproveWithReasonAsync(submitted.Id, reason)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([TooShort], problem.ErrorsFor("negativeStockOverrideReason"));
        var after = await s.GetAsync(submitted.Id);
        Assert.Equal(DocumentStatus.Submitted, after.Status);
        Assert.Null(after.NegativeStockOverrideReason);
    }

    [Fact]
    public async Task A_short_override_reason_given_for_a_document_that_needs_none_is_still_refused()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));

        (await s.Supervisor.Documents.ApproveWithReasonAsync(submitted.Id, "abc")).ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Exactly_five_characters_after_trimming_is_accepted_and_stored_trimmed()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 100, 100m));
        var toReject = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, Warehouse, new Line(product.Id, 60)));
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        var rejected = (await s.Supervisor.Documents.RejectAsync(toReject.Id, "   abcde   ")).Data<DocumentDto>();
        var reversed = (await s.Supervisor.Documents.ReverseAsync(posted.Id, "  fghij ")).Data<DocumentDto>();

        Assert.Equal("abcde", rejected.RejectionReason);
        Assert.Equal("fghij", reversed.ReversalReason);
    }

    [Fact]
    public async Task A_blank_reason_is_reported_once_as_required_not_also_as_too_short()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        var problem = (await s.Supervisor.Documents.ReverseAsync(posted.Id, "   ")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal([Messages.ReverseReasonRequired], problem.ErrorsFor("reason"));
    }
}
