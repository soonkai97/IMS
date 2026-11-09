using Ims.Api.Tests.Harness;
using Ims.Application.Features.Returns;

namespace Ims.Api.Tests.Clients;

public sealed class ReturnsClient(ApiClient api)
{
    /// <summary>Creates the Draft return of a Posted document (no If-Match: it makes a new document).</summary>
    public Task<ApiResponse> CreateAsync(Guid originalDocumentId) => api.PostAsync("/api/returns", new CreateReturnRequest(originalDocumentId));
}
