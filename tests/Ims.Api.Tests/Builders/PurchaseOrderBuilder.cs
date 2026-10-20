using Ims.Application.Features.Purchasing;

namespace Ims.Api.Tests.Builders;

/// <summary>Builds a purchase-order save request for a warehouse and a supplier from simple lines.</summary>
public sealed class PurchaseOrderBuilder(Guid warehouseId, Guid supplierId)
{
    private readonly List<PurchaseOrderLineInput> _lines = [];
    private string? _remark;

    public PurchaseOrderBuilder WithRemark(string remark) { _remark = remark; return this; }

    public PurchaseOrderBuilder WithLine(Guid productId, decimal qty, decimal unitPrice, Guid? uomId = null, string? remark = null)
    {
        _lines.Add(new PurchaseOrderLineInput(productId, qty, uomId, unitPrice, remark));
        return this;
    }

    public PurchaseOrderSaveRequest Build() => new(warehouseId, supplierId, _remark, _lines);
}
