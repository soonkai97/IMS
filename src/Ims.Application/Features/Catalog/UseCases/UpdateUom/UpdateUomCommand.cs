namespace Ims.Application.Features.Catalog;

public record UpdateUomCommand(int Id, UomSaveRequest Data) : ICommand<UomDto>;
