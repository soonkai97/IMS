using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>Posting is the point of no return: the document is frozen, and nothing can post it twice.</summary>
[Collection(ApiCollection.Name)]
public class PostedDocumentTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task A_posted_document_cannot_be_edited()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));

        var edit = new DocumentBuilder(Warehouse).WithRemark("sneaky").WithLines(new Line(product.Id, 500, 2m)).Build();
        var response = await s.Admin.Documents.UpdateAsync(posted.Id, edit);

        Assert.Contains("can no longer be edited", response.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        var unchanged = await s.GetAsync(posted.Id);
        Assert.Equal(5, Assert.Single(unchanged.Lines).Qty);
        Assert.Null(unchanged.Remark);
        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_posted_document_cannot_be_posted_again_submitted_or_rejected()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var posted = await s.ReceiveAsync(Warehouse, new Line(product.Id, 5, 2m));

        (await s.Admin.Documents.PostAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Admin.Documents.SubmitAsync(posted.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await s.Owner.Documents.RejectAsync(posted.Id, "x")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(5, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Single(await s.LedgerAsync(sourceDocId: posted.Id));
    }

    [Fact]
    public async Task A_document_that_was_never_submitted_cannot_be_posted()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 5, 2m));

        var response = await s.Admin.Documents.PostAsync(draft.Id);

        response.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(draft.Id)).Status);
        Assert.Equal(0, await s.OnHandAsync(product.Id, Warehouse));
    }

    [Fact]
    public async Task A_document_cannot_be_submitted_twice()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 5, 2m));
        (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeOk();

        (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.BadRequest);
    }
}
