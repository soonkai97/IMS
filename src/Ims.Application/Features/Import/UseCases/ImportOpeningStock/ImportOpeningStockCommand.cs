namespace Ims.Application.Features.Import;

public sealed record ImportOpeningStockCommand(Stream File, bool Commit) : ICommand<ImportResult>;
