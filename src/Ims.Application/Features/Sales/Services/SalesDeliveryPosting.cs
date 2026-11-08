using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// Posting a delivery created from a sales order. The link exists from PL3.5; the posting side (the order's lock, the authoritative over-delivery check, closing the order when everything is delivered) is
/// PL3.7. Until then a linked delivery is NOT posted: letting it through as an ordinary issue would skip the checks the link exists for. The refusal comes before anything is read or locked, so nothing
/// is written. When PL3.7 lands this class takes the order's lock first and checks the order and the quantities, exactly as the receipt side does.
/// </summary>
public class SalesDeliveryPosting(ICallerPermissions caller, IModuleSwitches modules) : IDocumentLinkPosting, IAppService
{
    public DocumentLinkKind Kind => DocumentLinkKind.SalesOrder;

    public async Task<LinkedPostingState?> BeforePostAsync(StockDocument document, CancellationToken ct)
    {
        await SalesDeliveryGuard.EnsureAsync(DocumentActions.Post, caller, modules, ct);
        throw new ConflictException(Messages.DeliveryCannotPostYet);
    }

    public void AfterPost(LinkedPostingState? state, StockDocument document)
    {
    }
}
