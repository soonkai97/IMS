using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>What posting a linked receipt carries from the check before the engine runs to the close after it.</summary>
public sealed record LinkedPosting(PurchaseOrder Order, bool FullyReceived);
