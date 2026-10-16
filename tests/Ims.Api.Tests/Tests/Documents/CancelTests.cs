using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

[Collection(ApiCollection.Name)]
public class CancelTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task A_draft_can_be_cancelled_with_a_reason_and_then_nothing_more_can_be_done_with_it()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m));

        var cancelled = (await s.Clerk.Documents.CancelAsync(draft.Id, "Ordered by mistake")).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal("Ordered by mistake", cancelled.CancelReason);
        Assert.Empty(cancelled.AllowedActions);
        (await s.Clerk.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.PostAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Clerk.Documents.CancelAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Clerk.Documents.UpdateAsync(draft.Id, new DocumentBuilder(Warehouse).WithLines(new Line(product.Id, 1, 1m)).Build()))
            .ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Empty(await s.LedgerAsync(sourceDocId: draft.Id));
    }

    [Fact]
    public async Task A_submitted_document_can_be_cancelled_without_a_reason()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var submitted = await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 5, 1m)));

        var cancelled = (await s.Clerk.Documents.CancelAsync(submitted.Id)).Data<DocumentDto>();

        Assert.Equal(DocumentStatus.Cancelled, cancelled.Status);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_posted_document_cannot_be_cancelled_only_reversed()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 1m));

        (await s.Admin.Documents.CancelAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(DocumentStatus.Posted, (await s.GetAsync(posted.Id)).Status);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task Cancel_has_no_permission_door_but_still_needs_a_signed_in_caller()
    {
        (await fixture.Anonymous().PostAsync($"/api/documents/{Guid.NewGuid()}/cancel", new CancelRequest("No token"))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Roles_without_the_create_right_cannot_cancel()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));

        (await s.Owner.Documents.CancelAsync(draft.Id)).ShouldBe(HttpStatusCode.Forbidden);
    }
}
