namespace Ims.Application.Features.Returns;

/// <param name="OriginalDocumentId">The Posted document to return: an issue with purpose Sale (a customer return, which becomes a receipt) or a receipt (a supplier return, which becomes an issue). It must not itself be a return, nor Reversed or Cancelled.</param>
public sealed record CreateReturnRequest(Guid OriginalDocumentId);
