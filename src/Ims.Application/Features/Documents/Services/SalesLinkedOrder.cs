using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>The sales order of a delivery created from one.</summary>
public sealed record SalesLinkedOrder(SalesOrder Order) : LinkedOrder;
