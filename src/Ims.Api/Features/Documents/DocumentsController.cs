using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet(ApiRoutes.PublicId), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(Guid id) => SendDocument(new GetDocumentQuery(id));

    [HttpPost(RouteSegments.Receipts), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateReceipt([FromBody] DocumentSaveRequest body) => SendDocument(new CreateDocumentCommand(DocumentType.Receipt, body));

    [HttpPost(RouteSegments.Issues), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateIssue([FromBody] DocumentSaveRequest body) => SendDocument(new CreateDocumentCommand(DocumentType.Issue, body));

    [HttpPut(ApiRoutes.PublicId), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(Guid id, [FromBody] DocumentSaveRequest body) => SendDocument(new UpdateDocumentCommand(id, body, IfMatch));

    [HttpPost(RouteSegments.Submit), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Submit(Guid id) => SendDocument(new SubmitDocumentCommand(id, IfMatch));

    /// <summary>Approves a Submitted document. The body is optional, except that a document with <c>requiresOverride</c> needs <c>negativeStockOverrideReason</c> here: the approver gives the reason, not the person who posts.</summary>
    [HttpPost(RouteSegments.Approve), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(Guid id, [FromBody] ApproveRequest? body = null) =>
        SendDocument(new ApproveDocumentCommand(id, body ?? new ApproveRequest(null), IfMatch));

    [HttpPost(RouteSegments.Reject), Authorize(Policy = AuthPolicies.DocumentReview)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(Guid id, [FromBody] RejectRequest body) => SendDocument(new RejectDocumentCommand(id, body, IfMatch));

    /// <summary>
    /// Takes the caller's own Submitted document back to Draft (the Edit button): same number, same lines, no reason. Repeating it on a document
    /// that is already an ordinary Draft answers 200 with the document and changes nothing.
    /// </summary>
    [HttpPost(RouteSegments.Recall), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Recall(Guid id) => SendDocument(new RecallDocumentCommand(id, IfMatch));

    /// <summary>Cancels a Draft, a Submitted or an Approved document. The reason is optional, except that cancelling an Approved document (by a reviewer it is routed to) needs one of at least 5 characters. The door is the create right or either approval tier (the Owner approves but cannot create); <c>DocumentActionPolicy.MayCancel</c> then decides for this document.</summary>
    [HttpPost(RouteSegments.Cancel), Authorize(Policy = AuthPolicies.DocumentCancel)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(Guid id, [FromBody] CancelRequest body) => SendDocument(new CancelDocumentCommand(id, body, IfMatch));

    /// <summary>Posts an Approved (or approval-free Submitted) document. There is no request body: the override reason was recorded at approval.</summary>
    [HttpPost(RouteSegments.Post), Authorize(Policy = Permissions.DocumentPost)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Post(Guid id) => SendDocument(new PostDocumentCommand(id, IfMatch));

    [HttpPost(RouteSegments.Reverse), Authorize(Policy = Permissions.DocumentReverse)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reverse(Guid id, [FromBody] ReverseRequest body) => SendDocument(new ReverseDocumentCommand(id, body, IfMatch));

    /// <summary>The concurrency token the client last saw; required on every document command (a missing header is 428, see <see cref="IfMatchHeader"/>).</summary>
    private string? IfMatch => IfMatchHeader.Read(Request, required: true);

    private async Task<IActionResult> SendDocument(IRequest<DocumentDto> request)
    {
        var document = await Send(request);
        Response.Headers[HeaderNames.ETag] = $"\"{document.ConcurrencyToken}\"";
        return Ok(document);
    }
}
