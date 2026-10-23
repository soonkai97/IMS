using System.Text.Json;
using Ims.Application;
using System.Linq.Expressions;
using Ims.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Ims.Infrastructure.Persistence;

/// <summary>
/// EF Core mapping for the whole model. Application code never sees this type: it works through
/// <see cref="IRepository{T}"/> and <see cref="IUnitOfWork"/>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock, ICurrentUser? current = null) : DbContext(options)
{
    /// <summary>All instants are UTC: every DateTime (and DateTime?) property is read back with Kind = Utc.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configuration) =>
        configuration.Properties<DateTime>().HaveConversion<UtcKindConverter>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Username).HasMaxLength(Limits.UsernameMaxLength);
            e.Property(x => x.Email).HasMaxLength(Limits.EmailMaxLength);
            e.Property(x => x.DisplayName).HasMaxLength(Limits.ShortNameMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Warehouses).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Warehouses).UsePropertyAccessMode(PropertyAccessMode.Field);
            // The role as a row, the only record of it. Restrict: a role in use is never removed under its users.
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        // Code and name are unique among live roles (ConfigureAuditColumns filters every unique index on REC_DELETED).
        b.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Code).HasMaxLength(Limits.RoleMaxLength);
            e.Property(x => x.Name).HasMaxLength(Limits.RoleNameMaxLength);
            e.Property(x => x.Description).HasMaxLength(Limits.RoleDescriptionMaxLength);
            e.HasMany(x => x.Grants).WithOne().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Grants).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<RolePermission>(e =>
        {
            e.HasIndex(x => new { x.RoleId, x.PermissionCode }).IsUnique();
            e.Property(x => x.PermissionCode).HasMaxLength(Limits.PermissionCodeMaxLength);
        });
        b.Entity<UserWarehouse>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.WarehouseId }).IsUnique();
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RefreshToken>(e =>
        {
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.TokenHash).HasMaxLength(Limits.TokenHashMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Category>(e => { e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(Limits.ShortNameMaxLength); });
        b.Entity<Uom>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(Limits.CodeMaxLength);
            e.Property(x => x.Name).HasMaxLength(Limits.UomNameMaxLength);
        });
        b.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Sku).IsUnique();
            // ORDER BY Name (product list) reads this narrow index in order instead of sorting the whole table; Sku and IsActive ride along for the search scan.
            e.HasIndex(x => x.Name).IncludeProperties(x => new { x.Sku, x.IsActive }).HasFilter(LiveRows);
            e.Property(x => x.Sku).HasMaxLength(Limits.SkuMaxLength);
            e.Property(x => x.Name).HasMaxLength(Limits.NameMaxLength);
            e.Property(x => x.SafetyStock).HasColumnType(SqlTypes.Quantity);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BaseUom).WithMany().HasForeignKey(x => x.BaseUomId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Barcodes).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Barcodes).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasMany(x => x.Conversions).WithOne().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Conversions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<ProductBarcode>(e => { e.HasIndex(x => x.Barcode).IsUnique(); e.Property(x => x.Barcode).HasMaxLength(Limits.BarcodeMaxLength); });
        b.Entity<UomConversion>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.FromUomId, x.ToUomId }).IsUnique();
            e.Property(x => x.Factor).HasColumnType(SqlTypes.Factor);
            e.HasOne(x => x.FromUom).WithMany().HasForeignKey(x => x.FromUomId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUom).WithMany().HasForeignKey(x => x.ToUomId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Warehouse>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(Limits.CodeMaxLength);
            e.Property(x => x.Name).HasMaxLength(Limits.ShortNameMaxLength);
        });
        b.Entity<Partner>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Limits.NameMaxLength);
            // One live supplier or customer per name (case-insensitive under the database collation); also serves the name prefix search.
            e.HasIndex(x => new { x.Type, x.Name }).IsUnique();
        });
        b.Entity<ApprovalRule>(e =>
        {
            e.Property(x => x.AmountThreshold).HasColumnType(SqlTypes.Money);
            e.Property(x => x.QtyThreshold).HasColumnType(SqlTypes.Quantity);
        });
        b.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(Limits.SettingKeyMaxLength);
            e.Property(x => x.Value).HasMaxLength(Limits.SettingValueMaxLength);
        });
        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.At);
            // Audit page filtered by user, newest first: a seek instead of a backwards scan of the whole table for a rare actor.
            e.HasIndex(x => new { x.Actor, x.Id }).IsDescending(false, true);
            // Find by document number or entity id (prefix), and by action prefix, newest first: seeks, not scans of a table that only grows.
            e.HasIndex(x => new { x.EntityId, x.Id }).IsDescending(false, true);
            e.HasIndex(x => new { x.Action, x.Id }).IsDescending(false, true);
            e.Property(x => x.Actor).HasMaxLength(Limits.AuditTextMaxLength);
            e.Property(x => x.Action).HasMaxLength(Limits.AuditTextMaxLength);
            e.Property(x => x.EntityType).HasMaxLength(Limits.AuditTextMaxLength);
            e.Property(x => x.EntityId).HasMaxLength(Limits.AuditTextMaxLength);
        });
        b.Entity<DocumentSequence>(e =>
        {
            e.HasKey(x => new { x.Prefix, x.YearMonth, x.WarehouseId });
            e.Property(x => x.Prefix).HasMaxLength(Limits.DocumentPrefixMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        b.Entity<StockDocument>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            // List filters are Status and/or Type, newest first (Id DESC); filtered on live rows so EF's REC_DELETED predicate is answered by the index itself.
            e.HasIndex(x => new { x.Status, x.Type, x.Id }).IsDescending(false, false, true).IncludeProperties(x => x.WarehouseId).HasFilter(LiveRows);
            e.HasIndex(x => x.CreatedAt).HasFilter(LiveRows);
            // "Posted today" on the Home counts: a seek on the day's postings instead of a scan of every posted document.
            e.HasIndex(x => x.PostedAt).IncludeProperties(x => new { x.Status, x.WarehouseId }).HasFilter($"{LiveRows} AND [PostedAt] IS NOT NULL");
            // The reversal rows of the movements report, newest reversal first: a seek on the (few) reversed documents. Written only when a document is reversed.
            e.HasIndex(x => x.ReversedAt).IncludeProperties(x => new { x.Status, x.Type, x.WarehouseId, x.PartnerId, x.No })
                .HasFilter($"{LiveRows} AND [ReversedAt] IS NOT NULL");
            // No index for the document search box on purpose: its contains-match must read every row, so its cost is the CPU of the comparison (see EfDocumentTextMatcher),
            // and a narrow covering index (number, partner, remark, warehouse) only cut the logical reads 2.4 times for 86 MB and a wider write on 1M documents
            // (docs/architecture/data-and-performance-rules.md section 9).
            e.Property(x => x.No).HasMaxLength(Limits.DocumentNoMaxLength);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.TotalQty).Ignore(x => x.TotalAmount).Ignore(x => x.CanPost).Ignore(x => x.IsReturned).Ignore(x => x.ActionContext).Ignore(x => x.IsLinked);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedByUser).WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PostedByUser).WithMany().HasForeignKey(x => x.PostedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RejectedByUser).WithMany().HasForeignKey(x => x.RejectedBy).OnDelete(DeleteBehavior.Restrict);
            // Only reversed documents carry the column, so the index holds only those rows (the foreign key still finds them).
            e.HasIndex(x => x.ReversedBy).HasFilter("[ReversedBy] IS NOT NULL");
            e.HasOne(x => x.ReversedByUser).WithMany().HasForeignKey(x => x.ReversedBy).OnDelete(DeleteBehavior.Restrict);
            // Only receipts created from a purchase order carry the column (NULL on every other document), so the foreign-key index holds only those rows: it serves "the receipts of this
            // order" and the received-quantity sum of the order, and costs nothing on a document that is not linked. EF would otherwise make the default unfiltered index over every row.
            e.HasIndex(x => x.PurchaseOrderId).HasFilter("[PurchaseOrderId] IS NOT NULL");
            e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(x => x.Document).HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<StockDocumentLine>(e =>
        {
            e.Property(x => x.Qty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.BaseQty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.UnitCost).HasColumnType(SqlTypes.Cost);
            e.Property(x => x.AvgCostBefore).HasColumnType(SqlTypes.Cost);
            e.Property(x => x.AvgCostAfter).HasColumnType(SqlTypes.Cost);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.EnteredUnitCost).HasColumnType(SqlTypes.Money);
            e.Property(x => x.Amount).HasColumnType(SqlTypes.Money);
            // The movements report counts and pages lines through their document; the filtered index answers EF's REC_DELETED predicate without reading the table.
            // The unfiltered foreign-key index stays (declared here, or EF would drop it as redundant): a physical delete of a document must find its soft-deleted lines too.
            e.HasIndex(x => x.DocumentId, "IX_StockDocumentLine_DocumentId");
            e.HasIndex(x => x.DocumentId, "IX_StockDocumentLine_DocumentId_Live").HasFilter(LiveRows);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        });
        // Purchase orders (module purchasing): their own tables, never a StockDocument type (plan D2).
        b.Entity<PurchaseOrder>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            // The list by status, newest first (Id DESC), and the count of open orders that blocks switching the module off: one seek on the (few) live orders of a status.
            e.HasIndex(x => new { x.Status, x.Id }).IsDescending(false, true).IncludeProperties(x => x.WarehouseId).HasFilter(LiveRows);
            e.Property(x => x.No).HasMaxLength(Limits.DocumentNoMaxLength);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.RejectionReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.CancelReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.CloseReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Ignore(x => x.TotalAmount).Ignore(x => x.TotalQty).Ignore(x => x.IsReturned).Ignore(x => x.IsPlainDraft).Ignore(x => x.ActionContext);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            // The workflow columns are nullable and filled by later steps: their foreign-key indexes hold only the rows that have a value.
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SubmittedBy).HasFilter("[SubmittedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ApprovedBy).HasFilter("[ApprovedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RejectedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.RejectedBy).HasFilter("[RejectedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ClosedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ClosedBy).HasFilter("[ClosedBy] IS NOT NULL");
            e.HasMany(x => x.Lines).WithOne(x => x.PurchaseOrder).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            e.Property(x => x.Qty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.BaseQty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.UnitPrice).HasColumnType(SqlTypes.Money);
            e.Property(x => x.Amount).HasColumnType(SqlTypes.Money);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            // Not unique on (order, line number): a save replaces the lines, and the old rows are soft-deleted in the same statement batch that inserts the new ones.
            // The foreign-key index on the order stays unfiltered (a physical delete must find soft-deleted lines too); the product index is the foreign-key index of the product
            // and the covering index of the on-order sum (open lines of a product, by base quantity) of the reorder list.
            e.HasIndex(x => x.ProductId).IncludeProperties(x => new { x.PurchaseOrderId, x.BaseQty });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        });
        // Sales orders (module sales): their own tables, never a StockDocument type (plan D1). The workflow columns exist from the start so the workflow slice needs no migration.
        b.Entity<SalesOrder>(e =>
        {
            e.HasIndex(x => x.No).IsUnique();
            // The list by status, newest first (Id DESC), and the count of open orders that blocks switching the module off: one seek on the (few) live orders of a status.
            e.HasIndex(x => new { x.Status, x.Id }).IsDescending(false, true).IncludeProperties(x => x.WarehouseId).HasFilter(LiveRows);
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.No).HasMaxLength(Limits.DocumentNoMaxLength);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.RejectionReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.CancelReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Property(x => x.CloseReason).HasMaxLength(Limits.RemarkMaxLength);
            e.Ignore(x => x.TotalSellingAmount).Ignore(x => x.TotalQty);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            // The workflow columns are nullable and filled by later steps: their foreign-key indexes hold only the rows that have a value.
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SubmittedBy).HasFilter("[SubmittedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ApprovedBy).HasFilter("[ApprovedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RejectedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.RejectedBy).HasFilter("[RejectedBy] IS NOT NULL");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ClosedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ClosedBy).HasFilter("[ClosedBy] IS NOT NULL");
            e.HasMany(x => x.Lines).WithOne(x => x.SalesOrder).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<SalesOrderLine>(e =>
        {
            e.Property(x => x.Qty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.BaseQty).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.SellingPrice).HasColumnType(SqlTypes.Money);
            e.Property(x => x.SellingAmount).HasColumnType(SqlTypes.Money);
            e.Property(x => x.Remark).HasMaxLength(Limits.RemarkMaxLength);
            // As the purchase-order line: the order foreign-key index stays unfiltered (a physical delete must find soft-deleted lines); the product index covers the delivered sums of later slices.
            e.HasIndex(x => x.ProductId).IncludeProperties(x => new { x.SalesOrderId, x.BaseQty });
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockLedgerEntry>(e =>
        {
            e.Property(x => x.QtyDelta).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.BalanceAfter).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.UnitCost).HasColumnType(SqlTypes.Cost);
            e.HasIndex(x => new { x.ProductId, x.WarehouseId, x.OccurredAt });
            e.HasIndex(x => x.SourceDocId);
            e.HasIndex(x => x.OccurredAt);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceDocument).WithMany().HasForeignKey(x => x.SourceDocId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Creator).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(Limits.AttachmentNameMaxLength);
            e.Property(x => x.StoragePath).HasMaxLength(Limits.AttachmentPathMaxLength);
            e.Property(x => x.ContentType).HasMaxLength(Limits.ContentTypeMaxLength);
            e.Property(x => x.Sha256).HasMaxLength(Limits.Sha256HexLength).IsFixedLength().IsUnicode(false);
            e.HasIndex(x => x.StoragePath).IsUnique();
            // The list for one owner: live files only, in the order they were added.
            e.HasIndex(x => new { x.OwnerType, x.OwnerId }).HasFilter(LiveRows);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockBalance>(e =>
        {
            e.HasKey(x => new { x.ProductId, x.WarehouseId });
            e.Property(x => x.QtyOnHand).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.QtyInTransit).HasColumnType(SqlTypes.Quantity);
            e.Property(x => x.AvgCost).HasColumnType(SqlTypes.Cost);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });

        ConfigurePublicIds(b);
        ConfigureAuditColumns(b);
    }

    /// <summary>
    /// The public id of every <see cref="PublicEntity"/>: a column with a random default (so a raw INSERT stays valid; the application always supplies its own value) and
    /// a unique index <c>UX_&lt;Table&gt;_PublicId</c>. The index is NOT filtered by REC_DELETED, unlike the other unique indexes (<see cref="ConfigureAuditColumns"/> skips
    /// it): a soft-deleted row keeps its id, so an id is never handed out twice. It is a plain non-clustered index; the integer key stays the clustered one.
    /// </summary>
    private static void ConfigurePublicIds(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes().Where(t => typeof(PublicEntity).IsAssignableFrom(t.ClrType)).ToList())
        {
            var e = b.Entity(entityType.ClrType);
            e.Property(nameof(PublicEntity.PublicId)).HasDefaultValueSql(PublicIdColumn.DefaultSql);
            e.HasIndex(nameof(PublicEntity.PublicId)).IsUnique().HasDatabaseName(PublicIdColumn.IndexNameOf(entityType.GetTableName()!)).HasFillFactor(PublicIdColumn.FillFactor);
        }
    }

    private static bool IsPublicIdIndex(Microsoft.EntityFrameworkCore.Metadata.IReadOnlyIndex index) =>
        index.Properties is [{ Name: nameof(PublicEntity.PublicId) }];

    /// <summary>Filter of every index that only needs live rows: the global query filter adds this predicate to every query, so the index answers it for free.</summary>
    private const string LiveRows = "[REC_DELETED] = 0";

    /// <summary>The ledger and the audit log: they carry the audit columns but are never updated or deleted (triggers enforce it).</summary>
    private static readonly HashSet<Type> AppendOnly = [typeof(StockLedgerEntry), typeof(AuditLog)];

    /// <summary>
    /// Standard audit columns on EVERY table: REC_DELETED, REC_CREATOR, REC_CREDATE, REC_REVISOR, REC_REVDATE, ROW_VERSION.
    /// Soft delete: a global query filter hides REC_DELETED rows from every query, and unique indexes only constrain live rows
    /// (so a deleted barcode or SKU can be used again). The append-only tables are never deleted, so they get no filter.
    /// </summary>
    private static void ConfigureAuditColumns(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes().Where(t => typeof(AuditableEntity).IsAssignableFrom(t.ClrType)).ToList())
        {
            var e = b.Entity(entityType.ClrType);
            e.Property(nameof(AuditableEntity.RecDeleted)).HasColumnName("REC_DELETED").HasDefaultValue(false);
            e.Property(nameof(AuditableEntity.RecCreator)).HasColumnName("REC_CREATOR");
            e.Property(nameof(AuditableEntity.RecCreDate)).HasColumnName("REC_CREDATE").HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(nameof(AuditableEntity.RecRevisor)).HasColumnName("REC_REVISOR");
            e.Property(nameof(AuditableEntity.RecRevDate)).HasColumnName("REC_REVDATE");
            e.Property(nameof(AuditableEntity.RowVersion)).HasColumnName("ROW_VERSION").IsRowVersion();

            if (AppendOnly.Contains(entityType.ClrType)) continue;

            var row = Expression.Parameter(entityType.ClrType, "e");
            e.HasQueryFilter(Expression.Lambda(Expression.Not(Expression.Property(row, nameof(AuditableEntity.RecDeleted))), row));

            foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique && !IsPublicIdIndex(i)))
                index.SetFilter(string.IsNullOrEmpty(index.GetFilter()) ? LiveRows : $"({index.GetFilter()}) AND {LiveRows}");
        }
    }

    // ---------- Immutability + row-level audit ----------
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } // enum names, not numbers, in the audit details
    };

    /// <summary>
    /// Row-level audit applies to master data and security data; documents and stock are audited by explicit business events. Settings too: a save writes ONE
    /// <c>settings.updated</c> event with the old and the new values of all of them, not one row per changed key (PS-12).
    /// </summary>
    private static readonly HashSet<Type> Audited =
    [
        typeof(Product), typeof(ProductBarcode), typeof(Warehouse), typeof(Partner), typeof(Category), typeof(Uom),
        typeof(ApprovalRule), typeof(User), typeof(UomConversion), typeof(Role), typeof(RolePermission)
    ];

    /// <summary>Secrets and login-attempt counters stay out of the audit payload (logins have their own events).</summary>
    private static readonly string[] AuditSkippedProperties = [nameof(User.PasswordHash), nameof(User.FailedLoginCount), nameof(User.LockoutUntil), nameof(User.Email), nameof(User.DisplayName),
        nameof(AuditableEntity.RecDeleted), nameof(AuditableEntity.RecCreator), nameof(AuditableEntity.RecCreDate),
        nameof(AuditableEntity.RecRevisor), nameof(AuditableEntity.RecRevDate), nameof(AuditableEntity.RowVersion)];

    private DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <param name="Changed">Names of the properties that changed (all of them for an added row); values are read after the save so generated keys are included.</param>
    private sealed record Pending(EntityEntry Entry, EntityState State, string? Before, IReadOnlyList<string> Changed);

    /// <summary>
    /// Saves the change and its audit rows atomically (AUTH-03: an audit row cannot be lost). If the caller already opened a
    /// transaction (UnitOfWork.ExecuteAsync) the audit rows join it; otherwise one is opened here for the two saves.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ChangeTracker.DetectChanges(); // so children removed from a collection are seen as deleted before they are converted
        EnforceAppendOnly();
        StampAuditColumns();
        var pending = CollectAudit();
        if (pending.Count == 0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        await using var own = Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(ct) : null;
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        var actor = current is { IsAuthenticated: true } ? current.Username : AuditEntities.SystemActor;
        foreach (var p in pending)
        {
            var action = p.State switch { EntityState.Added => AuditActions.RowCreated, EntityState.Deleted => AuditActions.RowDeleted, _ => AuditActions.RowUpdated };
            AuditLogs.Add(AuditLog.Record(actor, action, p.Entry.Metadata.ClrType.Name, KeyOf(p.Entry), p.Before, After(p), clock.UtcNow));
        }
        StampAuditColumns(); // the audit rows themselves get their creator and date too
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);

        if (own is not null) await own.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Stamps REC_CREATOR/REC_CREDATE on inserts and REC_REVISOR/REC_REVDATE on changes, and turns a physical delete into a soft delete
    /// (REC_DELETED = 1); <see cref="IsSoftDelete"/> then tells the row-level audit that the row was deleted.
    /// </summary>
    private void StampAuditColumns()
    {
        int? userId = current is { IsAuthenticated: true } ? current.UserId : null;
        var now = clock.UtcNow;
        TouchDocumentsWithChangedLines();
        TouchPurchaseOrdersWithChangedLines();
        TouchSalesOrdersWithChangedLines();
        TouchRolesWithChangedGrants();

        foreach (var e in ChangeTracker.Entries<AuditableEntity>().ToList())
        {
            switch (e.State)
            {
                case EntityState.Added:
                    e.Property(nameof(AuditableEntity.RecCreator)).CurrentValue = userId;
                    e.Property(nameof(AuditableEntity.RecCreDate)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    e.Property(nameof(AuditableEntity.RecRevisor)).CurrentValue = userId;
                    e.Property(nameof(AuditableEntity.RecRevDate)).CurrentValue = now;
                    break;
                case EntityState.Deleted:
                    e.State = EntityState.Modified;
                    e.Property(nameof(AuditableEntity.RecDeleted)).CurrentValue = true;
                    e.Property(nameof(AuditableEntity.RecRevisor)).CurrentValue = userId;
                    e.Property(nameof(AuditableEntity.RecRevDate)).CurrentValue = now;
                    break;
            }
        }
    }

    /// <summary>
    /// True when the save deletes the row: REC_DELETED goes from false to true, whether the context turned a physical delete into it or the domain called
    /// <see cref="AuditableEntity.MarkDeleted"/>. Read from the entry itself: an <see cref="EntityEntry"/> is made again by every <c>Entries()</c> call, so a set of
    /// entries kept between the two calls never matches, and the deletion would be reported as an update with no audited property changed (which writes no row).
    /// </summary>
    private static bool IsSoftDelete(EntityEntry e) =>
        e.State == EntityState.Modified && e.Property(nameof(AuditableEntity.RecDeleted)) is { OriginalValue: false, CurrentValue: true };

    /// <summary>
    /// A document is its header plus its lines. When only lines are added, changed or removed the header row would stay untouched and keep
    /// its ROW_VERSION, so the concurrency token the API hands out (ETag) would not notice an edit. Marking the header as revised moves it.
    /// </summary>
    private void TouchDocumentsWithChangedLines()
    {
        var documentIds = ChangeTracker.Entries<StockDocumentLine>()
            .Where(l => l.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(l => l.Property(x => x.DocumentId).CurrentValue)
            .ToHashSet();
        if (documentIds.Count == 0) return;

        foreach (var document in ChangeTracker.Entries<StockDocument>().Where(d => d.State == EntityState.Unchanged && documentIds.Contains(d.Entity.Id)))
            document.Property(nameof(AuditableEntity.RecRevDate)).IsModified = true;
    }

    /// <summary>The same for a purchase order and its lines: an edit that only replaces lines must still move the order's ROW_VERSION, or its concurrency token would not notice it.</summary>
    private void TouchPurchaseOrdersWithChangedLines()
    {
        var orderIds = ChangeTracker.Entries<PurchaseOrderLine>()
            .Where(l => l.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(l => l.Property(x => x.PurchaseOrderId).CurrentValue)
            .ToHashSet();
        if (orderIds.Count == 0) return;

        foreach (var order in ChangeTracker.Entries<PurchaseOrder>().Where(o => o.State == EntityState.Unchanged && orderIds.Contains(o.Entity.Id)))
            order.Property(nameof(AuditableEntity.RecRevDate)).IsModified = true;
    }

    /// <summary>The same for a sales order and its lines: an edit that only replaces lines must still move the order's ROW_VERSION, or its concurrency token would not notice it.</summary>
    private void TouchSalesOrdersWithChangedLines()
    {
        var orderIds = ChangeTracker.Entries<SalesOrderLine>()
            .Where(l => l.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(l => l.Property(x => x.SalesOrderId).CurrentValue)
            .ToHashSet();
        if (orderIds.Count == 0) return;

        foreach (var order in ChangeTracker.Entries<SalesOrder>().Where(o => o.State == EntityState.Unchanged && orderIds.Contains(o.Entity.Id)))
            order.Property(nameof(AuditableEntity.RecRevDate)).IsModified = true;
    }

    /// <summary>
    /// A role is its row plus its grants. When only grants are added or revoked the role row would stay untouched and keep its ROW_VERSION, so the concurrency token the API hands out
    /// (ETag) would not notice the edit. Marking the role as revised moves it.
    /// </summary>
    private void TouchRolesWithChangedGrants()
    {
        var roleIds = ChangeTracker.Entries<RolePermission>()
            .Where(g => g.State is EntityState.Added or EntityState.Deleted || IsSoftDelete(g))
            .Select(g => g.Property(x => x.RoleId).CurrentValue)
            .ToHashSet();
        if (roleIds.Count == 0) return;

        foreach (var role in ChangeTracker.Entries<Role>().Where(r => r.State == EntityState.Unchanged && roleIds.Contains(r.Entity.Id)))
            role.Property(nameof(AuditableEntity.RecRevDate)).IsModified = true;
    }

    private void EnforceAppendOnly()
    {
        foreach (var e in ChangeTracker.Entries())
            if (e.Entity is StockLedgerEntry or AuditLog && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{e.Metadata.ClrType.Name} is append-only and cannot be modified or deleted.");
    }

    private List<Pending> CollectAudit()
    {
        var list = new List<Pending>();
        foreach (var e in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!Audited.Contains(e.Metadata.ClrType)) continue;
            var props = e.Properties.Where(p => !AuditSkippedProperties.Contains(p.Metadata.Name) && !p.Metadata.IsShadowProperty()).ToList();
            var state = IsSoftDelete(e) ? EntityState.Deleted : e.State;
            switch (state)
            {
                case EntityState.Added:
                    list.Add(new Pending(e, state, null, props.Select(p => p.Metadata.Name).ToList()));
                    break;
                case EntityState.Deleted:
                    list.Add(new Pending(e, state, JsonSerializer.Serialize(props.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue), Json), []));
                    break;
                default:
                    var changed = props.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).ToList();
                    if (changed.Count == 0) continue;
                    list.Add(new Pending(e, state, JsonSerializer.Serialize(changed.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue), Json),
                        changed.Select(p => p.Metadata.Name).ToList()));
                    break;
            }
        }
        return list;
    }

    private static string? After(Pending p) =>
        p.State == EntityState.Deleted
            ? null
            : JsonSerializer.Serialize(p.Changed.ToDictionary(name => name, name => p.Entry.Property(name).CurrentValue), Json);

    /// <summary>
    /// The row-audited entities that have a public id (every entry of <see cref="Audited"/> that derives from <see cref="PublicEntity"/>): their audit rows name them by
    /// <see cref="PublicEntity.PublicId"/>, the id the client has. The others in <see cref="Audited"/> are child rows (barcodes, unit conversions) and keep their integer key.
    /// StockDocument and Attachment are not row-audited at all: their business events name them explicitly (the attachment events by public id, the document events by the
    /// document NUMBER, what the audit search uses). Rows written before a flip keep the integer; the audit page and its entity-id filter are plain text and show and match both.
    /// </summary>
    private static readonly HashSet<Type> AuditedByPublicId =
    [
        typeof(Warehouse), typeof(Partner), typeof(Category), typeof(Uom), typeof(User), typeof(Product), typeof(ApprovalRule), typeof(Role)
    ];

    private static string KeyOf(EntityEntry e) =>
        e.Entity is PublicEntity row && AuditedByPublicId.Contains(e.Metadata.ClrType)
            ? row.PublicId.ToString()
            : string.Join(DocumentNumberFormat.Separator, e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString()));
}
