namespace Ims.Application.Features.Documents;

public interface IDocumentReader
{
    Task<DocumentDto> GetAsync(Guid publicId, CancellationToken ct);
}
