using System.Net.Http.Headers;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Clients;

/// <summary>The attachment endpoints (multipart upload, list, download, delete).</summary>
public sealed class AttachmentsClient(ApiClient api)
{
    public Task<ApiResponse> UploadAsync(AttachmentOwnerType ownerType, Guid ownerId, byte[] content, string fileName = "photo.jpg") =>
        api.SendAsync(HttpMethod.Post, "/api/attachments", customize: request =>
        {
            var form = new MultipartFormDataContent
            {
                { new StringContent(ownerType.ToString()), "ownerType" },
                { new StringContent(ownerId.ToString()), "ownerId" }
            };
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
            request.Content = form;
        });

    public Task<ApiResponse> ListAsync(AttachmentOwnerType ownerType, Guid ownerId) =>
        api.GetAsync($"/api/attachments?ownerType={ownerType}&ownerId={ownerId}");

    public Task<ApiResponse> DownloadAsync(Guid id) => api.GetAsync($"/api/attachments/{id}");
}
