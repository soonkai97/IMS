using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ims.Api.Tests.Harness;

namespace Ims.Api.Tests.Clients;

/// <summary>
/// A caller of the API: one HTTP client, one bearer token, one fake client address. Raw verbs live here;
/// the feature groups (<see cref="Products"/>, <see cref="Documents"/>, ...) give them readable names.
/// </summary>
public sealed class ApiClient
{
    public ApiClient(HttpClient http, string? accessToken = null)
    {
        Http = http;
        AccessToken = accessToken;
        Auth = new AuthClient(this);
        Users = new UsersClient(this);
        Products = new ProductsClient(this);
        Categories = new CategoriesClient(this);
        Units = new UnitsClient(this);
        Warehouses = new WarehousesClient(this);
        Partners = new PartnersClient(this);
        Documents = new DocumentsClient(this);
        PurchaseOrders = new PurchaseOrdersClient(this);
        Stock = new StockClient(this);
        Reports = new ReportsClient(this);
        Settings = new SettingsClient(this);
        Modules = new ModulesClient(this);
        Audit = new AuditClient(this);
        Import = new ImportClient(this);
    }

    public HttpClient Http { get; }
    public string? AccessToken { get; set; }

    public AuthClient Auth { get; }
    public UsersClient Users { get; }
    public ProductsClient Products { get; }
    public CategoriesClient Categories { get; }
    public UnitsClient Units { get; }
    public WarehousesClient Warehouses { get; }
    public PartnersClient Partners { get; }
    public DocumentsClient Documents { get; }
    public PurchaseOrdersClient PurchaseOrders { get; }
    public StockClient Stock { get; }
    public ReportsClient Reports { get; }
    public SettingsClient Settings { get; }
    public ModulesClient Modules { get; }
    public AuditClient Audit { get; }
    public ImportClient Import { get; }

    public Task<ApiResponse> GetAsync(string url) => SendAsync(HttpMethod.Get, url);
    public Task<ApiResponse> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body);
    public Task<ApiResponse> PutAsync(string url, object? body = null) => SendAsync(HttpMethod.Put, url, body);
    public Task<ApiResponse> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url);

    public async Task<ApiResponse> SendAsync(HttpMethod method, string url, object? body = null, Action<HttpRequestMessage>? customize = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonDefaults.Options);
        if (AccessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        customize?.Invoke(request);
        using var response = await Http.SendAsync(request);
        return await ApiResponse.FromAsync(response);
    }

    /// <summary>Sends raw text as a JSON body (for malformed-body tests).</summary>
    public Task<ApiResponse> PostRawJsonAsync(string url, string json) => SendAsync(HttpMethod.Post, url, customize: r =>
        r.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    /// <summary>Multipart upload with a single "file" part, as the import endpoints expect.</summary>
    public Task<ApiResponse> UploadAsync(string url, byte[] content, string fileName = "import.xlsx") => SendAsync(HttpMethod.Post, url, customize: r =>
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        r.Content = form;
    });
}
