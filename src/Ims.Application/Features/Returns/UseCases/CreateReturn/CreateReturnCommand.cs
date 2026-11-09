using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Returns;

public record CreateReturnCommand(CreateReturnRequest Data) : ICommand<DocumentDto>;
