using Ims.Api.Tests.Builders;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Users;
using Ims.Application.Features.Warehouses;
using Ims.Domain;

namespace Ims.Api.Tests.Support;

/// <summary>Every business endpoint with the permission (or the any-of policy of <see cref="AuthPolicies"/>) that guards it. Adding an endpoint without adding it here is caught by the coverage test.</summary>
public static class EndpointCatalog
{
    private static readonly Guid MissingPublicId = new("5e8c1a4e-0000-4000-8000-000000000999"); // the entities addressed by a public id: well formed, names no row
    private static readonly HttpMethod Get = HttpMethod.Get;
    private static readonly HttpMethod Post = HttpMethod.Post;
    private static readonly HttpMethod Put = HttpMethod.Put;

    public static readonly IReadOnlyList<EndpointCase> All =
    [
        // ----- reads -----
        new("GET users", Get, Permissions.UsersRead, _ => "/api/users"),
        new("GET products", Get, Permissions.MasterDataRead, _ => "/api/products"),
        new("GET product", Get, Permissions.MasterDataRead, r => $"/api/products/{r.DemoProductId}"),
        new("GET product lookup", Get, Permissions.MasterDataRead, _ => "/api/products/lookup?code=9556000000011"),
        new("GET categories", Get, Permissions.MasterDataRead, _ => "/api/categories"),
        new("GET units", Get, Permissions.MasterDataRead, _ => "/api/units"),
        new("GET warehouses", Get, Permissions.MasterDataRead, _ => "/api/warehouses"),
        new("GET partners", Get, Permissions.MasterDataRead, _ => "/api/partners"),
        new("GET settings", Get, Permissions.MasterDataRead, _ => "/api/settings"),
        new("GET approval-rules", Get, Permissions.MasterDataRead, _ => "/api/approval-rules"),
        new("GET documents", Get, Permissions.StockRead, _ => "/api/documents"),
        new("GET documents counts", Get, Permissions.StockRead, _ => "/api/documents/counts"),
        new("GET stock", Get, Permissions.StockRead, _ => "/api/stock"),
        new("GET stock ledger", Get, Permissions.StockRead, _ => "/api/stock/ledger"),
        new("GET stock low-stock", Get, Permissions.StockRead, _ => "/api/stock/low-stock"),
        new("GET stock summary", Get, Permissions.StockRead, _ => "/api/stock/summary"),
        new("GET reports movements", Get, Permissions.ReportsRead, _ => "/api/reports/movements"),
        new("GET reports stock-summary export", Get, Permissions.ReportsRead, _ => "/api/reports/stock-summary/export"),
        new("GET reports movements export", Get, Permissions.ReportsRead, _ => "/api/reports/movements/export"),
        new("GET reports ledger export", Get, Permissions.ReportsRead, _ => "/api/reports/ledger/export"),
        new("GET reports low-stock export", Get, Permissions.ReportsRead, _ => "/api/reports/low-stock/export"),
        new("GET audit", Get, Permissions.AuditRead, _ => "/api/audit"),
        new("GET audit filters", Get, Permissions.AuditRead, _ => "/api/audit/filters"),
        new("GET import template", Get, Permissions.MasterDataManage, _ => "/api/import/templates/products"),

        // ----- writes that can succeed with a valid body -----
        new("POST users", Post, Permissions.UsersManage, _ => "/api/users", r => new UserSaveRequest(
            Unique.Username("rbac"), Unique.Email(Unique.Username("rbac")), "Rbac user", Roles.Clerk, true, "Rbac-Passw0rd!", [r.DefaultWarehouseId])),
        new("POST products", Post, Permissions.MasterDataManage, _ => "/api/products", r => new ProductBuilder(r).Build()),
        new("POST categories", Post, Permissions.MasterDataManage, _ => "/api/categories", _ => new CategorySaveRequest(Unique.Name("Cat"))),
        new("POST units", Post, Permissions.MasterDataManage, _ => "/api/units", _ => new UomSaveRequest(Unique.Code("U"), "Test unit")),
        new("POST warehouses", Post, Permissions.MasterDataManage, _ => "/api/warehouses",
            _ => new WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, true)),
        new("POST partners", Post, Permissions.PartnersCreate, _ => "/api/partners",
            _ => new PartnerSaveRequest(PartnerType.Supplier, Unique.Name("Partner"), null, true)),
        new("PUT settings", Put, Permissions.SettingsManage, _ => "/api/settings", _ => new SettingsDto(false, false, 24)),
        new("POST approval-rules", Post, Permissions.SettingsManage, _ => "/api/approval-rules",
            _ => new ApprovalRuleSaveRequest(DocumentType.Issue, 1m, null, Roles.Supervisor, IsActive: false)),
        new("POST receipt", Post, Permissions.DocumentCreate, _ => "/api/documents/receipts",
            r => new DocumentBuilder(r.DefaultWarehouseId).WithPartner(r.SupplierId).WithLines(new Line(r.DemoProductId, 1, 1m)).Build()),
        new("POST issue", Post, Permissions.DocumentCreate, _ => "/api/documents/issues",
            r => new DocumentBuilder(r.DefaultWarehouseId).WithPartner(r.CustomerId).WithLines(new Line(r.DemoProductId, 1)).Build()),

