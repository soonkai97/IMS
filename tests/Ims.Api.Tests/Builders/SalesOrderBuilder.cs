using Ims.Application.Features.Sales;

namespace Ims.Api.Tests.Builders;

/// <summary>Builds a sales-order save request for a warehouse and a customer from simple lines.</summary>
public sealed class SalesOrderBuilder(Guid warehouseId, Guid customerId)
{
    private readonly List<SalesOrderLineInput> _lines = [];
    private string? _remark;

    public SalesOrderBuilder WithRemark(string remark) { _remark = remark; return this; }

    public SalesOrderBuilder WithLine(Guid productId, decimal qty, decimal unitPrice, Guid? uomId = null, string? remark = null)
    {
        _lines.Add(new SalesOrderLineInput(productId, qty, uomId, unitPrice, remark));
        return this;
    }

    public SalesOrderSaveRequest Build() => new(warehouseId, customerId, _remark, _lines);
}
