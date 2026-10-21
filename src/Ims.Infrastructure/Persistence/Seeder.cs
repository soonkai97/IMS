using Ims.Application;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Idempotent seed: reference data, default settings and rules, the first admin, and (Development only) one demo user per
/// role plus a few products. Passwords come from configuration (Seed:AdminPassword / Seed:DemoPassword), never from code.
/// </summary>
public static class Seeder
{
    public static async Task SeedAsync(IServiceProvider services, bool development, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(Seeder));

        await db.Database.MigrateAsync(ct);
        await SeedReferenceDataAsync(db, ct);
        await SeedAdminAsync(db, hasher, cfg[ConfigKeys.SeedAdminPassword], log, ct);
        if (development) await SeedDemoAsync(db, hasher, cfg[ConfigKeys.SeedDemoPassword], ct);
    }

    private static async Task SeedReferenceDataAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var (code, name) in SeedDefaults.Units)
            if (!await db.Set<Uom>().AnyAsync(u => u.Code == code, ct)) db.Add(Uom.Create(code, name));
        if (!await db.Set<Category>().AnyAsync(ct)) db.Add(Category.Create(SeedDefaults.DefaultCategory));
        if (!await db.Set<Warehouse>().AnyAsync(ct))
            db.Add(Warehouse.Create(SeedDefaults.DefaultWarehouseCode, SeedDefaults.DefaultWarehouseName));

        var defaults = new Dictionary<string, string>
        {
            [SettingKeys.AllowNegativeStock] = bool.FalseString.ToLowerInvariant(),
            [SettingKeys.AllowSelfApproval] = bool.FalseString.ToLowerInvariant(),
            [SettingKeys.LowStockAlertCooldownHours] = Limits.DefaultLowStockCooldownHours.ToString()
        };
        foreach (var (key, value) in defaults)
            if (!await db.Set<SystemSetting>().AnyAsync(s => s.Key == key, ct)) db.Add(SystemSetting.Create(key, value));

        if (!await db.Set<ApprovalRule>().AnyAsync(ct))
            db.Add(ApprovalRule.Create(DocumentType.Issue, SeedDefaults.DefaultApprovalAmount, null, ApproverTier.Standard));
        // The two purchase-order tiers, once per database (a rule is never deleted, only switched off, so an Administrator who turned them off is not overruled). They sit unused and hidden until the purchasing module is on.
        if (!await db.Set<ApprovalRule>().AnyAsync(r => r.DocType == DocumentType.PurchaseOrder, ct))
        {
            db.Add(ApprovalRule.Create(DocumentType.PurchaseOrder, SeedDefaults.PurchaseOrderStandardAmount, null, ApproverTier.Standard));
            db.Add(ApprovalRule.Create(DocumentType.PurchaseOrder, SeedDefaults.PurchaseOrderLargeAmount, null, ApproverTier.Large));
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdminAsync(AppDbContext db, IPasswordHasher hasher, string? password, ILogger log, CancellationToken ct)
    {
        if (await db.Set<User>().AnyAsync(u => u.Role.Code == SystemRoleCodes.Admin, ct)) return;
        if (string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("No admin user exists and {Key} is not set. Set it to create the first admin.", ConfigKeys.SeedAdminPassword);
            return;
        }
        var admin = await db.Set<Role>().SingleAsync(r => r.Code == SystemRoleCodes.Admin, ct); // the migration seeds the system roles
        db.Add(User.Create(SeedDefaults.AdminUsername, SeedDefaults.AdminEmail, SeedDefaults.AdminDisplayName, admin, hasher.Hash(password), allWarehouses: true));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDemoAsync(AppDbContext db, IPasswordHasher hasher, string? password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(password) || await db.Set<User>().AnyAsync(u => u.Username == SeedDefaults.SupervisorUsername, ct)) return;

        var warehouseId = await db.Set<Warehouse>().Select(w => w.Id).FirstAsync(ct);
        var roles = await db.Set<Role>().ToDictionaryAsync(r => r.Code, ct);
        foreach (var (username, roleCode, allWarehouses) in SeedDefaults.DemoUsers)
        {
            var role = roles[roleCode];
            var displayName = char.ToUpperInvariant(username[0]) + username[1..];
            db.Add(User.Create(username, $"{username}@{SeedDefaults.DemoEmailDomain}", displayName, role, hasher.Hash(password), allWarehouses ? [] : [warehouseId], allWarehouses: allWarehouses));
        }

        if (!await db.Set<Product>().AnyAsync(ct))
        {
            var unit = await db.Set<Uom>().FirstAsync(u => u.Code == SeedDefaults.DefaultUnitCode, ct);
            var category = await db.Set<Category>().FirstAsync(ct);
            foreach (var (sku, name, safety, barcode) in SeedDefaults.DemoProducts)
            {
                var product = Product.Create(sku, name, category, unit, safety);
                product.SetBarcodes([barcode]);
                db.Add(product);
            }
            db.Add(Partner.Create(PartnerType.Supplier, SeedDefaults.DemoSupplier));
            db.Add(Partner.Create(PartnerType.Customer, SeedDefaults.DemoCustomer));
        }
        await db.SaveChangesAsync(ct);
    }
}
