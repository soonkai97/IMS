using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

public sealed class ImportClient(ApiClient api)
{
    public Task<ApiResponse> TemplateAsync(string kind) => api.GetAsync($"/api/import/templates/{kind}");

    public Task<ApiResponse> ProductsAsync(byte[] workbook, bool commit) =>
        api.UploadAsync($"/api/import/products?commit={commit.ToString().ToLowerInvariant()}", workbook);

    public Task<ApiResponse> OpeningStockAsync(byte[] workbook, bool commit) =>
        api.UploadAsync($"/api/import/opening-stock?commit={commit.ToString().ToLowerInvariant()}", workbook);
}
