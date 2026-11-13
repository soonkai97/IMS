namespace Ims.Application.Features.Catalog;

public sealed record UpdateUomCommand(Guid Id, UomSaveRequest Data) : ICommand<UomDto>;
