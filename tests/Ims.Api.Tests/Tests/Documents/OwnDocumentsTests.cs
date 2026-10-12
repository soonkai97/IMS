using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>UX 7.2 / EA-01: Warehouse staff and Clerk edit, submit and cancel only the documents they created (drafts) or submitted (recall).</summary>
[Collection(ApiCollection.Name)]
public class OwnDocumentsTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private DocumentSaveRequest Edit(Guid productId) =>
        new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(productId, 7, 1m)).Build();

    [Theory]
    [InlineData(Roles.Clerk, Roles.WarehouseStaff)]
    [InlineData(Roles.WarehouseStaff, Roles.Clerk)]
    public async Task A_draft_of_someone_else_cannot_be_edited_submitted_or_cancelled_and_nothing_changes(string actor, string creator)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.As(creator), Warehouse, new Line(product.Id, 1, 1m));

        var update = await s.As(actor).Documents.UpdateAsync(draft.Id, Edit(product.Id));
        var submit = await s.As(actor).Documents.SubmitAsync(draft.Id);
        var cancel = await s.As(actor).Documents.CancelAsync(draft.Id, "Not mine to cancel");

        Assert.Contains("only change documents you created", update.ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        submit.ShouldBeProblem(HttpStatusCode.Forbidden);
        cancel.ShouldBeProblem(HttpStatusCode.Forbidden);
        var after = await s.GetAsync(draft.Id);
        Assert.Equal(DocumentStatus.Draft, after.Status);
        Assert.Equal(1, after.TotalQty);
        Assert.Equal(draft.ConcurrencyToken, after.ConcurrencyToken);
    }

    [Theory]
    [InlineData(Roles.Clerk)]
    [InlineData(Roles.WarehouseStaff)]
    public async Task Your_own_draft_can_be_edited_submitted_and_cancelled(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var mine = await s.DraftReceiptAsync(s.As(role), Warehouse, new Line(product.Id, 1, 1m));
        var other = await s.DraftReceiptAsync(s.As(role), Warehouse, new Line(product.Id, 1, 1m));

        Assert.Equal(7, (await s.As(role).Documents.UpdateAsync(mine.Id, Edit(product.Id))).Data<DocumentDto>().TotalQty);
        Assert.Equal(DocumentStatus.Submitted, (await s.As(role).Documents.SubmitAsync(mine.Id)).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Cancelled, (await s.As(role).Documents.CancelAsync(other.Id)).Data<DocumentDto>().Status);
    }

    [Theory]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Admin)]
    public async Task Supervisor_and_admin_act_on_any_draft(string role)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m));
        var other = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m));

        Assert.Equal(7, (await s.As(role).Documents.UpdateAsync(draft.Id, Edit(product.Id))).Data<DocumentDto>().TotalQty);
        Assert.Equal(DocumentStatus.Submitted, (await s.As(role).Documents.SubmitAsync(draft.Id)).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Cancelled, (await s.As(role).Documents.CancelAsync(other.Id)).Data<DocumentDto>().Status);
    }

    [Fact]
    public async Task A_submitted_document_can_be_recalled_only_by_its_submitter()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var bySupervisor = await s.SubmitAsync(s.Supervisor, await s.DraftReceiptAsync(s.Supervisor, Warehouse, new Line(product.Id, 1, 1m)));
        var byStaff = await s.SubmitAsync(s.Staff, await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m)));

        // Neither the clerk nor the staff user submitted these (the staff user did submit the second one).
        Assert.Contains("only cancel documents you submitted", (await s.Clerk.Documents.CancelAsync(byStaff.Id)).ShouldBeProblem(HttpStatusCode.Forbidden).Detail);
        (await s.Staff.Documents.CancelAsync(bySupervisor.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(byStaff.Id)).Status);
        Assert.Equal(DocumentStatus.Submitted, (await s.GetAsync(bySupervisor.Id)).Status);

        Assert.Equal(DocumentStatus.Cancelled, (await s.Staff.Documents.CancelAsync(byStaff.Id)).Data<DocumentDto>().Status);
        Assert.Equal(DocumentStatus.Cancelled, (await s.Admin.Documents.CancelAsync(bySupervisor.Id)).Data<DocumentDto>().Status); // a supervisor or admin may cancel any
    }

    [Fact]
    public async Task A_draft_the_staff_user_created_but_a_supervisor_submitted_can_no_longer_be_cancelled_by_the_creator()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 1, 1m));
        await s.Supervisor.Documents.SubmitAsync(draft.Id);

        (await s.Staff.Documents.CancelAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_document_that_is_past_Draft_still_answers_with_the_state_error_not_an_ownership_error()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 1, 1m));

        (await s.Clerk.Documents.UpdateAsync(posted.Id, Edit(product.Id))).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Clerk.Documents.CancelAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }
}
