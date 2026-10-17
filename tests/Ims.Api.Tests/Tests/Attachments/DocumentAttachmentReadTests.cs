using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Attachments;
using Ims.Domain;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.Attachments;

/// <summary>The files of a document are not more public than the document: listing and downloading them needs the permission that opens the document (<c>stock.read</c>), not only its warehouse.</summary>
[Collection(ApiCollection.Name)]
public class DocumentAttachmentReadTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_signed_token_without_the_document_read_permission_can_neither_list_nor_download_the_files_of_a_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        var uploaded = (await new AttachmentsClient(s.Admin).UploadAsync(AttachmentOwnerType.Document, draft.Id, Jpeg())).Data<AttachmentDto>();
        var withoutIt = new AttachmentsClient(fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [Permissions.MasterDataRead])));
        var withIt = new AttachmentsClient(fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: [Permissions.StockRead])));

        (await withoutIt.ListAsync(AttachmentOwnerType.Document, draft.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await withoutIt.DownloadAsync(uploaded.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await withIt.ListAsync(AttachmentOwnerType.Document, draft.Id)).ShouldBeOk();
        (await withIt.DownloadAsync(uploaded.Id)).ShouldBeOk();
    }
}
