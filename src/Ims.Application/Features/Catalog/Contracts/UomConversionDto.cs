namespace Ims.Application.Features.Catalog;

public record UomConversionDto(Guid FromUomId, Guid ToUomId, decimal Factor);
