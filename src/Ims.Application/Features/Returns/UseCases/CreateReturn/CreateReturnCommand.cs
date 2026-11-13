using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Returns;

public sealed record CreateReturnCommand(CreateReturnRequest Data) : ICommand<DocumentDto>;
