using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Stock;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Builders;

/// <summary>
/// Everyday inventory flows for tests: make a product or warehouse, receive and issue stock, read balances and the ledger.
/// The "complete" helpers do create, submit, (approve if the rules ask for it) and post, using different people where the rules require it.
/// </summary>
public sealed class StockScenario
{
    private StockScenario(ApiFixture fixture, ApiClient admin, ApiClient owner, ApiClient supervisor, ApiClient staff, ApiClient clerk)
    {
        Fixture = fixture;
        Admin = admin;
        Owner = owner;
        Supervisor = supervisor;
        Staff = staff;
        Clerk = clerk;
    }

    public ApiFixture Fixture { get; }
    public ReferenceData Reference => Fixture.Reference;
    public ApiClient Admin { get; }
    public ApiClient Owner { get; }
    public ApiClient Supervisor { get; }
    public ApiClient Staff { get; }
    public ApiClient Clerk { get; }

    public static async Task<StockScenario> CreateAsync(ApiFixture fixture) => new(
        fixture,
        await fixture.AsAsync(Roles.Admin), await fixture.AsAsync(Roles.Owner), await fixture.AsAsync(Roles.Supervisor),
        await fixture.AsAsync(Roles.WarehouseStaff), await fixture.AsAsync(Roles.Clerk));

    public ApiClient As(string role) => role switch
    {
        Roles.Admin => Admin,
        Roles.Owner => Owner,
        Roles.Supervisor => Supervisor,
        Roles.WarehouseStaff => Staff,
        Roles.Clerk => Clerk,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };

    // ---------- master data ----------

    public Task<ProductDto> NewProductAsync(decimal safetyStock = 0, Action<ProductBuilder>? customize = null)
    {
        var builder = new ProductBuilder(Reference).WithSafetyStock(safetyStock);
        customize?.Invoke(builder);
        return builder.CreateAsync(Admin);
    }

    public async Task<WarehouseDto> NewWarehouseAsync(bool isActive = true)
    {
        var request = new WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, isActive);
        return (await Admin.Warehouses.CreateAsync(request)).Data<WarehouseDto>();
    }

    // ---------- documents ----------

    public async Task<DocumentDto> DraftReceiptAsync(ApiClient by, int warehouseId, params Line[] lines)
    {
        var request = new DocumentBuilder(warehouseId).WithPartner(Reference.SupplierId).WithLines(lines).Build();
        return (await by.Documents.CreateReceiptAsync(request)).Data<DocumentDto>();
    }

    public async Task<DocumentDto> DraftIssueAsync(ApiClient by, int warehouseId, params Line[] lines)
    {
        var request = new DocumentBuilder(warehouseId).WithPartner(Reference.CustomerId).WithLines(lines).Build();
        return (await by.Documents.CreateIssueAsync(request)).Data<DocumentDto>();
    }

    public async Task<DocumentDto> SubmitAsync(ApiClient by, DocumentDto draft) => (await by.Documents.SubmitAsync(draft.Id)).Data<DocumentDto>();

    public async Task<DocumentDto> GetAsync(int documentId) => (await Admin.Documents.GetAsync(documentId)).Data<DocumentDto>();

    /// <summary>
    /// The reviewer a submitted document is routed to (PRD 3.1) who is not its submitter: the Owner for a large document, otherwise a Supervisor,
    /// or the Admin when the supervisor submitted it.
    /// </summary>
    public ApiClient ApproverFor(DocumentDto submitted) =>
        submitted.IsLarge ? Owner : submitted.SubmittedBy == TestUsers.UsernameOf(Roles.Supervisor) ? Admin : Supervisor;

    /// <summary>Approves if the submitted document needs it (by its reviewer, who is never the submitter here, giving the override reason if there is one), then posts as the admin.</summary>
    public async Task<DocumentDto> CompleteAsync(DocumentDto submitted, string? negativeStockOverrideReason = null)
    {
        if (submitted.RequiresApproval) (await ApproverFor(submitted).Documents.ApproveWithReasonAsync(submitted.Id, negativeStockOverrideReason)).ShouldBeOk();
        return (await Admin.Documents.PostAsync(submitted.Id)).Data<DocumentDto>();
    }

    public async Task<DocumentDto> ReceiveAsync(int warehouseId, params Line[] lines)
    {
        var draft = await DraftReceiptAsync(Admin, warehouseId, lines);
        return await CompleteAsync(await SubmitAsync(Admin, draft));
    }

    public async Task<DocumentDto> IssueAsync(int warehouseId, params Line[] lines)
    {
        var draft = await DraftIssueAsync(Admin, warehouseId, lines);
        return await CompleteAsync(await SubmitAsync(Admin, draft));
    }

    // ---------- stock ----------

    public async Task<StockRow?> BalanceAsync(int productId, int warehouseId)
    {
        var page = (await Admin.Stock.QueryAsync(productId, warehouseId)).Data<PagedResult<StockRow>>();
        return page.Items.SingleOrDefault();
    }

    public async Task<decimal> OnHandAsync(int productId, int warehouseId) => (await BalanceAsync(productId, warehouseId))?.QtyOnHand ?? 0m;

    public async Task<List<LedgerRow>> LedgerAsync(int? productId = null, int? warehouseId = null, int? sourceDocId = null)
    {
        var rows = new List<LedgerRow>();
        for (var page = 1; ; page++)
        {
            var result = (await Admin.Stock.LedgerAsync(productId, warehouseId, sourceDocId, page)).Data<PagedResult<LedgerRow>>();
            rows.AddRange(result.Items);
            if (rows.Count >= result.Total || result.Items.Count == 0) return rows;
        }
    }

    public async Task<decimal> LedgerSumAsync(int productId, int warehouseId) =>
        (await LedgerAsync(productId, warehouseId)).Sum(r => r.QtyDelta);
}
