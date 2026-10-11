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
            e.Property(x => x.Role).HasMaxLength(Limits.RoleMaxLength);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasMany(x => x.Warehouses).WithOne().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Warehouses).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<UserWarehouse>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.WarehouseId }).IsUnique();
            e.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
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
            e.HasOne<Uom>().WithMany().HasForeignKey(x => x.FromUomId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Uom>().WithMany().HasForeignKey(x => x.ToUomId).OnDelete(DeleteBehavior.Restrict);
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
            e.Property(x => x.RoleRequired).HasMaxLength(Limits.RoleMaxLength);
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
            e.Ignore(x => x.TotalQty).Ignore(x => x.TotalAmount).Ignore(x => x.CanPost).Ignore(x => x.IsReturned).Ignore(x => x.ActionContext);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Partner).WithMany().HasForeignKey(x => x.PartnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubmittedByUser).WithMany().HasForeignKey(x => x.SubmittedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedByUser).WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PostedByUser).WithMany().HasForeignKey(x => x.PostedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RejectedByUser).WithMany().HasForeignKey(x => x.RejectedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReversedByUser).WithMany().HasForeignKey(x => x.ReversedBy).OnDelete(DeleteBehavior.Restrict);
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
            // The movements report counts and pages lines through their document; the filtered index answers EF's REC_DELETED predicate without reading the table.
            // The unfiltered foreign-key index stays (declared here, or EF would drop it as redundant): a physical delete of a document must find its soft-deleted lines too.
            e.HasIndex(x => x.DocumentId, "IX_StockDocumentLine_DocumentId");
            e.HasIndex(x => x.DocumentId, "IX_StockDocumentLine_DocumentId_Live").HasFilter(LiveRows);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Uom).WithMany().HasForeignKey(x => x.UomId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockLedgerEntry>(e =>
        {
            e.Property(x => x.QtyDelta).HasColumnType(SqlTypes.Quantity);
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

        ConfigureAuditColumns(b);
    }

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

            foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique))
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
        typeof(ApprovalRule), typeof(User), typeof(UomConversion)
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
        var softDeleted = StampAuditColumns();
        var pending = CollectAudit(softDeleted);
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
    /// (REC_DELETED = 1). Returns the entries that were soft-deleted, so the row-level audit still reports them as deletions.
    /// </summary>
    private HashSet<EntityEntry> StampAuditColumns()
    {
        int? userId = current is { IsAuthenticated: true } ? current.UserId : null;
        var now = clock.UtcNow;
        var softDeleted = new HashSet<EntityEntry>();
        TouchDocumentsWithChangedLines();

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
                    softDeleted.Add(e);
                    break;
            }
        }
        return softDeleted;
    }

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

    private void EnforceAppendOnly()
    {
        foreach (var e in ChangeTracker.Entries())
            if (e.Entity is StockLedgerEntry or AuditLog && e.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{e.Metadata.ClrType.Name} is append-only and cannot be modified or deleted.");
    }

    private List<Pending> CollectAudit(HashSet<EntityEntry> softDeleted)
    {
        var list = new List<Pending>();
        foreach (var e in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (!Audited.Contains(e.Metadata.ClrType)) continue;
            var props = e.Properties.Where(p => !AuditSkippedProperties.Contains(p.Metadata.Name) && !p.Metadata.IsShadowProperty()).ToList();
            var state = softDeleted.Contains(e) ? EntityState.Deleted : e.State;
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

    private static string KeyOf(EntityEntry e) =>
        string.Join(DocumentNumberFormat.Separator, e.Metadata.FindPrimaryKey()!.Properties.Select(p => e.Property(p.Name).CurrentValue?.ToString()));
}
