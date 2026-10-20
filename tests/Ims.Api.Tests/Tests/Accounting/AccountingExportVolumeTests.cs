using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Ims.Api.Tests.Tests.Accounting;

/// <summary>
/// PL1.6 acceptance 3 and 9 (the part a test can hold): a period of exactly <see cref="Limits.MaxExportRows"/> rows is written, one row more is refused with advice and no file, and
/// the time of the one at the cap is measured on a database that has ten times as many lines outside the period. The data is written with set-based SQL into a database of its own
/// (an <see cref="ApiFixture"/> per class: a uniquely named database that is dropped at the end), 50,000 rows in March 2025 and 200,000 lines of noise in 2023, so the period is a small
/// part of the table as a month is at the 1M tier. The 1M-tier timing with the same query is a DBA run (docs/dba/README.md section 11).
/// </summary>
public class AccountingExportVolumeTests(ApiFixture fixture, ITestOutputHelper output) : IClassFixture<ApiFixture>
{
    private const int LinesPerDocument = 10;
    private const int PostedOnlyDocuments = 4_000;   // 10 rows each
    private const int ReversedDocuments = 500;       // 20 rows each: the posting and the reversal of every line
    private const int NoiseDocuments = 20_000;       // 200,000 lines in another year
    private static readonly DateOnly From = new(2025, 3, 1);
    private static readonly DateOnly To = new(2025, 3, 31);
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(90);

    private const string SeedSql = """
        DECLARE @user int = (SELECT TOP 1 Id FROM [User] ORDER BY Id);
        DECLARE @warehouse int = (SELECT TOP 1 Id FROM Warehouse ORDER BY Id);
        DECLARE @supplier int = (SELECT TOP 1 Id FROM Partner WHERE Type = 1 ORDER BY Id);
        DECLARE @start datetime2 = '2025-03-02T00:00:00';
        DECLARE @noiseStart datetime2 = '2023-01-01T00:00:00';

        ;WITH n AS (SELECT TOP (@posted + @reversed) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b)
        INSERT StockDocument (Type, No, Status, WarehouseId, PartnerId, Purpose, CreatedBy, CreatedAt, PostedBy, PostedAt, ReversedBy, ReversedAt, RequiresApproval, IsLarge, RequiresOverride)
        SELECT CASE WHEN k % 2 = 0 THEN 1 ELSE 2 END, CONCAT(N'VOL-', k), CASE WHEN k <= @reversed THEN 4 ELSE 3 END, @warehouse, @supplier, CASE WHEN k % 2 = 1 THEN 1 END,
               @user, DATEADD(MINUTE, k * 8, @start), @user, DATEADD(MINUTE, k * 8, @start),
               CASE WHEN k <= @reversed THEN @user END, CASE WHEN k <= @reversed THEN DATEADD(MINUTE, k * 8 + 60, @start) END, 0, 0, 0
        FROM n;

        ;WITH n AS (SELECT TOP (@noise) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS k FROM sys.all_objects a CROSS JOIN sys.all_objects b)
        INSERT StockDocument (Type, No, Status, WarehouseId, PartnerId, Purpose, CreatedBy, CreatedAt, PostedBy, PostedAt, RequiresApproval, IsLarge, RequiresOverride)
        SELECT CASE WHEN k % 2 = 0 THEN 1 ELSE 2 END, CONCAT(N'NOISE-', k), 3, @warehouse, @supplier, CASE WHEN k % 2 = 1 THEN 1 END,
               @user, DATEADD(MINUTE, k * 20, @noiseStart), @user, DATEADD(MINUTE, k * 20, @noiseStart), 0, 0, 0
        FROM n;

        INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, EnteredUnitCost, Amount, CostFromAverage)
        SELECT d.Id, p.Id, 3, p.BaseUomId, 3, 2.5, 2.5, 7.5, 0
        FROM StockDocument d CROSS JOIN (SELECT TOP (@lines) Id, BaseUomId FROM Product WHERE Sku LIKE N'VOLP-%' ORDER BY Id) p
        WHERE d.No LIKE N'VOL-%' OR d.No LIKE N'NOISE-%';

        UPDATE STATISTICS StockDocument; UPDATE STATISTICS StockDocumentLine;
        """;

    private const string OneMoreSql = """
        DECLARE @user int = (SELECT TOP 1 Id FROM [User] ORDER BY Id);
        INSERT StockDocument (Type, No, Status, WarehouseId, Purpose, CreatedBy, CreatedAt, PostedBy, PostedAt, RequiresApproval, IsLarge, RequiresOverride)
        SELECT 2, N'VOL-ONE-MORE', 3, (SELECT TOP 1 Id FROM Warehouse ORDER BY Id), 1, @user, '2025-03-30T00:00:00', @user, '2025-03-30T00:00:00', 0, 0, 0;
        INSERT StockDocumentLine (DocumentId, ProductId, Qty, UomId, BaseQty, UnitCost, EnteredUnitCost, Amount, CostFromAverage)
        SELECT d.Id, p.Id, 3, p.BaseUomId, 3, 2.5, 2.5, 7.5, 0
        FROM StockDocument d CROSS JOIN (SELECT TOP 1 Id, BaseUomId FROM Product WHERE Sku LIKE N'VOLP-%' ORDER BY Id) p WHERE d.No = N'VOL-ONE-MORE';
        """;

