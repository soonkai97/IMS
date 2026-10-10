using Ims.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Attachments;

/// <summary>Multipart form for uploading a file to an owner (a document or a product).</summary>
public class AttachmentUploadForm
{
    [FromForm(Name = "ownerType")] public AttachmentOwnerType OwnerType { get; set; }
    [FromForm(Name = "ownerId")] public int OwnerId { get; set; }
    [FromForm(Name = "file")] public IFormFile File { get; set; } = null!;
}
