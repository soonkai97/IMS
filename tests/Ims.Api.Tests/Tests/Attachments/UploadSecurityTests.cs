using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Attachments;
using Ims.Domain;
using Ims.Domain.Tests.Attachments;
using Ims.Infrastructure.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static Ims.Domain.Tests.Attachments.TestImages;

namespace Ims.Api.Tests.Tests.Attachments;

/// <summary>SEC-18: uploaded images lose their metadata, broken images are refused, and an IUploadScanner can veto a file before it is stored.</summary>
[Collection(ApiCollection.Name)]
public class UploadSecurityTests(ApiFixture fixture)
{
    private async Task<(ApiClient Admin, int ProductId)> AdminWithProductAsync()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var product = await new ProductBuilder(fixture.Reference).CreateAsync(admin);
        return (admin, product.Id);
    }

    private static AttachmentsClient Files(ApiClient client) => new(client);

    // ---------- metadata ----------

    [Fact]
    public async Task A_jpeg_with_gps_exif_is_stored_and_served_without_it()
    {
        var (admin, productId) = await AdminWithProductAsync();
        var photo = Jpeg(exif: true, comment: true, trailing: true);
        Assert.True(Contains(photo, GpsMarker)); // the control: the upload really carries GPS

        var uploaded = (await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, photo)).Data<AttachmentDto>();
        var download = (await Files(admin).DownloadAsync(uploaded.Id)).ShouldBeOk();

        Assert.Equal("image/jpeg", download.ContentType);
        Assert.False(Contains(download.Bytes, GpsMarker));
        Assert.False(Contains(download.Bytes, "Exif"));
        Assert.False(Contains(download.Bytes, CommentMarker));
        Assert.False(Contains(download.Bytes, TrailingMarker));
        Assert.Equal(Jpeg(), download.Bytes);
        Assert.Equal(download.Bytes.Length, uploaded.SizeBytes); // the recorded size is the size of what is stored
    }

    [Fact]
    public async Task A_png_with_text_chunks_and_appended_bytes_is_cleaned()
    {
        var (admin, productId) = await AdminWithProductAsync();

        var uploaded = (await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, Png(text: true, exif: true, time: true, trailing: true), "scan.png"))
            .Data<AttachmentDto>();
        var download = (await Files(admin).DownloadAsync(uploaded.Id)).ShouldBeOk();

        Assert.Equal(Png(), download.Bytes);
    }

    [Fact]
    public async Task A_webp_with_exif_is_cleaned()
    {
        var (admin, productId) = await AdminWithProductAsync();

        var uploaded = (await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, Webp(exif: true, xmp: true), "p.webp")).Data<AttachmentDto>();
        var download = (await Files(admin).DownloadAsync(uploaded.Id)).ShouldBeOk();

        Assert.Equal(Webp(), download.Bytes);
    }

    [Fact]
    public async Task An_image_without_metadata_is_stored_byte_for_byte()
    {
        var (admin, productId) = await AdminWithProductAsync();

        var uploaded = (await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, Png(), "clean.png")).Data<AttachmentDto>();

        Assert.Equal(Png(), (await Files(admin).DownloadAsync(uploaded.Id)).ShouldBeOk().Bytes);
    }

    [Fact]
    public async Task A_file_that_only_starts_like_an_image_is_refused_and_nothing_is_recorded()
    {
        var (admin, productId) = await AdminWithProductAsync();
        byte[] jpegMagicOnly = [0xFF, 0xD8, 0xFF, .. System.Text.Encoding.ASCII.GetBytes("<script>alert(1)</script>")];
        byte[] pngMagicOnly = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. System.Text.Encoding.ASCII.GetBytes("<html>")];

        var jpeg = await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, jpegMagicOnly);
        var png = await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, pngMagicOnly, "x.png");

        Assert.Equal(Messages.FileNotAValidImage, jpeg.ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        png.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Empty((await Files(admin).ListAsync(AttachmentOwnerType.Product, productId)).Data<List<AttachmentDto>>());
    }

    // ---------- upload scanner hook ----------

    [Fact]
    public void The_default_scanner_is_the_no_op_adapter()
    {
        Assert.IsType<NoOpUploadScanner>(fixture.Host.Services.GetRequiredService<IUploadScanner>());
    }

    [Fact]
    public async Task The_default_scanner_accepts_a_clean_file()
    {
        var (admin, productId) = await AdminWithProductAsync();

        (await Files(admin).UploadAsync(AttachmentOwnerType.Product, productId, Png(), "ok.png")).ShouldBeOk();
    }

    [Fact]
    public async Task A_plugged_in_scanner_sees_the_cleaned_bytes_and_a_sanitised_name_once_per_upload()
    {
        var scanner = new RecordingUploadScanner();
        using var host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton<IUploadScanner>(scanner)));
        var (admin, productId) = await AdminWithProductAsync();
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = admin.AccessToken;

        (await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, Jpeg(exif: true), "../../evil name.jpg")).ShouldBeOk();

        var (request, content) = Assert.Single(scanner.Scans);
        Assert.Equal(FileKind.Jpeg, request.Kind);
        Assert.Equal(Jpeg(), content);          // metadata already removed: the scanner sees what would be stored
        Assert.Equal(content.Length, request.Length);
        Assert.Equal("evil name.jpg", request.FileName);
    }

    [Fact]
    public async Task A_file_the_scanner_refuses_is_a_400_with_a_generic_message_and_is_never_stored()
    {
        var scanner = new RecordingUploadScanner(refuseWhenContains: "EICAR-MARKER");
        using var host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton<IUploadScanner>(scanner)));
        var (admin, productId) = await AdminWithProductAsync();
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = admin.AccessToken;
        byte[] pdf = [.. System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% EICAR-MARKER\n%%EOF")];

        var refused = await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, pdf, "doc.pdf");

        var problem = refused.ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Messages.FileRejectedByScanner, problem.Detail);
        Assert.DoesNotContain("Test.Signature", refused.Body); // the engine's wording stays in the server log
        Assert.Empty((await Files(admin).ListAsync(AttachmentOwnerType.Product, productId)).Data<List<AttachmentDto>>());
        // A clean file for the same owner still goes through the same scanner.
        (await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, Png(), "fine.png")).ShouldBeOk();
        Assert.Equal(2, scanner.Scans.Count);
    }

    [Fact]
    public async Task A_scanner_that_cannot_reach_its_engine_fails_the_upload_closed()
    {
        var scanner = new RecordingUploadScanner(fail: true);
        using var host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton<IUploadScanner>(scanner)));
        var (admin, productId) = await AdminWithProductAsync();
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = admin.AccessToken;

        var response = await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, Png(), "x.png");

        response.ShouldBe(HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("engine unavailable", response.Body);
        Assert.Empty((await Files(admin).ListAsync(AttachmentOwnerType.Product, productId)).Data<List<AttachmentDto>>());
    }

    [Fact]
    public async Task The_scanner_is_not_called_for_files_that_fail_the_earlier_checks()
    {
        var scanner = new RecordingUploadScanner();
        using var host = fixture.CreateHost(b => b.ConfigureTestServices(s => s.AddSingleton<IUploadScanner>(scanner)));
        var (admin, productId) = await AdminWithProductAsync();
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = admin.AccessToken;

        (await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, "plain text"u8.ToArray(), "a.png")).ShouldBe(HttpStatusCode.BadRequest);
        (await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, "%PDF-1.4 /JavaScript (app.alert(1))"u8.ToArray(), "a.pdf")).ShouldBe(HttpStatusCode.BadRequest);
        (await Files(client).UploadAsync(AttachmentOwnerType.Product, productId, Jpeg()[..20], "a.jpg")).ShouldBe(HttpStatusCode.BadRequest);

        Assert.Empty(scanner.Scans);
    }
}
