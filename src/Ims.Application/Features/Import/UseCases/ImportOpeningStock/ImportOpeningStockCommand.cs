namespace Ims.Application.Features.Import;

public record ImportOpeningStockCommand(Stream File, bool Commit) : ICommand<ImportResult>;
