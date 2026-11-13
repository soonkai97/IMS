namespace Ims.Application.Features.Catalog;

public sealed record UomConversionDto(Guid FromUomId, Guid ToUomId, decimal Factor);
