namespace Ims.Application.Features.Catalog;

public record UpdateUomCommand(Guid Id, UomSaveRequest Data) : ICommand<UomDto>;
