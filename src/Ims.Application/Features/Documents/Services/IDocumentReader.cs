namespace Ims.Application.Features.Documents;

public interface IDocumentReader
{
    Task<DocumentDto> GetAsync(int id, CancellationToken ct);
}
