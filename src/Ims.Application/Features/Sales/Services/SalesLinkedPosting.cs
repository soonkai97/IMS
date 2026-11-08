using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>What posting a linked delivery carries from the check before the engine runs to the close after it.</summary>
public sealed record SalesLinkedPosting(SalesOrder Order, bool FullyDelivered) : LinkedPostingState;
