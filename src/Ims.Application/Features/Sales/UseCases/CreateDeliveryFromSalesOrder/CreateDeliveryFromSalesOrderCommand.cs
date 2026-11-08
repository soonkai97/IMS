using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

/// <summary>Creates the Draft delivery (an Issue with purpose Sale) of an Approved sales order, prefilled from what remains of each line, with no price. Answers with the delivery.</summary>
public record CreateDeliveryFromSalesOrderCommand(Guid OrderId) : ICommand<DocumentDto>;
