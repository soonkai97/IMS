using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class SubmitSalesOrderHandler(IRepository<SalesOrder> orders, IApprovalEvaluator approvals, ISalesOrderAccess access, ISalesOrderReader reader,
    IDocumentVersion version, ICurrentUser current, IClock clock, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<SubmitSalesOrderCommand, SalesOrderDto>
{
    public async Task<SalesOrderDto> HandleAsync(SubmitSalesOrderCommand cmd, CancellationToken ct)
    {
        var order = await orders.FirstOrDefaultAsync(new SalesOrderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        access.EnsureInScope(order.WarehouseId);
        version.EnsureCurrent(order.PublicId, order.No, order, cmd.IfMatch);

        // The order is tested at its total at cost and its total base quantity against the rules of its type, and released at once when none fires.
        var assessment = await approvals.AssessAsync(DocumentType.SalesOrder, order.TotalQty, order.TotalSellingAmount, ct);
        var before = order.Status;
        order.Submit(current.UserId, assessment.Required, assessment.IsLarge, clock.UtcNow);
        audit.Record(AuditActions.SalesOrderSubmitted, AuditEntities.SalesOrder, order.No, new { Status = before },
            new Dictionary<string, object?>
            {
                [nameof(order.Status)] = order.Status, [nameof(order.RequiresApproval)] = order.RequiresApproval, [nameof(order.IsLarge)] = order.IsLarge, [AuditFields.Amount] = assessment.Amount
            });
        await uow.SaveChangesAsync(ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