        // ----- writes whose result depends on data: only "access was not denied" is checked -----
        new("PUT users/{id}", Put, Permissions.UsersManage, _ => $"/api/users/{MissingPublicId}",
            r => new UserSaveRequest("ghost", "ghost@example.test", "Ghost", Roles.Clerk, true, null, [r.DefaultWarehouseId]), OkWhenAllowed: false),
        new("PUT products/{id}", Put, Permissions.MasterDataManage, _ => $"/api/products/{MissingPublicId}", r => new ProductBuilder(r).Build(), OkWhenAllowed: false),
        new("PUT categories/{id}", Put, Permissions.MasterDataManage, _ => $"/api/categories/{MissingPublicId}", _ => new CategorySaveRequest("x"), OkWhenAllowed: false),
        new("PUT units/{id}", Put, Permissions.MasterDataManage, _ => $"/api/units/{MissingPublicId}", _ => new UomSaveRequest("X", "x"), OkWhenAllowed: false),
        new("PUT warehouses/{id}", Put, Permissions.MasterDataManage, r => $"/api/warehouses/{r.DefaultWarehouseId}",
            r => new WarehouseSaveRequest(r.DefaultWarehouseCode, "Main warehouse", null, null, true)), // unchanged values: harmless, and inside every scoped supervisor's scope
        new("PUT partners/{id}", Put, Permissions.MasterDataManage, _ => $"/api/partners/{MissingPublicId}",
            _ => new PartnerSaveRequest(PartnerType.Supplier, "x", null, true), OkWhenAllowed: false),
        new("PUT approval-rules/{id}", Put, Permissions.SettingsManage, _ => $"/api/approval-rules/{MissingPublicId}",
            _ => new ApprovalRuleSaveRequest(DocumentType.Issue, 1m, null, Roles.Supervisor, false), OkWhenAllowed: false),
        new("PUT documents/{id}", Put, Permissions.DocumentCreate, _ => $"/api/documents/{MissingPublicId}",
            r => new DocumentBuilder(r.DefaultWarehouseId).WithLines(new Line(r.DemoProductId, 1)).Build(), OkWhenAllowed: false),
        new("POST documents/{id}/submit", Post, Permissions.DocumentCreate, _ => $"/api/documents/{MissingPublicId}/submit", OkWhenAllowed: false),
        new("POST documents/{id}/recall", Post, Permissions.DocumentCreate, _ => $"/api/documents/{MissingPublicId}/recall", OkWhenAllowed: false),
        new("POST documents/{id}/cancel", Post, AuthPolicies.DocumentCancel, _ => $"/api/documents/{MissingPublicId}/cancel", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("POST documents/{id}/approve", Post, AuthPolicies.DocumentReview, _ => $"/api/documents/{MissingPublicId}/approve", OkWhenAllowed: false),
        new("POST documents/{id}/reject", Post, AuthPolicies.DocumentReview, _ => $"/api/documents/{MissingPublicId}/reject", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("POST documents/{id}/post", Post, Permissions.DocumentPost, _ => $"/api/documents/{MissingPublicId}/post", _ => new { }, OkWhenAllowed: false),
        new("POST documents/{id}/reverse", Post, Permissions.DocumentReverse, _ => $"/api/documents/{MissingPublicId}/reverse", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("GET documents/{id}", Get, Permissions.StockRead, _ => $"/api/documents/{MissingPublicId}", OkWhenAllowed: false),
        new("POST import products", Post, Permissions.MasterDataManage, _ => "/api/import/products", OkWhenAllowed: false),
        new("POST import opening-stock", Post, Permissions.MasterDataManage, _ => "/api/import/opening-stock", OkWhenAllowed: false),
    ];

    public static EndpointCase Named(string name) => All.Single(c => c.Name == name);
}
