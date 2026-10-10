using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Ims.Api.Features.Documents;

/// <summary>
/// Receipts and issues share one resource and one state machine (Figure 5). Buttons in the UI come from <c>allowedActions</c>.
/// Every response that carries a document also carries its <c>ETag</c>; a write may send it back as <c>If-Match</c> to be refused with 409 when
/// someone else changed the document in the meantime, before anything is changed.
/// </summary>
[Route(ApiRoutes.Documents)]
public class DocumentsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<PagedResult<DocumentListItem>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] DocumentFilter filter) => SendOk(new ListDocumentsQuery(filter));

    /// <summary>The Home tiles and menu badges (returned to me, my drafts, posted today, approvals waiting) in one cheap call.</summary>
    [HttpGet(RouteSegments.Counts), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<DocumentCounts>(StatusCodes.Status200OK)]
    public Task<IActionResult> Counts() => SendOk(new GetDocumentCountsQuery());

    [HttpGet(ApiRoutes.Id), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(int id) => SendDocument(new GetDocumentQuery(id));

    [HttpPost(RouteSegments.Receipts), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateReceipt([FromBody] DocumentSaveRequest body) => SendDocument(new CreateDocumentCommand(DocumentType.Receipt, body));

    [HttpPost(RouteSegments.Issues), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateIssue([FromBody] DocumentSaveRequest body) => SendDocument(new CreateDocumentCommand(DocumentType.Issue, body));

    [HttpPut(ApiRoutes.Id), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(int id, [FromBody] DocumentSaveRequest body) => SendDocument(new UpdateDocumentCommand(id, body, IfMatch));

    [HttpPost(RouteSegments.Submit), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Submit(int id) => SendDocument(new SubmitDocumentCommand(id, IfMatch));

    [HttpPost(RouteSegments.Approve), Authorize(Policy = Permissions.DocumentApprove)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(int id) => SendDocument(new ApproveDocumentCommand(id, IfMatch));

    [HttpPost(RouteSegments.Reject), Authorize(Policy = Permissions.DocumentApprove)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(int id, [FromBody] RejectRequest body) => SendDocument(new RejectDocumentCommand(id, body, IfMatch));

    [HttpPost(RouteSegments.Cancel), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(int id, [FromBody] CancelRequest body) => SendDocument(new CancelDocumentCommand(id, body, IfMatch));

    [HttpPost(RouteSegments.Post), Authorize(Policy = Permissions.DocumentPost)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Post(int id, [FromBody] PostRequest body) => SendDocument(new PostDocumentCommand(id, body, IfMatch));

    [HttpPost(RouteSegments.Reverse), Authorize(Policy = Permissions.DocumentReverse)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reverse(int id, [FromBody] ReverseRequest body) => SendDocument(new ReverseDocumentCommand(id, body, IfMatch));

    /// <summary>The concurrency token the client last saw, from the If-Match header (null when absent or "*", which means "any version").</summary>
    private string? IfMatch =>
        Request.GetTypedHeaders().IfMatch.FirstOrDefault(tag => !tag.Equals(EntityTagHeaderValue.Any))?.Tag.Value?.Trim('"');

    private async Task<IActionResult> SendDocument(IRequest<DocumentDto> request)
    {
        var document = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{document.ConcurrencyToken}\"";
        return Ok(document);
    }
}
