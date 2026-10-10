using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Builders;

/// <summary>Builds a document request for a warehouse from simple <see cref="Line"/> values.</summary>
public sealed class DocumentBuilder(int warehouseId)
{
    private readonly List<Line> _lines = [];
    private int? _partnerId;
    private IssuePurpose? _purpose = IssuePurpose.Sale; // required for an issue, ignored for a receipt
    private string? _remark;

    public DocumentBuilder WithPartner(int? partnerId) { _partnerId = partnerId; return this; }
    public DocumentBuilder WithPurpose(IssuePurpose purpose) { _purpose = purpose; return this; }
    public DocumentBuilder WithRemark(string remark) { _remark = remark; return this; }

    public DocumentBuilder WithLines(params Line[] lines)
    {
        _lines.AddRange(lines);
        return this;
    }

    public DocumentSaveRequest Build() => new(warehouseId, _partnerId, _purpose, _remark,
        _lines.Select(l => new DocumentLineInput(l.ProductId, l.Qty, l.UomId, l.UnitCost, null)).ToList());
}
