using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Stock;
using Ims.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.PublicIds;

/// <summary>
/// GUID plan PR-7: a document is addressed by its public id everywhere (routes, responses, list items, the approvals inbox, the ledger's source document and its filter, the opening-stock
/// import), a document line has no id of its own, a shortage that cannot be named is still said by public ids, and approval rules follow. The integer key stays inside.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class DocumentFlipTests(ApiFixture fixture)
{
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>The look-up an <c>IIdMap</c> makes: nothing but the integer and the public id of rows of the document table.</summary>
    [GeneratedRegex(@"^SELECT \[\w+\]\.\[Id\], \[\w+\]\.\[PublicId\]\s+FROM \[StockDocument\]", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentByPublicId();

    private Task<Guid> StoredIdAsync(string no) => fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM StockDocument WHERE [No] = @v", ("@v", no));

    [Fact]
    public async Task A_document_answers_with_the_public_id_stored_for_it_in_every_response_and_its_lines_have_no_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 3, 2m), new Line((await s.NewProductAsync()).Id, 1, 1m));
        var stored = await StoredIdAsync(draft.No);

        var read = await s.Admin.Documents.GetAsync(draft.Id);
        var listed = (await s.Admin.Documents.ListAsync(search: draft.No)).Data<PagedResult<DocumentListItem>>().Items;
        var submitted = await s.SubmitAsync(s.Admin, draft);
        var posted = await s.CompleteAsync(submitted);
        var reversed = (await s.Admin.Documents.ReverseAsync(draft.Id, "Entered twice")).Data<DocumentDto>();

        Assert.Equal(stored, draft.Id);
        Assert.Equal(stored, read.Data<DocumentDto>().Id);
        Assert.Equal(stored, Assert.Single(listed).Id);
        Assert.All(new[] { submitted, posted, reversed }, d => Assert.Equal(stored, d.Id));
        Assert.Equal(JsonValueKind.String, read.Json().RootElement.GetProperty("id").ValueKind); // a string on the wire, never a number

        var lines = read.Json().RootElement.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.False(line.TryGetProperty("id", out _)));
        Assert.Equal([product.Id.ToString(), draft.Lines[1].ProductId.ToString()], lines.Select(l => l.GetProperty("productId").GetString())); // in the order they were entered
    }

    [Fact]
    public async Task The_integer_routes_are_gone_and_an_unknown_foreign_or_empty_id_is_404_with_one_body_on_every_action()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var wrongType = Warehouse; // a real public id, of a warehouse
        var body = new DocumentBuilder(Warehouse).WithLines(new Line(Guid.NewGuid(), 1)).Build();

        (await s.Admin.GetAsync("/api/documents/1")).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.PutAsync("/api/documents/1", body)).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.PostAsync("/api/documents/1/submit", new { })).ShouldBe(HttpStatusCode.NotFound);
        (await s.Admin.PostAsync("/api/documents/1/post", new { })).ShouldBe(HttpStatusCode.NotFound);

        var calls = new Dictionary<string, Func<Guid, Task<ApiResponse>>>
        {
            ["get"] = id => s.Admin.Documents.GetAsync(id),
            ["update"] = id => s.Admin.Documents.UpdateAsync(id, body),
            ["submit"] = id => s.Admin.Documents.SubmitAsync(id),
            ["recall"] = id => s.Admin.Documents.RecallAsync(id),
            ["cancel"] = id => s.Admin.Documents.CancelAsync(id, "Not needed here"),
            ["approve"] = id => s.Owner.Documents.ApproveAsync(id),
            ["reject"] = id => s.Owner.Documents.RejectAsync(id, "Not needed here"),
            ["post"] = id => s.Admin.Documents.PostAsync(id),
            ["reverse"] = id => s.Admin.Documents.ReverseAsync(id, "Not needed here")
        };
        foreach (var (name, call) in calls)
        {
            var unknown = (await call(Guid.NewGuid())).ShouldBeProblem(HttpStatusCode.NotFound);
            foreach (var other in new[] { wrongType, Guid.Empty })
            {
                var p = (await call(other)).ShouldBeProblem(HttpStatusCode.NotFound);
                Assert.True((unknown.Title, unknown.Detail) == (p.Title, p.Detail), $"{name} {other}: the 404 differs from the one for an unknown id");
            }
        }
    }

    [Fact]
    public async Task The_ledger_names_its_source_document_by_public_id_and_filters_by_it_and_an_unknown_or_foreign_id_filters_to_nothing()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(warehouse.Id, new Line(product.Id, 6, 1m));
        var issue = await s.IssueAsync(warehouse.Id, new Line(product.Id, 2));
        (await s.Admin.Documents.ReverseAsync(issue.Id, "Entered twice")).ShouldBeOk();

        var all = await s.LedgerAsync(product.Id, warehouse.Id);
        Assert.Equal(3, all.Count); // the receipt, the issue and the reversal of the issue (booked against the original)
        Assert.Equal(1, all.Count(r => r.SourceDocId == receipt.Id));
        Assert.Equal(2, all.Count(r => r.SourceDocId == issue.Id));
        Assert.All(all, r => Assert.Contains(r.SourceDocId, new[] { receipt.Id, issue.Id }));

        Assert.Equal(6m, Assert.Single(await s.LedgerAsync(sourceDocId: receipt.Id)).QtyDelta);
        Assert.Equal(2, (await s.LedgerAsync(warehouseId: warehouse.Id, sourceDocId: issue.Id)).Count);
        var export = Xlsx.Read((await s.Admin.GetAsync($"/api/reports/ledger/export?sourceDocId={receipt.Id}")).ShouldBeOk().Bytes);
        Assert.Single(export.Rows);

        foreach (var missing in new[] { Guid.NewGuid(), Warehouse, product.Id, Guid.Empty })
        {
            Assert.Empty(await s.LedgerAsync(sourceDocId: missing));
            Assert.Empty(Xlsx.Read((await s.Admin.GetAsync($"/api/reports/ledger/export?sourceDocId={missing}")).ShouldBeOk().Bytes).Rows);
        }
    }

    [Fact]
    public async Task A_ledger_filter_by_document_is_translated_with_one_look_up_and_the_lists_carry_their_public_ids_without_any()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var receipt = await s.ReceiveAsync(Warehouse, new Line(product.Id, 9, 1m));
        var draft = await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, 1));
        await s.SubmitAsync(s.Admin, draft);
        var log = new Documents.DocumentReadQueryTests.CommandLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<Ims.Infrastructure.Persistence.AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<Ims.Application.Features.Auth.AuthResult>().AccessToken;

        async Task<int> LookupsAsync(Func<Task<ApiResponse>> request)
        {
            log.Commands.Clear();
            (await request()).ShouldBeOk();
            return log.Commands.Count(c => DocumentByPublicId().IsMatch(c));
        }

        Assert.Equal(1, await LookupsAsync(() => client.Stock.LedgerAsync(sourceDocId: receipt.Id)));
        Assert.Equal(0, await LookupsAsync(() => client.Stock.LedgerAsync(product.Id)));                      // the rows project the document's public id from the join they have
        Assert.Equal(0, await LookupsAsync(() => client.Documents.ListAsync()));
        Assert.Equal(0, await LookupsAsync(() => client.Documents.ListAsync(status: DocumentStatus.Submitted, requiresApproval: false))); // the shape of the approvals inbox
        Assert.Equal(0, await LookupsAsync(() => client.Documents.GetAsync(draft.Id)));
        Assert.Equal(0, await LookupsAsync(() => client.Reports.MovementsAsync(product.Id)));
    }

    [Fact]
    public async Task Reading_a_document_costs_the_same_number_of_statements_for_one_line_and_for_many()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var products = new List<Guid>();
        for (var i = 0; i < 25; i++) products.Add((await s.NewProductAsync()).Id);
        var small = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(products[0], 1, 1m));
        var large = await s.DraftReceiptAsync(s.Admin, Warehouse, products.Select(p => new Line(p, 1, 1m)).ToArray());
        var log = new Documents.DocumentReadQueryTests.CommandLog();
        await using var host = fixture.CreateHost(b => b.ConfigureServices(services => services.ConfigureDbContext<Ims.Infrastructure.Persistence.AppDbContext>(o => o.AddInterceptors(log))));
        var client = ApiFixture.CreateClient(host);
        client.AccessToken = (await client.Auth.LoginAsync("admin", fixture.Settings.AdminPassword)).Data<Ims.Application.Features.Auth.AuthResult>().AccessToken;

        async Task<(int Statements, int Lines)> ReadAsync(Guid id)
        {
            log.Commands.Clear();
            var lines = (await client.Documents.GetAsync(id)).Data<DocumentDto>().Lines.Count;
            return (log.Commands.Count, lines);
        }

        await ReadAsync(small.Id); // warm-up: the first request of a host may do work once
        var one = await ReadAsync(small.Id);
        var many = await ReadAsync(large.Id);

        Assert.Equal((1, 25), (one.Lines, many.Lines));
        Assert.Equal(one.Statements, many.Statements); // no query per line
    }

    [Fact]
    public async Task A_shortage_that_cannot_be_named_is_still_a_409_that_says_the_product_and_warehouse_by_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var warehouse = await s.NewWarehouseAsync();
        var product = await s.NewProductAsync();
        var draft = await s.DraftIssueAsync(s.Admin, warehouse.Id, new Line(product.Id, 5));
        // The product is soft-deleted after the draft was made (no use case does it today), so its SKU can no longer be looked up for the message.
        await fixture.Database.ExecuteAsync("UPDATE Product SET REC_DELETED = 1 WHERE PublicId = @p", ("@p", product.Id));

        var problem = (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeProblem(HttpStatusCode.Conflict, "Insufficient stock");

        // Exactly this sentence: the two public ids and the quantities, no integer key.
        Assert.Equal(Messages.InsufficientStockUnnamed(product.Id, warehouse.Id, 0m, 5m), problem.Detail);
        Assert.Equal(product.Id, problem.ProductId);
    }

    [Fact]
    public async Task The_document_events_keep_naming_the_document_by_its_number_and_the_opening_stock_import_answers_with_a_public_id()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 4, 1m));
        (await s.Admin.Documents.CancelAsync(draft.Id, "Not needed")).ShouldBeOk();

        var events = (await s.Admin.Audit.QueryAsync(entityId: draft.No)).Data<AuditPage>().Items;

        Assert.Equal([AuditActions.DocumentCancelled, AuditActions.DocumentCreated], events.Select(e => e.Action).Order());
        Assert.All(events, e => Assert.Equal((nameof(StockDocument), draft.No), (e.EntityType, e.EntityId))); // the number is what a person searches the audit trail for

        var file = Xlsx.Create("Opening stock", ["SKU", "Warehouse", "Qty", "UnitCost"], [product.Sku, fixture.Reference.DefaultWarehouseCode, 6, 2]);
        var imported = (await s.Admin.Import.OpeningStockAsync(file, commit: true)).Data<Ims.Application.Features.Import.ImportResult>();
        var opened = (await s.Admin.Documents.GetAsync(imported.DocumentId!.Value)).Data<DocumentDto>();
        Assert.Equal(DocumentStatus.Draft, opened.Status);
    }

    [Fact]
    public async Task An_approval_rule_is_addressed_by_its_public_id_and_its_audit_rows_name_it_by_it()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var request = new ApprovalRuleSaveRequest(DocumentType.Issue, 987654.32m, null, Roles.Supervisor, IsActive: false);

        var created = (await s.Admin.Settings.CreateRuleAsync(request)).Data<ApprovalRuleDto>();
        var stored = await fixture.Database.ScalarAsync<Guid>("SELECT PublicId FROM ApprovalRule WHERE AmountThreshold = 987654.32 AND REC_DELETED = 0 AND IsActive = 0");
        var updated = (await s.Admin.Settings.UpdateRuleAsync(created.Id, request with { QtyThreshold = 5m })).Data<ApprovalRuleDto>();
        var listed = (await s.Admin.Settings.ListRulesAsync()).Data<List<ApprovalRuleDto>>();

        Assert.Equal(stored, created.Id);
        Assert.Equal(stored, updated.Id);
        Assert.Contains(listed, r => r.Id == stored);
        var audit = (await s.Admin.Audit.QueryAsync(entityType: nameof(ApprovalRule), entityId: stored.ToString())).Data<AuditPage>().Items;
        Assert.Equal([AuditActions.RowCreated, AuditActions.RowUpdated], audit.Select(e => e.Action).Order());

        (await s.Admin.PutAsync("/api/approval-rules/1", request)).ShouldBe(HttpStatusCode.NotFound);
        var unknown = (await s.Admin.Settings.UpdateRuleAsync(Guid.NewGuid(), request)).ShouldBeProblem(HttpStatusCode.NotFound);
        var foreign = (await s.Admin.Settings.UpdateRuleAsync(Warehouse, request)).ShouldBeProblem(HttpStatusCode.NotFound);
        Assert.Equal((unknown.Title, unknown.Detail), (foreign.Title, foreign.Detail));
    }
}
