namespace Ims.Application.Features.Catalog;

public sealed record CreateUomCommand(UomSaveRequest Data) : ICommand<UomDto>;
