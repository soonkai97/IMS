namespace Ims.Application.Features.Catalog;

public record UomConversionDto(int FromUomId, int ToUomId, decimal Factor);
