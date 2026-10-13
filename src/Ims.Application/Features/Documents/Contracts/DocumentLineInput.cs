namespace Ims.Application.Features.Documents;

public record DocumentLineInput(Guid ProductId, decimal Qty, Guid? UomId, decimal? UnitCost, string? Remark);
