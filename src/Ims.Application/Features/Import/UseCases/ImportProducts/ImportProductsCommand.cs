namespace Ims.Application.Features.Import;

public record ImportProductsCommand(Stream File, bool Commit) : ICommand<ImportResult>;
