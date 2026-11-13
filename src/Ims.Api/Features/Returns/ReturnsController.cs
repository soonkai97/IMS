using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Returns;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ims.Api.Features.Returns;

/// <summary>
/// The returns module (pilot slice PL3.16): the Draft return of a Posted document. While the module is off every signed-in caller gets the 404 of an unknown route (<see cref="RequiresModuleAttribute"/>), before the
/// permission is asked. The return is an ordinary stock document: it is then read, saved, submitted, approved, posted and reversed through <c>/api/documents</c> like any other; its lines name the original lines by
/// <c>returnOfLineNo</c>, and a return always needs one approval from a Supervisor (or an Administrator), whatever it is worth.
/// </summary>
[Route(ApiRoutes.Returns), RequiresModule(ModuleCodes.Returns)]
public sealed class ReturnsController(IDispatcher dispatcher) : ApiControllerBase(dispatcher)
{
    /// <summary>
    /// Creates the Draft return of a Posted document: a customer return (a Receipt, partner the customer) of an issue with purpose Sale, or a supplier return (an Issue with purpose Return, partner the supplier) of a receipt.
    /// One line for every original line that still has something to return, in the original unit, with the quantity that can still be returned. A customer return takes the goods back at the cost they left at; a supplier
    /// return is valued at the average like every issue. 409 when the document is not Posted (or was reversed), is itself a return, is not a sale or a receipt, or has nothing left to return. The number comes from the
    /// normal sequence of its type. Needs <c>returns.create</c>; the document must be in a warehouse the caller may read (404 otherwise).
    /// </summary>
    [HttpPost, Authorize(Policy = Permissions.ReturnsCreate)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateReturnRequest body)
    {
        var draft = await Send(new CreateReturnCommand(body));
        Response.Headers[HeaderNames.ETag] = $"\"{draft.ConcurrencyToken}\"";
        return Ok(draft);
    }
}
