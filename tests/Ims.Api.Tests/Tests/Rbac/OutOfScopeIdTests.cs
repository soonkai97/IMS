using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Attachments;
using Ims.Application.Features.Documents;
using Ims.Domain;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>
/// SEC-11: an id outside the caller's warehouses is answered exactly like an id that does not exist (404, same title and message), on every by-id
/// route of documents and of the files attached to them. A 403 or a different message would confirm that the id is real.
/// </summary>
[Collection(ApiCollection.Name)]
public class OutOfScopeIdTests(ApiFixture fixture)
{
    private static readonly Guid MissingId = Guid.NewGuid();

    private const string GetDocument = "get a document";
    private const string UpdateDocument = "update a document";
    private const string SubmitDocument = "submit a document";
    private const string RecallDocument = "recall a document";
    private const string CancelDocument = "cancel a document";
    private const string PostDocument = "post a document";
    private const string ApproveDocument = "approve a document";
    private const string RejectDocument = "reject a document";
    private const string ReverseDocument = "reverse a document";
    private const string ListFiles = "list the files of a document";
    private const string UploadFile = "upload a file to a document";
    private const string DownloadFile = "download a file";
    private const string DeleteFile = "delete a file";

    private sealed record Setup(StockScenario Scenario, Guid DocumentId, Guid FileId, DocumentSaveRequest Edit);

    private static AttachmentsClient Files(ApiClient client) => new(client);

    public static IEnumerable<object[]> Routes() =>
    [
        [GetDocument], [UpdateDocument], [SubmitDocument], [RecallDocument], [CancelDocument], [PostDocument], [ApproveDocument], [RejectDocument], [ReverseDocument],
        [ListFiles], [UploadFile], [DownloadFile], [DeleteFile]
    ];

    /// <summary>A draft receipt with a file attached, in a warehouse that the seeded staff and supervisor users are not assigned to.</summary>
    private async Task<Setup> CreateAsync()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, other.Id, new Line(product.Id, 1, 1m));
        var file = (await Files(s.Admin).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg())).Data<AttachmentDto>();
        var edit = new DocumentBuilder(other.Id).WithLines(new Line(product.Id, 2, 1m)).Build();
        return new Setup(s, draft.Id, file.Id, edit);
    }

    /// <summary>The id a route is given when it is real but out of scope, and the call itself.</summary>
    private static (Guid KnownId, Func<Guid, Task<ApiResponse>> Call) Route(string route, Setup setup)
    {
        var s = setup.Scenario;
        return route switch
        {
            GetDocument => (setup.DocumentId, id => s.Staff.Documents.GetAsync(id)),
            UpdateDocument => (setup.DocumentId, id => s.Staff.Documents.UpdateAsync(id, setup.Edit)),
            SubmitDocument => (setup.DocumentId, id => s.Staff.Documents.SubmitAsync(id)),
            RecallDocument => (setup.DocumentId, id => s.Staff.Documents.RecallAsync(id)),
            CancelDocument => (setup.DocumentId, id => s.Staff.Documents.CancelAsync(id)),
            PostDocument => (setup.DocumentId, id => s.Staff.Documents.PostAsync(id)),
            ApproveDocument => (setup.DocumentId, id => s.Supervisor.Documents.ApproveAsync(id)),
            RejectDocument => (setup.DocumentId, id => s.Supervisor.Documents.RejectAsync(id, "Not needed")),
            ReverseDocument => (setup.DocumentId, id => s.Supervisor.Documents.ReverseAsync(id, "Entered twice")),
            ListFiles => (setup.DocumentId, id => Files(s.Staff).ListAsync(AttachmentOwnerType.Document, id)),
            UploadFile => (setup.DocumentId, id => Files(s.Staff).UploadAsync(AttachmentOwnerType.Document, id, Jpeg())),
            DownloadFile => (setup.FileId, id => Files(s.Staff).DownloadAsync(id)),
            DeleteFile => (setup.FileId, id => s.Staff.DeleteAsync($"/api/attachments/{id}")),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown route.")
        };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task An_id_outside_the_callers_warehouses_is_answered_exactly_like_an_id_that_does_not_exist(string route)
    {
        var setup = await CreateAsync();
        var (knownId, call) = Route(route, setup);

        var outside = (await call(knownId)).ShouldBeProblem(HttpStatusCode.NotFound);
        var missing = (await call(MissingId)).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.Equal((missing.Title, missing.Detail), (outside.Title, outside.Detail));
    }

    [Fact]
    public async Task Nothing_changed_behind_the_404s_and_the_same_ids_work_for_someone_who_sees_the_warehouse()
    {
        var setup = await CreateAsync();
        var s = setup.Scenario;

        foreach (var route in Routes().Select(r => (string)r[0]))
        {
            var (knownId, call) = Route(route, setup);
            await call(knownId);
        }

        Assert.Equal(DocumentStatus.Draft, (await s.GetAsync(setup.DocumentId)).Status);
        Assert.Single((await Files(s.Admin).ListAsync(AttachmentOwnerType.Document, setup.DocumentId)).Data<List<AttachmentDto>>());
        (await Files(s.Admin).DownloadAsync(setup.FileId)).ShouldBeOk();
    }

    [Fact]
    public async Task A_file_on_a_document_in_the_callers_own_warehouse_is_still_served()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Staff, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var file = (await Files(s.Staff).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg())).Data<AttachmentDto>();

        (await Files(s.Staff).DownloadAsync(file.Id)).ShouldBeOk();
        Assert.Single((await Files(s.Staff).ListAsync(AttachmentOwnerType.Document, draft.Id)).Data<List<AttachmentDto>>());
        (await s.Staff.DeleteAsync($"/api/attachments/{file.Id}")).ShouldBe(HttpStatusCode.NoContent);
    }
}
