using Ims.Api.Tests.Builders;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Modules;
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
    private static readonly HttpMethod Delete = HttpMethod.Delete;

    public static readonly IReadOnlyList<EndpointCase> All =
    [
        // ----- reads -----
        new("GET users", Get, Permissions.UsersRead, _ => "/api/users"),
        new("GET users assignable-roles", Get, Permissions.UsersManage, _ => "/api/users/assignable-roles"),
        new("GET roles", Get, Permissions.RolesRead, _ => "/api/roles"),
        new("GET role", Get, Permissions.RolesRead, _ => $"/api/roles/{MissingPublicId}", OkWhenAllowed: false),
        new("GET permissions", Get, Permissions.RolesRead, _ => "/api/permissions"),
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
        new("POST roles", Post, Permissions.RolesManage, _ => "/api/roles", _ => new Ims.Application.Features.Roles.RoleCreateRequest(Unique.Name("Role"))),
        new("POST products", Post, Permissions.MasterDataManage, _ => "/api/products", r => new ProductBuilder(r).Build()),
        new("POST categories", Post, Permissions.MasterDataManage, _ => "/api/categories", _ => new CategorySaveRequest(Unique.Name("Cat"))),
        new("POST units", Post, Permissions.MasterDataManage, _ => "/api/units", _ => new UomSaveRequest(Unique.Code("U"), "Test unit")),
        new("POST warehouses", Post, Permissions.MasterDataManage, _ => "/api/warehouses",
            _ => new WarehouseSaveRequest(Unique.Code("W"), Unique.Name("Warehouse"), null, null, true)),
        new("POST partners", Post, Permissions.PartnersCreate, _ => "/api/partners",
            _ => new PartnerSaveRequest(PartnerType.Supplier, Unique.Name("Partner"), null, true)),
        new("PUT settings", Put, Permissions.SettingsManage, _ => "/api/settings", _ => new SettingsDto(false, false, 24)),
        // Switching a module off that is already off changes nothing, so the matrix leaves the installation as it found it.
        new("PUT modules/{code}", Put, Permissions.ModulesManage, _ => $"/api/modules/{ModuleCodes.Accounting}", _ => new ModuleSwitchRequest(false, "Permission matrix check")),
        new("POST approval-rules", Post, Permissions.SettingsManage, _ => "/api/approval-rules",
            _ => new ApprovalRuleSaveRequest(DocumentType.Issue, 1m, null, IsActive: false, ApproverTier: ApproverTier.Standard)),
        new("POST receipt", Post, Permissions.DocumentCreate, _ => "/api/documents/receipts",
            r => new DocumentBuilder(r.DefaultWarehouseId).WithPartner(r.SupplierId).WithLines(new Line(r.DemoProductId, 1, 1m)).Build()),
        new("POST issue", Post, Permissions.DocumentCreate, _ => "/api/documents/issues",
            r => new DocumentBuilder(r.DefaultWarehouseId).WithPartner(r.CustomerId).WithLines(new Line(r.DemoProductId, 1)).Build()),

        // ----- writes whose result depends on data: only "access was not denied" is checked -----
        new("PUT users/{id}", Put, Permissions.UsersManage, _ => $"/api/users/{MissingPublicId}",
            r => new UserSaveRequest("ghost", "ghost@example.test", "Ghost", Roles.Clerk, true, null, [r.DefaultWarehouseId]), OkWhenAllowed: false),
        new("PUT roles/{id}", Put, Permissions.RolesManage, _ => $"/api/roles/{MissingPublicId}", _ => new Ims.Application.Features.Roles.RoleUpdateRequest("Role x", null, true), OkWhenAllowed: false),
        new("PUT roles/{id}/permissions", Put, Permissions.RolesManage, _ => $"/api/roles/{MissingPublicId}/permissions", _ => new Ims.Application.Features.Roles.RolePermissionsRequest([]), OkWhenAllowed: false),
        new("DELETE roles/{id}", Delete, Permissions.RolesManage, _ => $"/api/roles/{MissingPublicId}", OkWhenAllowed: false),
        new("PUT products/{id}", Put, Permissions.MasterDataManage, _ => $"/api/products/{MissingPublicId}", r => new ProductBuilder(r).Build(), OkWhenAllowed: false),
        new("PUT categories/{id}", Put, Permissions.MasterDataManage, _ => $"/api/categories/{MissingPublicId}", _ => new CategorySaveRequest("x"), OkWhenAllowed: false),
        new("PUT units/{id}", Put, Permissions.MasterDataManage, _ => $"/api/units/{MissingPublicId}", _ => new UomSaveRequest("X", "x"), OkWhenAllowed: false),
        new("PUT warehouses/{id}", Put, Permissions.MasterDataManage, r => $"/api/warehouses/{r.DefaultWarehouseId}",
            r => new WarehouseSaveRequest(r.DefaultWarehouseCode, "Main warehouse", null, null, true)), // unchanged values: harmless, and inside every scoped supervisor's scope
        new("PUT partners/{id}", Put, Permissions.MasterDataManage, _ => $"/api/partners/{MissingPublicId}",
            _ => new PartnerSaveRequest(PartnerType.Supplier, "x", null, true), OkWhenAllowed: false),
        new("PUT approval-rules/{id}", Put, Permissions.SettingsManage, _ => $"/api/approval-rules/{MissingPublicId}",
            _ => new ApprovalRuleSaveRequest(DocumentType.Issue, 1m, null, false, ApproverTier.Standard), OkWhenAllowed: false),
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

    /// <summary>
    /// Endpoints of a module. While the module is off they answer 404 to everyone, so they stay out of <see cref="All"/> (the matrices that run every role against it assume the core is
    /// what answers) and are tested with the module on (the purchasing tests). <c>EndpointCoverageTests</c> still counts them, so one cannot be added without a row here.
    /// </summary>
    public static readonly IReadOnlyList<EndpointCase> Gated =
    [
        new("GET purchase-orders", Get, Permissions.PoRead, _ => "/api/purchase-orders"),
        new("GET purchase-orders/suggestions", Get, Permissions.PoRead, _ => $"/api/purchase-orders/suggestions?warehouseId={MissingPublicId}", OkWhenAllowed: false),
        new("GET purchase-orders/{id}", Get, Permissions.PoRead, _ => $"/api/purchase-orders/{MissingPublicId}", OkWhenAllowed: false),
        new("POST purchase-orders", Post, Permissions.PoManage, _ => "/api/purchase-orders", OkWhenAllowed: false),
        new("PUT purchase-orders/{id}", Put, Permissions.PoManage, _ => $"/api/purchase-orders/{MissingPublicId}", OkWhenAllowed: false),
        new("POST purchase-orders/{id}/submit", Post, Permissions.PoManage, _ => $"/api/purchase-orders/{MissingPublicId}/submit", OkWhenAllowed: false),
        new("POST purchase-orders/{id}/approve", Post, AuthPolicies.DocumentReview, _ => $"/api/purchase-orders/{MissingPublicId}/approve", OkWhenAllowed: false),
        new("POST purchase-orders/{id}/reject", Post, AuthPolicies.DocumentReview, _ => $"/api/purchase-orders/{MissingPublicId}/reject", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("POST purchase-orders/{id}/recall", Post, Permissions.PoManage, _ => $"/api/purchase-orders/{MissingPublicId}/recall", OkWhenAllowed: false),
        new("POST purchase-orders/{id}/cancel", Post, AuthPolicies.PurchaseOrderCancel, _ => $"/api/purchase-orders/{MissingPublicId}/cancel", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("POST purchase-orders/{id}/close", Post, Permissions.PoManage, _ => $"/api/purchase-orders/{MissingPublicId}/close", _ => new { reason = "x" }, OkWhenAllowed: false),
        new("POST purchase-orders/{id}/receipt", Post, Permissions.DocumentCreate, _ => $"/api/purchase-orders/{MissingPublicId}/receipt", OkWhenAllowed: false),
    ];

    public static EndpointCase Named(string name) => All.Single(c => c.Name == name);
}
