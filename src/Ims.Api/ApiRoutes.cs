namespace Ims.Api;

/// <summary>Presentation-layer constants: routes, headers, configuration keys, policy names, log templates.</summary>
public static class ApiRoutes
{
    public const string Prefix = "api";
    public const string Auth = Prefix + "/auth";
    public const string Users = Prefix + "/users";
    public const string Roles = Prefix + "/roles";
    public const string Permissions = Prefix + "/permissions";
    public const string Products = Prefix + "/products";
    public const string Categories = Prefix + "/categories";
    public const string Units = Prefix + "/units";
    public const string Warehouses = Prefix + "/warehouses";
    public const string Partners = Prefix + "/partners";
    public const string Settings = Prefix + "/settings";
    public const string Modules = Prefix + "/modules";
    public const string ApprovalRules = Prefix + "/approval-rules";
    public const string Documents = Prefix + "/documents";
    public const string PurchaseOrders = Prefix + "/purchase-orders";
    public const string Stock = Prefix + "/stock";
    public const string Reports = Prefix + "/reports";
    public const string Import = Prefix + "/import";
    public const string Audit = Prefix + "/audit";
    public const string Attachments = Prefix + "/attachments";
    public const string Health = "/health";

    /// <summary>The route of an entity addressed by its public id (a GUID): every addressable entity (the integer route is gone).</summary>
    public const string PublicId = "{id:guid}";
}