    private async Task SeedAsync()
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        for (var i = 0; i < LinesPerDocument; i++) await new ProductBuilder(fixture.Reference).WithSku(Unique.Sku("VOLP")).CreateAsync(admin);
        // The one connection runs the whole script: the parameters are the sizes above.
        var script = SeedSql;
        await fixture.Database.ExecuteAsync("DECLARE @posted int = @p, @reversed int = @r, @noise int = @n, @lines int = @l;\n" + script,
            ("@p", PostedOnlyDocuments), ("@r", ReversedDocuments), ("@n", NoiseDocuments), ("@l", LinesPerDocument));
    }

    private static string Url(DateOnly from, DateOnly to) => $"/api/accounting/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";

    [Fact]
    public async Task A_period_of_exactly_the_cap_is_written_and_timed_and_one_row_more_is_refused_with_advice_and_no_file()
    {
        var seeding = Stopwatch.StartNew();
        await SeedAsync();
        output.WriteLine($"Seeded {PostedOnlyDocuments + ReversedDocuments + NoiseDocuments} documents in {seeding.ElapsedMilliseconds} ms.");
        var expectedRows = PostedOnlyDocuments * LinesPerDocument + ReversedDocuments * LinesPerDocument * 2;
        Assert.Equal(Limits.MaxExportRows, expectedRows);
        var admin = await fixture.AsAsync(Roles.Admin);
        (await admin.Modules.SwitchAsync(ModuleCodes.Accounting, true, "Volume test")).ShouldBeOk();

        // The period at the cap: written whole.
        var clock = Stopwatch.StartNew();
        var written = (await admin.GetAsync(Url(From, To))).ShouldBeOk();
        clock.Stop();
        var file = Xlsx.Read(written.Bytes);

        Assert.Equal(Limits.MaxExportRows, file.Rows.Count);
        var ofReversedDocuments = file.Rows.Where(r => ReversedNumber(file.Cell(r, "Document no."))).ToList();
        Assert.Equal(ReversedDocuments * LinesPerDocument * 2, ofReversedDocuments.Count);
        Assert.Equal(0m, ofReversedDocuments.Sum(r => decimal.Parse(file.Cell(r, "Amount"), NumberStyles.Any, CultureInfo.InvariantCulture))); // every reversed document nets to nothing
        var reads = await StatementReadsAsync();
        output.WriteLine($"Export at the cap: {file.Rows.Count} rows, {written.Bytes.Length / 1024} KB, {clock.ElapsedMilliseconds} ms end to end (request, query, workbook, response).");
        output.WriteLine($"The statement: {reads.Reads} logical reads, {reads.WorkMs} ms of CPU, {reads.ElapsedMs} ms elapsed; plan: {reads.Plan}.");
        Assert.True(clock.Elapsed < Generous, $"The export at the cap took {clock.Elapsed}.");
        Assert.True(reads.Reads is > 0 and < 100_000, $"The statement read {reads.Reads} pages.");   // the whole data set is about 8,000 pages: a plan that reads a row at a time is far above

        // A narrower period is what the advice asks for, and it works.
        var week = Xlsx.Read((await admin.GetAsync(Url(From, new DateOnly(2025, 3, 8)))).ShouldBeOk().Bytes);
        Assert.InRange(week.Rows.Count, 1, Limits.MaxExportRows - 1);

        // One more row: refused, no file, and the refusal is not an export (nothing audited).
        var audited = await ExportsAuditedAsync();
        await fixture.Database.ExecuteAsync(OneMoreSql);
        var refused = (await admin.GetAsync(Url(From, To))).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("Too many rows", refused.Detail);
        Assert.Contains("Narrow the date range", refused.Detail);
        Assert.Contains(Limits.MaxExportRows.ToString(CultureInfo.InvariantCulture), refused.Detail);
        Assert.Equal(audited, await ExportsAuditedAsync());
    }

    /// <summary>The documents VOL-1 to VOL-500 were reversed.</summary>
    private static bool ReversedNumber(string documentNumber) =>
        int.TryParse(documentNumber["VOL-".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var k) && k <= ReversedDocuments;

    private async Task<int> ExportsAuditedAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(a => a.Action == AuditActions.AccountingExported);
    }

    /// <summary>What the plan cache knows about the export statement: it is the only one that selects HsCode from the lines of two kinds of row.</summary>
    private async Task<(long Reads, long WorkMs, long ElapsedMs, string Plan)> StatementReadsAsync()
    {
        const string sql = """
            SELECT TOP 1 qs.total_logical_reads, qs.total_worker_time / 1000, qs.total_elapsed_time / 1000, CAST(p.query_plan AS nvarchar(max))
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
            CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) p
            WHERE t.text LIKE N'%UNION ALL%' AND t.text LIKE N'%HsCode%' AND t.text LIKE N'%StockDocumentLine%'
            ORDER BY qs.last_execution_time DESC
            """;
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(fixture.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (-1, -1, -1, "not in the plan cache");
        var plan = reader.GetString(3);
        var seeks = Regex.Matches(plan, "PhysicalOp=\"Index Seek\"").Count;
        var scans = Regex.Matches(plan, "PhysicalOp=\"(Clustered )?Index Scan\"").Count;
        var parallel = Regex.Match(plan, @"DegreeOfParallelism=""(\d+)""").Groups[1].Value;
        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), $"{seeks} index seeks, {scans} scans, degree of parallelism {parallel}");
    }
}
