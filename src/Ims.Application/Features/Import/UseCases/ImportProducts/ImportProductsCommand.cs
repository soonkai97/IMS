namespace Ims.Application.Features.Import;

public sealed record ImportProductsCommand(Stream File, bool Commit) : ICommand<ImportResult>;
