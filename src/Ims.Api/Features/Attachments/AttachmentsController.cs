using Ims.Application;
using Ims.Application.Features.Attachments;
using Ims.Domain;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Attachments;

/// <summary>
/// Files (photos, scans, PDFs) attached to documents and products. Bytes live on disk, metadata in the database. What a caller may do
/// is decided per owner by an owner policy (warehouse scope for documents, master-data permissions for products); any signed-in user
/// can reach these routes, the policy answers 403/404.
/// </summary>
[Route(ApiRoutes.Attachments)]
public class AttachmentsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AttachmentDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] AttachmentOwnerType ownerType, [FromQuery] int ownerId) => SendOk(new ListAttachmentsQuery(ownerType, ownerId));

    [HttpPost, Consumes("multipart/form-data")]
    [RequestSizeLimit(Limits.MaxRequestBodyBytes), RequestTimeout(RequestTimeoutPolicies.Long)]
    [ProducesResponseType<AttachmentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Upload([FromForm] AttachmentUploadForm form)
    {
        await using var stream = form.File.OpenReadStream();
        return Ok(await Send(new UploadAttachmentCommand(form.OwnerType, form.OwnerId, form.File.FileName, stream, form.File.Length)));
    }

    /// <summary>Downloads the file. Images can be shown inline with <c>inline=true</c>; everything else is a download.</summary>
    [HttpGet(ApiRoutes.Id), RequestTimeout(RequestTimeoutPolicies.Long)]
    public async Task<IActionResult> Download(int id, [FromQuery] bool inline = false)
    {
        var file = await Send(new DownloadAttachmentQuery(id));
        // The file is untrusted content: never let a browser sniff or run it.
        Response.Headers[HeaderNames.ContentTypeOptions] = "nosniff";
        Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; sandbox";
        Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        return inline && file.IsImage ? File(file.Content, file.ContentType) : File(file.Content, file.ContentType, file.FileName);
    }

    [HttpDelete(ApiRoutes.Id)]
    public async Task<IActionResult> Delete(int id)
    {
        await Send(new DeleteAttachmentCommand(id));
        return NoContent();
    }
}
