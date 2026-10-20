using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Accounting;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Partners;
using Ims.Application.Features.Users;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Accounting;

/// <summary>
/// PL1.6 acceptance 1 to 8 on a real SQL Server: the accounting file of a period. The module gate (404 off, 401 anonymous, 403 without the permission), the fixed layout, signed reversal rows,
/// posted documents only, the Kuala Lumpur day, warehouse scope, formula-safe text, blank tax values, the ledger and the screen agreeing, the cost.view check that does not depend on the policy
/// and the audit event. Every test works in a warehouse of its own, so the other tests' documents never appear in what it counts. The row cap and the timing are in
/// <c>AccountingExportVolumeTests</c> (a database of its own with 50,000 lines).
/// </summary>
[Collection(ApiCollection.Name)]
public class AccountingExportTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(Limits.MalaysiaUtcOffsetHours));

    private StockScenario _s = null!;

    public async Task InitializeAsync()
    {
        await ResetAsync();
        _s = await StockScenario.CreateAsync(fixture);
        await TurnModuleOnAsync();
    }

    public Task DisposeAsync() => ResetAsync();

    private Task ResetAsync() => fixture.Database.ExecuteAsync("DELETE FROM SystemSetting WHERE [Key] LIKE N'Module.%'");

    // The setting row is written straight into the database, not through PUT /api/modules, so these tests do not depend on whether the API lets the module be switched on yet.
    private Task TurnModuleOnAsync() => fixture.Database.ExecuteAsync("INSERT SystemSetting ([Key], [Value]) VALUES (N'Module.accounting', N'true')");

    // ----- helpers -----
    private static string Url(DateOnly from, DateOnly to, Guid? warehouse = null) =>
        $"/api/accounting/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" + (warehouse is { } w ? $"&warehouseId={w}" : "");

    private static string Url(Guid? warehouse = null) => Url(Today.AddDays(-1), Today.AddDays(1), warehouse);

    private static decimal Number(string text) => decimal.Parse(text, NumberStyles.Any, CultureInfo.InvariantCulture);

    private async Task<XlsxContent> FileOfAsync(Guid warehouse, ApiClient? by = null, DateOnly? from = null, DateOnly? to = null)
    {
        var response = await (by ?? _s.Admin).GetAsync(from is { } f && to is { } t ? Url(f, t, warehouse) : Url(warehouse));
        return Xlsx.Read(response.ShouldBeOk().Bytes);
    }

    private async Task<DocumentDto> ReceiveFromAsync(Guid warehouse, Guid? partner, params Line[] lines)
    {
        var draft = (await _s.Admin.Documents.CreateReceiptAsync(new DocumentBuilder(warehouse).WithPartner(partner).WithLines(lines).Build())).Data<DocumentDto>();
        return await _s.CompleteAsync(await _s.SubmitAsync(_s.Admin, draft));
    }

    private async Task<DocumentDto> IssueToAsync(Guid warehouse, Guid? partner, params Line[] lines)
    {
        var draft = (await _s.Admin.Documents.CreateIssueAsync(new DocumentBuilder(warehouse).WithPartner(partner).WithLines(lines).Build())).Data<DocumentDto>();
        return await _s.CompleteAsync(await _s.SubmitAsync(_s.Admin, draft));
    }

    private Task SetPostedAtAsync(DocumentDto document, DateTime utc) =>
        fixture.Database.ExecuteAsync("UPDATE StockDocument SET PostedAt = @at WHERE PublicId = @id", ("@at", utc), ("@id", document.Id));

    private static DateTime Utc(int year, int month, int day, int hour, int minute, int second) => new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private async Task<PartnerDto> NewPartnerAsync(PartnerType type, string? name = null, string? tin = "C1234567890", string? sstRegNo = "W10-1234-56789012") =>
        (await _s.Admin.Partners.CreateAsync(new PartnerSaveRequest(type, name ?? Unique.Name("Partner"), null, true, tin, sstRegNo))).Data<PartnerDto>();

    private async Task<int> AuditedExportsAsync()
    {
        using var scope = fixture.Host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(a => a.Action == AuditActions.AccountingExported);
    }

    private async Task<TestAccount> ScopedOwnerAsync(Guid warehouseId)
    {
        var username = Unique.Username("acct");
        var password = $"Pw-{Guid.NewGuid():N}-1!";
        var request = new UserSaveRequest(username, Unique.Email(username), username, Roles.Owner, true, password, [warehouseId], false);
        var created = (await _s.Admin.Users.CreateAsync(request)).Data<UserDto>();
        return new TestAccount(created.Id, username, request.Email, password, Roles.Owner, [warehouseId]);
    }

    // ----- the gate (acceptance 4) -----
    [Fact]
    public async Task While_the_module_is_off_every_signed_in_caller_gets_404_even_the_ones_with_the_permission_and_an_anonymous_caller_still_gets_401()
    {
        var warehouse = await _s.NewWarehouseAsync();
        await ResetAsync();
        var before = await AuditedExportsAsync();

        foreach (var who in new[] { _s.Admin, _s.Owner, _s.Supervisor, _s.Staff, _s.Clerk })
            (await who.GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.NotFound);
        (await fixture.Anonymous().GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.Unauthorized);

        Assert.Equal(before, await AuditedExportsAsync());
    }

    [Fact]
    public async Task While_the_module_is_on_the_permission_decides_403_without_it_and_200_with_it_and_401_with_no_token()
    {
        var warehouse = await _s.NewWarehouseAsync();

        foreach (var who in new[] { _s.Admin, _s.Owner })
            (await who.GetAsync(Url(warehouse.Id))).ShouldBeOk();
        foreach (var who in new[] { _s.Supervisor, _s.Staff, _s.Clerk })
            (await who.GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(Permissions.CostView)]
    [InlineData(Permissions.MasterDataRead)]
    [InlineData(Permissions.ReportsRead)]
    public async Task A_token_with_the_export_permission_but_without_one_it_requires_is_refused_by_the_handler_and_nothing_is_audited(string missing)
    {
        var granted = new[] { Permissions.AccountingExport, Permissions.CostView, Permissions.MasterDataRead, Permissions.ReportsRead, Permissions.StockRead }.Where(p => p != missing);
        var token = ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: granted);
        var before = await AuditedExportsAsync();

        var refused = (await fixture.CreateClient(token).GetAsync(Url())).ShouldBeProblem(HttpStatusCode.Forbidden);

        Assert.Equal(Messages.AccountingExportNeedsPermissions, refused.Detail);
        Assert.Equal(before, await AuditedExportsAsync());
    }

    [Fact]
    public async Task A_role_with_the_export_permission_and_cost_view_only_cannot_be_saved_because_it_would_show_partners_and_products_without_the_right_to_read_them()
    {
        var created = (await _s.Admin.CreateAsync(Unique.Name("Cost only"), Permissions.CostView, Permissions.AccountingExport)).ShouldBeProblem(HttpStatusCode.BadRequest);
        var complete = (await _s.Admin.CreateAsync(Unique.Name("Accountant"), Permissions.CostView, Permissions.MasterDataRead, Permissions.ReportsRead, Permissions.AccountingExport)).Data<Ims.Application.Features.Roles.RoleDto>();

        Assert.Contains("masterdata.read", created.Detail);
        Assert.Contains(Permissions.AccountingExport, complete.Permissions);
        (await _s.Admin.ReplaceAsync(complete, [Permissions.CostView, Permissions.MasterDataRead, Permissions.AccountingExport])).ShouldBeProblem(HttpStatusCode.BadRequest); // taking reports.read away is refused too
        (await _s.Admin.ReplaceAsync(complete, [Permissions.CostView, Permissions.MasterDataRead, Permissions.ReportsRead])).ShouldBeOk();                     // dropping the export itself is fine
        (await _s.Admin.DeleteRoleAsync(complete)).ShouldBe(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData("from=0001-01-01&to=2026-10-31")]
    [InlineData("from=2026-10-01&to=9999-12-31")]
    [InlineData("from=1999-12-31&to=2026-10-31")]
    public async Task A_date_far_outside_the_calendar_is_400_with_a_sentence_not_a_server_error(string query)
    {
        var refused = (await _s.Admin.GetAsync("/api/accounting/export?" + query)).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(Messages.AccountingDateOutOfRange, refused.Detail);
    }

    // ----- the file (acceptance 1) -----
    [Fact]
    public async Task The_answer_is_an_xlsx_with_the_fixed_name_the_fixed_sheet_and_headers_and_is_never_cached()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);

        var response = (await _s.Admin.GetAsync(Url(from, to, warehouse.Id))).ShouldBeOk();

        Assert.Equal(ContentTypes.Xlsx, response.ContentType);
        Assert.Contains("ims-accounting-2026-10-01-2026-10-31.xlsx", response.ContentDisposition);
        var cacheControl = response.Header("Cache-Control") ?? "";
        Assert.True(cacheControl.Contains("no-store", StringComparison.Ordinal) && cacheControl.Contains("private", StringComparison.Ordinal), cacheControl);
        var file = Xlsx.Read(response.Bytes);
        Assert.Equal("Accounting", file.SheetName);
        Assert.Equal(["Date", "Document no.", "Type", "Purpose", "Reversal of", "Warehouse", "Partner", "Partner TIN", "Partner SST reg. no.", "SKU", "Product", "HS code", "SST code", "Unit",
            "Quantity", "Unit cost", "Amount"], file.Headers);
        Assert.Empty(file.Rows);
    }

    [Fact]
    public async Task One_row_per_posted_line_with_every_column_filled_and_nothing_for_a_document_that_is_not_posted()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var supplier = await NewPartnerAsync(PartnerType.Supplier);
        var product = await _s.NewProductAsync(customize: p => p.WithTaxCodes("7318.15", "ST-1"));
        var other = await _s.NewProductAsync();
        var posted = await ReceiveFromAsync(warehouse.Id, supplier.Id, new Line(product.Id, 10, 5m), new Line(other.Id, 2, 3m));
        await _s.DraftReceiptAsync(_s.Admin, warehouse.Id, new Line(product.Id, 99, 1m));                                     // a draft
        await _s.SubmitAsync(_s.Admin, await _s.DraftReceiptAsync(_s.Admin, warehouse.Id, new Line(product.Id, 98, 1m)));       // submitted, not posted

        var file = await FileOfAsync(warehouse.Id);

        Assert.Equal(2, file.Rows.Count);
        var row = file.FindRow("SKU", product.Sku)!;
        Assert.Equal(
            [Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), posted.No, "Receipt", "", "", warehouse.Code, supplier.Name, "C1234567890", "W10-1234-56789012", product.Sku, product.Name,
                "7318.15", "ST-1"],
            file.Headers.Take(13).Select(h => file.Cell(row, h)).ToList());
        Assert.NotEqual("", file.Cell(row, "Unit")); // the base unit of the product
        Assert.Equal((10m, 5m, 50m), (Number(file.Cell(row, "Quantity")), Number(file.Cell(row, "Unit cost")), Number(file.Cell(row, "Amount"))));
    }

    [Fact]
    public async Task A_missing_tax_value_and_a_missing_partner_are_blank_cells_and_the_export_succeeds()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 3, 2m));

        var file = await FileOfAsync(warehouse.Id);

        var row = file.FindRow("SKU", product.Sku)!;
        Assert.All(new[] { "Purpose", "Reversal of", "Partner", "Partner TIN", "Partner SST reg. no.", "HS code", "SST code" }, column => Assert.Equal("", file.Cell(row, column)));
    }

    // ----- signed reversal rows (acceptance 2) -----
    [Fact]
    public async Task A_reversed_receipt_has_a_row_of_10_and_50_and_a_row_of_minus_10_and_minus_50_marked_with_the_document_and_the_columns_add_up_to_nothing()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var receipt = await ReceiveFromAsync(warehouse.Id, fixture.Reference.SupplierId, new Line(product.Id, 10, 5m));
        (await _s.Admin.Documents.ReverseAsync(receipt.Id, "entered by mistake")).ShouldBeOk();

        var file = await FileOfAsync(warehouse.Id);

        Assert.Equal(2, file.Rows.Count);
        var (posting, reversal) = (file.Rows[0], file.Rows[1]);
        Assert.Equal(("10", "50.00", ""), (file.Cell(posting, "Quantity"), file.Cell(posting, "Amount"), file.Cell(posting, "Reversal of")));
        Assert.Equal(("-10", "-50.00", receipt.No), (file.Cell(reversal, "Quantity"), file.Cell(reversal, "Amount"), file.Cell(reversal, "Reversal of")));
        Assert.Equal(receipt.No, file.Cell(reversal, "Document no."));
        Assert.Equal("5.0000", file.Cell(reversal, "Unit cost")); // a price is not signed
        Assert.Equal((0m, 0m), (file.Rows.Sum(r => Number(file.Cell(r, "Quantity"))), file.Rows.Sum(r => Number(file.Cell(r, "Amount")))));
    }

    [Fact]
    public async Task An_issue_is_negative_and_its_reversal_is_positive_with_the_purpose_on_both_rows()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        await ReceiveFromAsync(warehouse.Id, fixture.Reference.SupplierId, new Line(product.Id, 20, 2m));
        var issue = await IssueToAsync(warehouse.Id, fixture.Reference.CustomerId, new Line(product.Id, 4));
        (await _s.Admin.Documents.ReverseAsync(issue.Id, "wrong customer")).ShouldBeOk();

        var file = await FileOfAsync(warehouse.Id);

        Assert.Equal(3, file.Rows.Count);
        var issueRows = file.Rows.Where(r => file.Cell(r, "Document no.") == issue.No).ToList();
        Assert.Equal([("-4", "-8.00", "Sale"), ("4", "8.00", "Sale")], issueRows.Select(r => (file.Cell(r, "Quantity"), file.Cell(r, "Amount"), file.Cell(r, "Purpose"))));
        Assert.Equal(20m, file.Rows.Sum(r => Number(file.Cell(r, "Quantity")))); // what is on the shelf
        Assert.Equal(40m, file.Rows.Sum(r => Number(file.Cell(r, "Amount"))));
    }

    // ----- the period (acceptance 1) -----
    [Fact]
    public async Task The_period_is_cut_at_midnight_in_Kuala_Lumpur_both_ends_included()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var firstSecondOfOctober = await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 1, 1m));
        var lastSecondOfSeptember = await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 1, 1m));
        var lastSecondOfOctober = await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 1, 1m));
        var firstSecondOfNovember = await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 1, 1m));
        await SetPostedAtAsync(firstSecondOfOctober, Utc(2026, 9, 30, 16, 0, 0));    // 1 October 00:00:00 in Kuala Lumpur (UTC+8)
        await SetPostedAtAsync(lastSecondOfSeptember, Utc(2026, 9, 30, 15, 59, 59));
        await SetPostedAtAsync(lastSecondOfOctober, Utc(2026, 10, 31, 15, 59, 59));
        await SetPostedAtAsync(firstSecondOfNovember, Utc(2026, 10, 31, 16, 0, 0));

        var october = await FileOfAsync(warehouse.Id, from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 10, 31));
        var september30 = await FileOfAsync(warehouse.Id, from: new DateOnly(2026, 9, 30), to: new DateOnly(2026, 9, 30));
        var november1 = await FileOfAsync(warehouse.Id, from: new DateOnly(2026, 11, 1), to: new DateOnly(2026, 11, 1));

        Assert.Equal([firstSecondOfOctober.No, lastSecondOfOctober.No], october.Rows.Select(r => october.Cell(r, "Document no.")));
        Assert.Equal(["01/10/2026", "31/10/2026"], october.Rows.Select(r => october.Cell(r, "Date")));
        Assert.Equal([lastSecondOfSeptember.No], september30.Rows.Select(r => september30.Cell(r, "Document no.")));
        Assert.Equal([firstSecondOfNovember.No], november1.Rows.Select(r => november1.Cell(r, "Document no.")));
    }

    [Fact]
    public async Task A_reversal_belongs_to_the_period_in_which_it_happened_so_each_period_adds_up_to_its_own_change()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var receipt = await ReceiveFromAsync(warehouse.Id, null, new Line(product.Id, 5, 2m));
        (await _s.Admin.Documents.ReverseAsync(receipt.Id, "entered by mistake")).ShouldBeOk();
        await SetPostedAtAsync(receipt, Utc(2026, 9, 15, 4, 0, 0)); // posted in September, reversed today

        var september = await FileOfAsync(warehouse.Id, from: new DateOnly(2026, 9, 1), to: new DateOnly(2026, 9, 30));
        var thisPeriod = await FileOfAsync(warehouse.Id);

        Assert.Equal([("5", "")], september.Rows.Select(r => (september.Cell(r, "Quantity"), september.Cell(r, "Reversal of"))));
        Assert.Equal([("-5", receipt.No)], thisPeriod.Rows.Select(r => (thisPeriod.Cell(r, "Quantity"), thisPeriod.Cell(r, "Reversal of"))));
    }

    [Fact]
    public async Task A_request_without_both_dates_or_with_the_last_before_the_first_is_400_with_a_sentence()
    {
        var before = await AuditedExportsAsync();

        (await _s.Admin.GetAsync("/api/accounting/export")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Admin.GetAsync("/api/accounting/export?from=2026-10-01")).ShouldBeProblem(HttpStatusCode.BadRequest);
        (await _s.Admin.GetAsync("/api/accounting/export?to=2026-10-31")).ShouldBeProblem(HttpStatusCode.BadRequest);
        Assert.Equal(Messages.DateRangeInvalid, (await _s.Admin.GetAsync(Url(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1)))).ShouldBeProblem(HttpStatusCode.BadRequest).Detail);
        (await _s.Admin.GetAsync("/api/accounting/export?from=yesterday&to=today")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(before, await AuditedExportsAsync());
    }

    // ----- warehouse scope (acceptance 4) -----
    [Fact]
    public async Task A_person_limited_to_one_warehouse_gets_only_its_rows_and_a_warehouse_outside_the_limit_is_404_like_one_that_does_not_exist()
    {
        var mine = await _s.NewWarehouseAsync();
        var theirs = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var myReceipt = await ReceiveFromAsync(mine.Id, null, new Line(product.Id, 3, 4m));
        var theirReceipt = await ReceiveFromAsync(theirs.Id, null, new Line(product.Id, 8, 4m));
        var owner = await ScopedOwnerAsync(mine.Id);
        var (client, _) = await new AccountFactory(fixture).SignInAsync(owner);

        var everything = Xlsx.Read((await client.GetAsync(Url())).ShouldBeOk().Bytes);        // no warehouse asked: the whole scope, which is one warehouse
        var asked = Xlsx.Read((await client.GetAsync(Url(mine.Id))).ShouldBeOk().Bytes);
        var outside = (await client.GetAsync(Url(theirs.Id))).ShouldBeProblem(HttpStatusCode.NotFound);
        var unknown = (await client.GetAsync(Url(Guid.NewGuid()))).ShouldBeProblem(HttpStatusCode.NotFound);

        Assert.All(everything.Rows, r => Assert.Equal(mine.Code, everything.Cell(r, "Warehouse")));
        Assert.Contains(everything.Rows, r => everything.Cell(r, "Document no.") == myReceipt.No);
        Assert.DoesNotContain(everything.Rows, r => everything.Cell(r, "Document no.") == theirReceipt.No);
        Assert.Equal([myReceipt.No], asked.Rows.Select(r => asked.Cell(r, "Document no.")));
        Assert.Equal((unknown.Title, unknown.Detail), (outside.Title, outside.Detail)); // the person learns nothing about warehouses they cannot see
        Assert.Equal(Messages.WarehouseNotFound, outside.Detail);
    }

    [Fact]
    public async Task A_person_who_sees_every_warehouse_can_ask_for_one_and_gets_only_that_one()
    {
        var one = await _s.NewWarehouseAsync();
        var two = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var inOne = await ReceiveFromAsync(one.Id, null, new Line(product.Id, 1, 1m));
        await ReceiveFromAsync(two.Id, null, new Line(product.Id, 1, 1m));

        var file = await FileOfAsync(one.Id);
        var both = Xlsx.Read((await _s.Admin.GetAsync(Url())).ShouldBeOk().Bytes);

        Assert.Equal([inOne.No], file.Rows.Select(r => file.Cell(r, "Document no.")));
        Assert.Contains(both.Rows, r => both.Cell(r, "Warehouse") == two.Code);
        Assert.Contains(both.Rows, r => both.Cell(r, "Warehouse") == one.Code);
    }

    // ----- text (formula injection) -----
    [Fact]
    public async Task Text_that_could_run_as_a_formula_is_written_as_quoted_text_never_as_a_formula()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var partner = await NewPartnerAsync(PartnerType.Supplier, name: "=HYPERLINK(\"http://example.test\",\"pay\")");
        var product = await _s.NewProductAsync(customize: p => p.WithName("+SUM(1+1)").WithSku("-" + Unique.Sku("INJ")));
        var second = await _s.NewProductAsync(customize: p => p.WithName("@cmd|calc").WithSku("=" + Unique.Sku("INJ")));
        var third = await _s.NewProductAsync(customize: p => p.WithName("'Ah Kow Trading")); // a leading quote is data too: the writer would drop it without a prefix of its own
        await ReceiveFromAsync(warehouse.Id, partner.Id, new Line(product.Id, 1, 1m), new Line(second.Id, 1, 1m), new Line(third.Id, 1, 1m));

        var response = (await _s.Admin.GetAsync(Url(warehouse.Id))).ShouldBeOk();

        using var workbook = new XLWorkbook(new MemoryStream(response.Bytes));
        var sheet = workbook.Worksheet(1);
        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        // The file keeps each value exactly as typed and marks the cell as text with Excel's own quote-prefix flag (what a typed leading apostrophe does), so no spreadsheet runs it, now or when
        // somebody edits the cell. The values are not altered, which an import into the accounting package needs.
        var expected = new Dictionary<(int Row, string Column), string>
        {
            [(2, "Partner")] = partner.Name, [(3, "Partner")] = partner.Name, [(2, "SKU")] = product.Sku, [(2, "Product")] = product.Name, [(3, "SKU")] = second.Sku, [(3, "Product")] = second.Name, [(4, "Product")] = third.Name
        };
        foreach (var ((rowNumber, header), text) in expected)
        {
            var cell = sheet.Cell(rowNumber, headers.IndexOf(header) + 1);
            Assert.False(cell.HasFormula, $"{header} row {rowNumber} is a formula");
            Assert.Equal(XLDataType.Text, cell.DataType);
            Assert.Equal(text, cell.GetString());
            Assert.True(cell.Style.IncludeQuotePrefix, $"{header} row {rowNumber} has no quote prefix");
        }
        Assert.False(sheet.Cell(2, headers.IndexOf("Type") + 1).Style.IncludeQuotePrefix); // ordinary text is left alone
        Assert.False(sheet.Cell(2, headers.IndexOf("Warehouse") + 1).Style.IncludeQuotePrefix);
        // The numbers and the date are real numbers and a real date, not text.
        Assert.Equal(XLDataType.Number, sheet.Cell(2, headers.IndexOf("Quantity") + 1).DataType);
        Assert.Equal(XLDataType.Number, sheet.Cell(2, headers.IndexOf("Amount") + 1).DataType);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 1).DataType);
    }

    // ----- the money (acceptance 5) -----
    [Fact]
    public async Task The_amounts_are_the_ones_on_screen_and_the_ledger_value_of_the_period_equals_the_total_of_the_file()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync();
        var first = await ReceiveFromAsync(warehouse.Id, fixture.Reference.SupplierId, new Line(product.Id, 10, 5m));
        var second = await ReceiveFromAsync(warehouse.Id, fixture.Reference.SupplierId, new Line(product.Id, 10, 7m));
        var issue = await IssueToAsync(warehouse.Id, fixture.Reference.CustomerId, new Line(product.Id, 8)); // at the average of 6.00: 48.00
        var mistaken = await ReceiveFromAsync(warehouse.Id, fixture.Reference.SupplierId, new Line(product.Id, 4, 10m));
        (await _s.Admin.Documents.ReverseAsync(mistaken.Id, "entered by mistake")).ShouldBeOk();

        var file = await FileOfAsync(warehouse.Id);
        var ledger = await _s.LedgerAsync(product.Id, warehouse.Id);

        foreach (var document in new[] { first, second, issue })
        {
            var screen = (await _s.Admin.Documents.GetAsync(document.Id)).Data<DocumentDto>();
            var inFile = file.Rows.Where(r => file.Cell(r, "Document no.") == document.No).Sum(r => Math.Abs(Number(file.Cell(r, "Amount"))));
            Assert.Equal(screen.TotalAmount, inFile); // the total of a document is the sum of its lines, and the file shows the lines
        }
        Assert.Equal(72m, file.Rows.Sum(r => Number(file.Cell(r, "Amount")))); // 50 + 70 - 48, the reversed receipt nets to nothing
        Assert.Equal(ledger.Sum(l => l.QtyDelta * l.UnitCost!.Value), file.Rows.Sum(r => Number(file.Cell(r, "Amount"))));
        Assert.Equal(ledger.Sum(l => l.QtyDelta), file.Rows.Sum(r => Number(file.Cell(r, "Quantity"))));
    }

    // ----- the audit event (acceptance 8) -----
    [Fact]
    public async Task Every_export_writes_one_audit_event_with_who_the_period_the_warehouse_and_the_row_count_and_never_the_content()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var product = await _s.NewProductAsync(customize: p => p.WithTaxCodes("7318.15", "ST-1"));
        var partner = await NewPartnerAsync(PartnerType.Supplier, tin: "C99887766", sstRegNo: null);
        var receipt = await ReceiveFromAsync(warehouse.Id, partner.Id, new Line(product.Id, 10, 5m));
        (await _s.Admin.Documents.ReverseAsync(receipt.Id, "entered by mistake")).ShouldBeOk();
        var before = await AuditedExportsAsync();

        (await _s.Admin.GetAsync(Url(warehouse.Id))).ShouldBeOk();

        Assert.Equal(before + 1, await AuditedExportsAsync());
        using var scope = fixture.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logged = await db.Set<AuditLog>().Where(a => a.Action == AuditActions.AccountingExported).OrderByDescending(a => a.Id).FirstAsync();
        Assert.Equal((TestUsers.UsernameOf(Roles.Admin), AuditEntities.AccountingExport), (logged.Actor, logged.EntityType));
        Assert.Equal($"ims-accounting-{Today.AddDays(-1):yyyy-MM-dd}-{Today.AddDays(1):yyyy-MM-dd}.xlsx", logged.EntityId);
        Assert.True(logged.At > DateTime.UtcNow.AddMinutes(-5));
        using var after = System.Text.Json.JsonDocument.Parse(logged.After!);
        Assert.Equal(Today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), after.RootElement.GetProperty("From").GetString());
        Assert.Equal(Today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), after.RootElement.GetProperty("To").GetString());
        Assert.Equal(warehouse.Id, after.RootElement.GetProperty("Warehouse").GetGuid());
        Assert.Equal(2, after.RootElement.GetProperty("Rows").GetInt32());
        foreach (var content in new[] { product.Sku, product.Name, partner.Name, "C99887766", "7318.15", receipt.No })
            Assert.DoesNotContain(content, logged.After + logged.Before);
    }

    [Fact]
    public async Task A_request_that_is_refused_writes_no_audit_event()
    {
        var warehouse = await _s.NewWarehouseAsync();
        var before = await AuditedExportsAsync();

        (await _s.Supervisor.GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.Forbidden);
        (await fixture.Anonymous().GetAsync(Url(warehouse.Id))).ShouldBeProblem(HttpStatusCode.Unauthorized);
        (await _s.Admin.GetAsync(Url(Guid.NewGuid()))).ShouldBeProblem(HttpStatusCode.NotFound);
        (await _s.Admin.GetAsync("/api/accounting/export?from=2026-10-01")).ShouldBeProblem(HttpStatusCode.BadRequest);

        Assert.Equal(before, await AuditedExportsAsync());
    }
}
