using System.Globalization;
using System.Text.Json;
using Ims.Application;
using Ims.Application.Features.Accounting;
using Ims.Application.Features.Reports;

namespace Ims.Domain.Tests.Accounting;

/// <summary>
/// The accounting export without a database: the sign of every row, the date, the layout, the refusal above the row cap, the cost check that does not depend on the policy, the warehouse
/// rules and the audit event. The reader is a fake that returns what a database would; the SQL itself is tested against a real SQL Server in the API tests.
/// </summary>
public class ExportAccountingHandlerTests
{
    private static readonly Guid WarehouseId = Guid.NewGuid();
    private const int WarehouseKey = 7;
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 31);

    private sealed class Reader(IReadOnlyList<AccountingEntry> rows) : IAccountingReader
    {
        public int Calls { get; private set; }
        public int? Limit { get; private set; }
        public int? WarehouseKeyAsked { get; private set; }
        public IReadOnlyCollection<int>? ScopeAsked { get; private set; }

        public Task<IReadOnlyList<AccountingEntry>> ListAsync(DateOnly from, DateOnly to, int? warehouseId, IReadOnlyCollection<int>? warehouseScope, int limit, CancellationToken ct)
        {
            Calls++;
            Limit = limit;
            WarehouseKeyAsked = warehouseId;
            ScopeAsked = warehouseScope;
            return Task.FromResult<IReadOnlyList<AccountingEntry>>(rows.Take(limit).ToList());
        }
    }

    private sealed class Scope(IReadOnlyCollection<int>? ids) : IWarehouseScope
    {
        public IReadOnlyCollection<int>? Ids() => ids;
    }

    private sealed class Warehouses : IIdMap<Warehouse>
    {
        public Task<IReadOnlyDictionary<Guid, int>> ToInternalAsync(IEnumerable<Guid> publicIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(publicIds.Where(id => id == WarehouseId).ToDictionary(id => id, _ => WarehouseKey));

        public Task<IReadOnlyDictionary<int, Guid>> ToPublicAsync(IEnumerable<int> ids, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Writer : IReportWriter
    {
        public IReadOnlyList<ReportColumn>? Columns { get; private set; }
        public string? Sheet { get; private set; }
        public List<object?[]> Rows { get; } = [];

        public byte[] Write(string sheetName, IReadOnlyList<ReportColumn> columns, IEnumerable<object?[]> rows)
        {
            Sheet = sheetName;
            Columns = columns;
            Rows.AddRange(rows);
            return [1, 2, 3];
        }
    }

    private sealed class Caller(params string[] permissions) : ICallerPermissions
    {
        public bool Has(string permission) => permissions.Contains(permission);
    }

    private sealed class Audit : IAuditTrail
    {
        public List<(string Action, string Entity, string Id, string After)> Events { get; } = [];

        public void Record(string action, string entityType, string entityId, object? before = null, object? after = null, string? actor = null) =>
            Events.Add((action, entityType, entityId, JsonSerializer.Serialize(after)));
    }

    private sealed class Saver : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
        public Task<T> ExecuteAsync<T>(Func<Task<T>> work, CancellationToken ct = default) => work();
    }

    private sealed record Rig(ExportAccountingHandler Handler, Reader Reader, Writer Writer, Audit Audit, Saver Unit);

    private static Rig Build(IReadOnlyList<AccountingEntry> rows, IReadOnlyCollection<int>? scope = null, bool costView = true)
    {
        var reader = new Reader(rows);
        var writer = new Writer();
        var audit = new Audit();
        var unit = new Saver();
        var caller = costView ? new Caller(Permissions.AccountingExport, Permissions.CostView) : new Caller(Permissions.AccountingExport);
        return new Rig(new ExportAccountingHandler(reader, new Scope(scope), new Warehouses(), writer, caller, audit, unit), reader, writer, audit, unit);
    }

    private static Task<ExportFile> Export(Rig rig, Guid? warehouse = null) =>
        rig.Handler.HandleAsync(new ExportAccountingQuery(new AccountingExportFilter(From, To, warehouse)), CancellationToken.None);

    private static AccountingEntry Entry(DocumentType type = DocumentType.Receipt, bool reversal = false, decimal qty = 10m, decimal cost = 5m, decimal amount = 50m,
        DateTime? at = null, string doc = "RCV-WH01-2610-0001", string? partner = "Acme Sdn Bhd") => new()
    {
        At = at ?? new DateTime(2026, 10, 5, 3, 0, 0, DateTimeKind.Utc), DocumentId = 1, LineId = 1, DocNo = doc, Type = type,
        Purpose = type == DocumentType.Issue ? IssuePurpose.Sale : null, WarehouseCode = "WH01", Partner = partner, PartnerTin = "C1234567890", PartnerSstRegNo = "W10-1234-56789012",
        Sku = "SKU-1", ProductName = "Steel bolt", HsCode = "7318.15", SstCode = "ST-1", BaseUnit = "PCS", BaseQty = qty, UnitCost = cost, Amount = amount, IsReversal = reversal
    };

    private static int Column(string header) => Array.FindIndex(AccountingLayout.Columns, c => c.Header == header);

    // ----- the layout -----
    [Fact]
    public async Task The_file_has_the_fixed_columns_in_the_fixed_order_on_a_sheet_called_Accounting()
    {
        var rig = Build([Entry()]);

        var file = await Export(rig);

        Assert.Equal("Accounting", rig.Writer.Sheet);
        Assert.Equal(["Date", "Document no.", "Type", "Purpose", "Reversal of", "Warehouse", "Partner", "Partner TIN", "Partner SST reg. no.", "SKU", "Product", "HS code", "SST code",
            "Unit", "Quantity", "Unit cost", "Amount"], ReportHeaders.Names(rig.Writer.Columns!));
        Assert.All(rig.Writer.Rows, row => Assert.Equal(rig.Writer.Columns!.Count, row.Length));
        Assert.Equal("ims-accounting-2026-10-01-2026-10-31.xlsx", file.FileName);
        Assert.Equal(ContentTypes.Xlsx, file.ContentType);
    }

    [Fact]
    public void Only_the_unit_cost_and_the_amount_are_cost_and_they_need_cost_view()
    {
        var gated = AccountingLayout.Columns.Where(c => c.VisibleWith is not null).ToList();

        Assert.Equal(["Unit cost", "Amount"], gated.Select(c => c.Header));
        Assert.All(gated, c => Assert.Equal(Permissions.CostView, c.VisibleWith));
        Assert.Equal([4, 2], gated.Select(c => c.DecimalPlaces));
    }

    [Fact]
    public async Task A_posting_row_carries_every_value_and_a_reversal_row_names_the_document_it_undoes()
    {
        var rig = Build([Entry(), Entry(reversal: true)]);

        await Export(rig);

        var posting = rig.Writer.Rows[0];
        Assert.Equal([new DateOnly(2026, 10, 5), "RCV-WH01-2610-0001", "Receipt", null, null, "WH01", "Acme Sdn Bhd", "C1234567890", "W10-1234-56789012", "SKU-1", "Steel bolt", "7318.15",
            "ST-1", "PCS", 10m, 5m, 50m], posting);
        Assert.Equal("RCV-WH01-2610-0001", rig.Writer.Rows[1][Column("Reversal of")]);
    }

    // ----- sign and date -----
    [Theory]
    [InlineData(DocumentType.Receipt, false, 10, 50)]
    [InlineData(DocumentType.Receipt, true, -10, -50)]
    [InlineData(DocumentType.Issue, false, -10, -50)]
    [InlineData(DocumentType.Issue, true, 10, 50)]
    public async Task Quantity_and_amount_carry_the_sign_of_the_stock_effect(DocumentType type, bool reversal, int quantity, int amount)
    {
        var rig = Build([Entry(type, reversal)]);

        await Export(rig);

        var row = rig.Writer.Rows.Single();
        Assert.Equal((quantity, amount, 5m), ((decimal)row[Column("Quantity")]!, (decimal)row[Column("Amount")]!, (decimal)row[Column("Unit cost")]!)); // a price is never signed
    }

    [Fact]
    public async Task A_posting_and_its_reversal_add_up_to_nothing()
    {
        var rig = Build([Entry(), Entry(reversal: true), Entry(DocumentType.Issue, qty: 4m, amount: 20m), Entry(DocumentType.Issue, reversal: true, qty: 4m, amount: 20m)]);

        await Export(rig);

        Assert.Equal(0m, rig.Writer.Rows.Sum(r => (decimal)r[Column("Quantity")]!));
        Assert.Equal(0m, rig.Writer.Rows.Sum(r => (decimal)r[Column("Amount")]!));
    }

    [Fact]
    public async Task A_zero_amount_is_a_plain_zero_never_minus_zero()
    {
        var rig = Build([Entry(DocumentType.Issue, amount: 0m, cost: 0m)]);

        await Export(rig);

        var amount = (decimal)rig.Writer.Rows.Single()[Column("Amount")]!;
        Assert.False(decimal.IsNegative(amount));
    }

    [Theory]
    [InlineData("2026-09-30T16:00:00Z", "2026-10-01")] // midnight in Kuala Lumpur
    [InlineData("2026-09-30T15:59:59Z", "2026-09-30")]
    [InlineData("2026-10-31T15:59:59Z", "2026-10-31")]
    public async Task The_date_is_the_Kuala_Lumpur_day(string utc, string expected)
    {
        var rig = Build([Entry(at: DateTime.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))]);

        await Export(rig);

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), rig.Writer.Rows.Single()[Column("Date")]);
    }

    // ----- text -----
    [Fact]
    public async Task Text_a_person_typed_is_made_safe_but_numbers_and_the_date_are_not_touched()
    {
        var rig = Build([Entry(partner: "=cmd|' /C calc'!A0")]);

        await Export(rig);

        var row = rig.Writer.Rows.Single();
        Assert.Equal("'=cmd|' /C calc'!A0", row[Column("Partner")]);
        Assert.IsType<DateOnly>(row[Column("Date")]);
        Assert.IsType<decimal>(row[Column("Quantity")]);
    }

    [Fact]
    public async Task A_missing_tax_value_is_a_blank_cell_not_a_failure()
    {
        var entry = new AccountingEntry { DocNo = "RCV-1", Type = DocumentType.Receipt, WarehouseCode = "WH01", Sku = "S", ProductName = "P", BaseUnit = "PCS", BaseQty = 1m, UnitCost = 1m, Amount = 1m };
        var rig = Build([entry]);

        await Export(rig);

        var row = rig.Writer.Rows.Single();
        Assert.All(new[] { "Partner", "Partner TIN", "Partner SST reg. no.", "HS code", "SST code", "Purpose" }, column => Assert.Null(row[Column(column)]));
    }

    // ----- the cap -----
    [Fact]
    public async Task A_period_of_exactly_the_cap_is_written()
    {
        var rig = Build(Enumerable.Range(0, Limits.MaxExportRows).Select(_ => Entry()).ToList());

        await Export(rig);

        Assert.Equal(Limits.MaxExportRows, rig.Writer.Rows.Count);
        Assert.Equal(Limits.MaxExportRows + 1, rig.Reader.Limit); // it asks for one more than it can write, to know
    }

    [Fact]
    public async Task One_row_above_the_cap_is_refused_with_advice_and_nothing_is_written_or_audited()
    {
        var rig = Build(Enumerable.Range(0, Limits.MaxExportRows + 1).Select(_ => Entry()).ToList());

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Export(rig));

        Assert.Contains("Too many rows", refused.Message);
        Assert.Contains("Narrow the date range", refused.Message);
        Assert.Contains(Limits.MaxExportRows.ToString(CultureInfo.InvariantCulture), refused.Message);
        Assert.Empty(rig.Writer.Rows);
        Assert.Null(rig.Writer.Sheet);
        Assert.Empty(rig.Audit.Events);
        Assert.Equal(0, rig.Unit.Saves);
    }

    // ----- cost.view -----
    [Fact]
    public async Task A_caller_without_cost_view_is_refused_before_anything_is_read_even_with_the_export_permission()
    {
        var rig = Build([Entry()], costView: false);

        await Assert.ThrowsAsync<ForbiddenException>(() => Export(rig));

        Assert.Equal(0, rig.Reader.Calls);
        Assert.Empty(rig.Audit.Events);
    }

    // ----- warehouses -----
    [Fact]
    public async Task A_warehouse_that_does_not_exist_is_404()
    {
        var rig = Build([Entry()]);

        await Assert.ThrowsAsync<NotFoundException>(() => Export(rig, warehouse: Guid.NewGuid()));

        Assert.Equal(0, rig.Reader.Calls);
    }

    [Fact]
    public async Task A_warehouse_outside_the_callers_scope_is_the_same_404()
    {
        var rig = Build([Entry()], scope: [3, 4]);

        var refused = await Assert.ThrowsAsync<NotFoundException>(() => Export(rig, warehouse: WarehouseId));

        Assert.Equal(Messages.WarehouseNotFound, refused.Message);
        Assert.Equal(0, rig.Reader.Calls);
    }

    [Fact]
    public async Task A_warehouse_inside_the_scope_is_passed_to_the_query_with_the_scope()
    {
        var rig = Build([Entry()], scope: [WarehouseKey, 9]);

        await Export(rig, warehouse: WarehouseId);

        Assert.Equal(WarehouseKey, rig.Reader.WarehouseKeyAsked);
        Assert.Equal([WarehouseKey, 9], rig.Reader.ScopeAsked);
    }

    [Fact]
    public async Task Without_a_warehouse_the_scope_still_goes_to_the_query()
    {
        var rig = Build([Entry()], scope: [3]);

        await Export(rig);

        Assert.Null(rig.Reader.WarehouseKeyAsked);
        Assert.Equal([3], rig.Reader.ScopeAsked);
    }

    // ----- the audit event -----
    [Fact]
    public async Task One_audit_event_names_the_period_the_warehouse_and_the_row_count_and_never_the_content()
    {
        var rig = Build([Entry(), Entry(reversal: true)]);

        await Export(rig, warehouse: WarehouseId);

        var recorded = Assert.Single(rig.Audit.Events);
        Assert.Equal((AuditActions.AccountingExported, AuditEntities.AccountingExport, "ims-accounting-2026-10-01-2026-10-31.xlsx"), (recorded.Action, recorded.Entity, recorded.Id));
        using var after = JsonDocument.Parse(recorded.After);
        Assert.Equal("2026-10-01", after.RootElement.GetProperty("From").GetString());
        Assert.Equal("2026-10-31", after.RootElement.GetProperty("To").GetString());
        Assert.Equal(WarehouseId, after.RootElement.GetProperty("Warehouse").GetGuid());
        Assert.Equal(2, after.RootElement.GetProperty("Rows").GetInt32());
        Assert.DoesNotContain("Steel", recorded.After);
        Assert.DoesNotContain("Acme", recorded.After);
        Assert.DoesNotContain("C1234567890", recorded.After);
        Assert.Equal(1, rig.Unit.Saves);
    }

    // ----- the request -----
    [Theory]
    [InlineData(null, "2026-10-31")]
    [InlineData("2026-10-01", null)]
    [InlineData(null, null)]
    public void Both_dates_are_required(string? from, string? to)
    {
        var filter = new AccountingExportFilter(from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture), to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture), null);

        var result = new ExportAccountingValidator().Validate(new ExportAccountingQuery(filter));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == Messages.AccountingRangeRequired);
    }

    [Fact]
    public void The_last_date_cannot_be_before_the_first()
    {
        var result = new ExportAccountingValidator().Validate(new ExportAccountingQuery(new AccountingExportFilter(To, From, null)));

        Assert.Contains(result.Errors, e => e.ErrorMessage == Messages.DateRangeInvalid);
    }

    [Fact]
    public void A_period_of_one_day_is_valid() =>
        Assert.True(new ExportAccountingValidator().Validate(new ExportAccountingQuery(new AccountingExportFilter(From, From, null))).IsValid);
}
