using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Documents;

/// <summary>Receipts and issues share one resource and one state machine (Figure 5). Buttons in the UI come from <c>allowedActions</c>.</summary>
[Route(ApiRoutes.Documents)]
public class DocumentsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    [HttpGet, Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<PagedResult<DocumentListItem>>(StatusCodes.Status200OK)]
    public Task<IActionResult> List([FromQuery] DocumentFilter filter) => SendOk(new ListDocumentsQuery(filter));

    [HttpGet(ApiRoutes.Id), Authorize(Policy = Permissions.StockRead)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Get(int id) => SendOk(new GetDocumentQuery(id));

    [HttpPost(RouteSegments.Receipts), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateReceipt([FromBody] DocumentSaveRequest body) => SendOk(new CreateDocumentCommand(DocumentType.Receipt, body));

    [HttpPost(RouteSegments.Issues), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> CreateIssue([FromBody] DocumentSaveRequest body) => SendOk(new CreateDocumentCommand(DocumentType.Issue, body));

    [HttpPut(ApiRoutes.Id), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Update(int id, [FromBody] DocumentSaveRequest body) => SendOk(new UpdateDocumentCommand(id, body));

    [HttpPost(RouteSegments.Submit), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Submit(int id) => SendOk(new SubmitDocumentCommand(id));

    [HttpPost(RouteSegments.Approve), Authorize(Policy = Permissions.DocumentApprove)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(int id) => SendOk(new ApproveDocumentCommand(id));

    [HttpPost(RouteSegments.Reject), Authorize(Policy = Permissions.DocumentApprove)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(int id, [FromBody] RejectRequest body) => SendOk(new RejectDocumentCommand(id, body));

    [HttpPost(RouteSegments.Cancel), Authorize(Policy = Permissions.DocumentCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Cancel(int id, [FromBody] CancelRequest body) => SendOk(new CancelDocumentCommand(id, body));

    [HttpPost(RouteSegments.Post), Authorize(Policy = Permissions.DocumentPost)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Post(int id, [FromBody] PostRequest body) => SendOk(new PostDocumentCommand(id, body));

    [HttpPost(RouteSegments.Reverse), Authorize(Policy = Permissions.DocumentReverse)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reverse(int id, [FromBody] ReverseRequest body) => SendOk(new ReverseDocumentCommand(id, body));
}
