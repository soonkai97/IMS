namespace Ims.Application.Features.Catalog;

public record CreateUomCommand(UomSaveRequest Data) : ICommand<UomDto>;
