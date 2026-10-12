namespace Ims.Application.Features.Documents;

public record DocumentLineInput(int ProductId, decimal Qty, Guid? UomId, decimal? UnitCost, string? Remark);
