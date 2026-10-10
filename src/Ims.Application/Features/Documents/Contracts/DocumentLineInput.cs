namespace Ims.Application.Features.Documents;

public record DocumentLineInput(int ProductId, decimal Qty, int? UomId, decimal? UnitCost, string? Remark);
