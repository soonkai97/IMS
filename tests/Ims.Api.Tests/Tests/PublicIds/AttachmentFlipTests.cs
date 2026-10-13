using System.Net;
using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Attachments;
using Ims.Domain;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan PR-7: an attachment and its owner are named by public id, for a document and for a product alike. The owner is looked up among the owners of the type asked for, so the
/// id of a row of another kind, an unknown id and an out-of-scope id are the same 404, and the answer never tells an existing owner from a missing one for a caller who may not ask.
/// </summary>
[Collection(ApiCollection.Name)]
public class AttachmentFlipTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private static AttachmentsClient Files(ApiClient client) => new(client);

    [Fact]
    public async Task A_file_of_a_document_and_a_file_of_a_product_answer_with_the_public_ids_stored_for_them()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));

        var onDocument = await Files(s.Admin).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg());
        var onProduct = await Files(s.Admin).UploadAsync(AttachmentOwnerType.Product, product.Id, Png(), "p.png");

        foreach (var (response, owner) in new[] { (onDocument, draft.Id), (onProduct, product.Id) })
        {
            var file = response.Data<AttachmentDto>();
            // Stored with the public id the response gave, against the integer key of the owner that has the public id the response names.
            var ownerTable = file.OwnerType == AttachmentOwnerType.Document ? "StockDocument" : "Product";
            Assert.Equal(1, await fixture.Database.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM Attachment WHERE PublicId = @a AND OwnerId = (SELECT Id FROM {ownerTable} WHERE PublicId = @o)", ("@a", file.Id), ("@o", owner)));
            Assert.Equal(owner, file.OwnerId);
            Assert.Equal(JsonValueKind.String, response.Json().RootElement.GetProperty("ownerId").ValueKind);

            var listed = Assert.Single((await Files(s.Admin).ListAsync(file.OwnerType, owner)).Data<List<AttachmentDto>>());
            Assert.Equal((file.Id, owner, "admin"), (listed.Id, listed.OwnerId, listed.UploadedBy));
            Assert.Equal(file.SizeBytes, (await Files(s.Admin).DownloadAsync(file.Id)).ShouldBeOk().Bytes.Length);
        }

        var deleted = await s.Admin.DeleteAsync($"/api/attachments/{onProduct.Data<AttachmentDto>().Id}");
        deleted.ShouldBe(HttpStatusCode.NoContent);
        Assert.Empty((await Files(s.Admin).ListAsync(AttachmentOwnerType.Product, product.Id)).Data<List<AttachmentDto>>());
        Assert.Single((await Files(s.Admin).ListAsync(AttachmentOwnerType.Document, draft.Id)).Data<List<AttachmentDto>>()); // the other owner's file is untouched
    }

    [Fact]
    public async Task The_integer_forms_are_gone_the_owner_id_must_be_a_guid_and_an_integer_attachment_route_is_404()
    {
        var s = await StockScenario.CreateAsync(fixture);

        (await s.Admin.GetAsync("/api/attachments/1")).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.DeleteAsync("/api/attachments/1")).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.GetAsync("/api/attachments?ownerType=Product&ownerId=1")).ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync("/api/attachments?ownerType=Document&ownerId=abc")).ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(AttachmentOwnerType.Product)]
    [InlineData(AttachmentOwnerType.Document)]
    public async Task An_unknown_empty_or_other_kind_of_owner_id_is_one_404_whether_the_caller_lists_or_uploads(AttachmentOwnerType type)
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));
        var otherKind = type == AttachmentOwnerType.Product ? draft.Id : product.Id; // a real public id, of the other kind of owner
        // The id of a product given as the owner of a document (and the reverse) must not be found: the look-up is per owner type.
        var ids = new[] { Guid.NewGuid(), Guid.Empty, otherKind, Warehouse };

        var listed = new List<ProblemBody>();
        foreach (var id in ids) listed.Add((await Files(s.Admin).ListAsync(type, id)).ShouldBeProblem(HttpStatusCode.NotFound));
        var uploaded = (await Files(s.Admin).UploadAsync(type, Guid.NewGuid(), Jpeg())).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.All(listed.Append(uploaded), p => Assert.Equal((listed[0].Title, listed[0].Detail), (p.Title, p.Detail)));
        Assert.Equal(type == AttachmentOwnerType.Product ? Messages.ProductNotFound : Messages.DocumentNotFound, listed[0].Detail);
        // Nothing was attached to the row of the other kind that happens to share the integer key.
        Assert.Empty((await Files(s.Admin).ListAsync(type == AttachmentOwnerType.Product ? AttachmentOwnerType.Document : AttachmentOwnerType.Product, otherKind)).Data<List<AttachmentDto>>());
    }

    [Fact]
    public async Task An_upload_without_an_owner_is_a_400_and_a_caller_without_the_permission_gets_the_same_403_for_a_real_and_an_unknown_product()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var empty = (await Files(s.Admin).UploadAsync(AttachmentOwnerType.Product, Guid.Empty, Jpeg())).ShouldBeProblem(HttpStatusCode.BadRequest);
        var real = (await Files(s.Clerk).UploadAsync(AttachmentOwnerType.Product, product.Id, Jpeg())).ShouldBeProblem(HttpStatusCode.Forbidden);
        var unknown = (await Files(s.Clerk).UploadAsync(AttachmentOwnerType.Product, Guid.NewGuid(), Jpeg())).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Contains(Messages.AttachmentOwnerRequired, empty.Errors!.Values.SelectMany(v => v));
        Assert.Equal((real.Title, real.Detail), (unknown.Title, unknown.Detail)); // the permission is checked before the owner is looked up: no existence oracle
    }

    [Fact]
    public async Task The_audit_log_names_an_attachment_by_its_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var file = (await Files(s.Admin).UploadAsync(AttachmentOwnerType.Product, product.Id, Png(), "a.png")).Data<AttachmentDto>();
        (await s.Admin.DeleteAsync($"/api/attachments/{file.Id}")).ShouldBe(HttpStatusCode.NoContent);

        var events = (await s.Admin.Audit.QueryAsync(entityType: nameof(Attachment), entityId: file.Id.ToString())).Data<AuditPage>().Items;

        Assert.Equal([AuditActions.AttachmentDeleted, AuditActions.AttachmentUploaded], events.Select(e => e.Action).Order());
        Assert.All(events, e => Assert.Equal(file.Id.ToString(), e.EntityId));
        // Both payloads say the same things: no owner id at all, so the integer key of the owner never reaches the audit trail.
        Assert.All(events, e => Assert.DoesNotContain("OwnerId", e.Before ?? e.After ?? ""));
        Assert.All(events, e => Assert.Contains(nameof(AttachmentOwnerType.Product), e.Before ?? e.After ?? ""));
    }

    [Fact]
    public async Task A_caller_without_the_document_permission_gets_the_same_403_for_a_real_and_an_unknown_document_on_upload_and_delete()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m));
        var file = (await Files(s.Admin).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg())).Data<AttachmentDto>();

        // The Owner reads every document but may not create or change one: the permission is checked before the document is looked up.
        var real = (await Files(s.Owner).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg())).ShouldBeProblem(HttpStatusCode.Forbidden);
        var unknown = (await Files(s.Owner).UploadAsync(AttachmentOwnerType.Document, Guid.NewGuid(), Jpeg())).ShouldBeProblem(HttpStatusCode.Forbidden);
        var delete = (await s.Owner.DeleteAsync($"/api/attachments/{file.Id}")).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal((real.Title, real.Detail), (unknown.Title, unknown.Detail));
        Assert.Equal((real.Title, real.Detail), (delete.Title, delete.Detail));
        Assert.Single((await Files(s.Admin).ListAsync(AttachmentOwnerType.Document, draft.Id)).Data<List<AttachmentDto>>()); // nothing was added or removed
    }
}
