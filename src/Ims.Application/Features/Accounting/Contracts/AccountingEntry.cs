using Ims.Domain;
namespace Ims.Application.Features.Accounting;

/// <summary>
/// One row of the accounting file before it is signed and formatted: the posting of a document line, or (for a reversed document) its reversal. Plain properties, not a constructor,
/// so the two kinds of row have the same shape and EF can concatenate them. The keys (document id, line id) put the rows in the file's order and never reach the client.
/// </summary>
public sealed class AccountingEntry
{
    public DateTime At { get; init; }
    public int DocumentId { get; init; }
    public int LineId { get; init; }
    public string DocNo { get; init; } = "";
    public DocumentType Type { get; init; }
    public IssuePurpose? Purpose { get; init; }
    public string WarehouseCode { get; init; } = "";
    public string? Partner { get; init; }
    public string? PartnerTin { get; init; }
    public string? PartnerSstRegNo { get; init; }
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? HsCode { get; init; }
    public string? SstCode { get; init; }
    public string BaseUnit { get; init; } = "";
    public decimal BaseQty { get; init; }
    public decimal UnitCost { get; init; }
    public decimal Amount { get; init; }
    public bool IsReversal { get; init; }
}
