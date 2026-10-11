using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Auth;
using Ims.Application.Features.Catalog;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Settings;
using Ims.Application.Features.Stock;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Visibility;

/// <summary>
/// Cost and value are shown by role (docs/design/mvp-ux.md 1.4): the Warehouse staff see none, the Clerk sees cost on documents but not what stock is
/// worth, Supervisor, Owner and Admin see everything. Every read endpoint, every export and every document command is asked as every role, and the
/// money found in the answer (by name, see <see cref="MoneyScan"/>) must be exactly what that role may see.
/// </summary>
[Collection(ApiCollection.Name)]
public class CostVisibilityMatrixTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly string[] AllRoles = [Roles.WarehouseStaff, Roles.Clerk, Roles.Supervisor, Roles.Owner, Roles.Admin];

    private StockScenario _s = null!;
    private ProductDto _product = null!;
    private DocumentDto _receipt = null!;
    private DocumentDto _held = null!;
    private ApprovalRuleScope _rule = null!;

    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>A product with stock at average cost 4, a posted receipt and issue, an amount rule from 1, and an issue the Warehouse staff submitted that the rule holds for approval.</summary>
    public async Task InitializeAsync()
    {
        _s = await StockScenario.CreateAsync(fixture);
        _product = await _s.NewProductAsync();
        _receipt = await _s.ReceiveAsync(Warehouse, new Line(_product.Id, 10, 4m));
        await _s.IssueAsync(Warehouse, new Line(_product.Id, 2));
        _rule = await ApprovalRuleScope.AddAsync(_s.Admin, DocumentType.Issue, amountThreshold: 1m, qtyThreshold: null, Roles.Supervisor);
        _held = await _s.SubmitAsync(_s.Staff, await _s.DraftIssueAsync(_s.Staff, Warehouse, new Line(_product.Id, 1)));
        Assert.True(_held.RequiresApproval);
    }

    public async Task DisposeAsync() => await _rule.DisposeAsync();

    /// <param name="Level">The permission that unlocks the money.</param>
    /// <param name="Money">The money fields (lower case) a role holding it finds in the answer; for a workbook, the money column headers.</param>
    private sealed record ReadCase(string Name, Func<CostVisibilityMatrixTests, string> Url, string Level, string[] Money, bool Workbook = false);

    private static readonly ReadCase[] ReadCases =
    [
        new("documents list", t => $"/api/documents?search={t._receipt.No}", Permissions.CostView, ["totalamount"]),
        new("document", t => $"/api/documents/{t._receipt.Id}", Permissions.CostView, ["totalamount", "unitcost", "baseunitcost", "amount"]),
        new("document held for approval", t => $"/api/documents/{t._held.Id}", Permissions.CostView, ["totalamount", "baseunitcost", "amount", "threshold", "actual"]), // an issue: valued at the average, so no editable unitcost
        new("stock", t => $"/api/stock?productId={t._product.Id}", Permissions.ValuationView, ["avgcost", "value"]),
        new("stock ledger", t => $"/api/stock/ledger?productId={t._product.Id}", Permissions.ValuationView, ["unitcost"]),
        new("stock summary", _ => "/api/stock/summary", Permissions.ValuationView, ["totalvalue"]),
        new("low stock", _ => "/api/stock/low-stock", Permissions.CostView, []),
        new("document counts", _ => "/api/documents/counts", Permissions.CostView, []),
        new("movements", t => $"/api/reports/movements?productId={t._product.Id}", Permissions.CostView, ["unitcost", "amount"]),
        new("approval rules", _ => "/api/approval-rules", Permissions.CostView, ["amountthreshold"]),
        new("audit of a rule", _ => "/api/audit?entityType=ApprovalRule&pageSize=5", Permissions.CostView, ["amountthreshold"]),
        new("audit of a submitted document", t => $"/api/audit?entityId={t._held.No}", Permissions.CostView, ["amount"]),
        new("stock summary export", t => $"/api/reports/stock-summary/export?productId={t._product.Id}", Permissions.ValuationView, ["avg cost", "value"], Workbook: true),
        new("movements export", t => $"/api/reports/movements/export?productId={t._product.Id}", Permissions.CostView, ["unit cost", "amount"], Workbook: true),
        new("ledger export", t => $"/api/reports/ledger/export?productId={t._product.Id}", Permissions.ValuationView, ["unit cost"], Workbook: true),
        new("low stock export", _ => "/api/reports/low-stock/export", Permissions.CostView, [], Workbook: true),
        new("opening stock template", _ => "/api/import/templates/opening-stock", Permissions.CostView, ["unitcost"], Workbook: true),
        new("product template", _ => "/api/import/templates/products", Permissions.CostView, [], Workbook: true),
    ];

    public static IEnumerable<object[]> Reads() => ReadCases.Select(c => new object[] { c.Name });

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Every_read_shows_each_role_exactly_the_money_it_may_see(string name)
    {
        var read = ReadCases.Single(c => c.Name == name);
        var url = read.Url(this);
        var mismatches = new List<string>();
        var answered = 0;

        foreach (var role in AllRoles)
        {
            var response = await _s.As(role).GetAsync(url);
            if (response.Status == HttpStatusCode.Forbidden) continue; // the role may not call this endpoint at all, so it gets no money either
            response.ShouldBeOk();
            answered++;

            var found = read.Workbook ? MoneyScan.InWorkbook(response.Bytes) : MoneyScan.InJson(response);
            var expected = RoleMatrix.MayUse(role, read.Level) ? MoneyScan.Names(read.Money) : MoneyScan.Names();
            if (!found.SetEquals(expected)) mismatches.Add($"{role}: found [{string.Join(", ", found)}] but may see [{string.Join(", ", expected)}]");
        }

        Assert.True(answered >= 1, $"{read.Name}: nobody could call it");
        Assert.True(mismatches.Count == 0, $"{read.Name} ({url}):{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
    }

    [Fact]
    public async Task Every_catalogued_read_hides_cost_from_the_staff_and_value_from_the_clerk()
    {
        var valuation = new[] { "avgcost", "value", "totalvalue" };
        var leaks = new List<string>();

        foreach (var endpoint in EndpointCatalog.All.Where(e => e.Method == HttpMethod.Get))
        {
            foreach (var role in new[] { Roles.WarehouseStaff, Roles.Clerk })
            {
                var response = await _s.As(role).GetAsync(endpoint.Path(fixture.Reference));
                if (response.Status != HttpStatusCode.OK || response.ContentType != "application/json") continue;
                var leaked = MoneyScan.InJson(response).Where(field => role == Roles.WarehouseStaff
                    || valuation.Contains(field) || (field == "unitcost" && endpoint.Name.Contains("ledger", StringComparison.Ordinal))).ToList();
                if (leaked.Count > 0) leaks.Add($"{role} {endpoint.Name}: {string.Join(", ", leaked)}");
            }
        }

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine, leaks));
    }

    [Fact]
    public async Task The_audit_log_is_closed_to_staff_and_clerk_and_open_to_owner_and_admin()
    {
        foreach (var role in new[] { Roles.WarehouseStaff, Roles.Clerk })
        {
            (await _s.As(role).GetAsync("/api/audit")).ShouldBe(HttpStatusCode.Forbidden);
            (await _s.As(role).GetAsync("/api/audit/filters")).ShouldBe(HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { Roles.Owner, Roles.Admin })
            (await _s.As(role).GetAsync("/api/audit")).ShouldBeOk();
    }

    [Theory]
    [InlineData(Roles.WarehouseStaff, false, false)]
    [InlineData(Roles.Clerk, true, false)]
    [InlineData(Roles.Supervisor, true, true)]
    [InlineData(Roles.Owner, true, true)]
    [InlineData(Roles.Admin, true, true)]
    public async Task The_permission_list_the_web_app_receives_says_what_each_role_may_see(string role, bool cost, bool valuation)
    {
        var me = (await _s.As(role).GetAsync("/api/auth/me")).Data<UserInfo>();

        Assert.Equal(cost, me.Permissions.Contains(Permissions.CostView));
        Assert.Equal(valuation, me.Permissions.Contains(Permissions.ValuationView));
    }

    // ----- sorting -----
    [Theory]
    [InlineData(Roles.WarehouseStaff)]
    [InlineData(Roles.Clerk)]
    public async Task Sorting_stock_by_value_is_refused_to_a_role_that_may_not_see_value(string role)
    {
        foreach (var descending in new[] { "false", "true" })
        {
            var problem = (await _s.As(role).GetAsync($"/api/stock?sortBy=Value&desc={descending}")).ShouldBeProblem(HttpStatusCode.BadRequest);

            Assert.Contains("sortBy", problem.Errors!.Keys);
        }
    }

    // ----- commands -----
    [Fact]
    public async Task Every_document_command_answers_the_warehouse_staff_without_cost()
    {
        var staff = _s.Staff;
        var request = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(_product.Id, 3, 9m)).Build();

        var created = await staff.Documents.CreateReceiptAsync(request);
        var id = created.Data<DocumentDto>().Id;
        var updated = await staff.Documents.UpdateAsync(id, request);
        var submitted = await staff.Documents.SubmitAsync(id);
        var posted = await staff.Documents.PostAsync(id);
        var toCancel = (await staff.Documents.CreateReceiptAsync(request)).Data<DocumentDto>().Id;
        var cancelled = await staff.Documents.CancelAsync(toCancel, "not needed");
        var issue = await staff.Documents.CreateIssueAsync(new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.CustomerId).WithLines(new Line(_product.Id, 1)).Build());

        foreach (var (name, response) in new[] { ("create receipt", created), ("update", updated), ("submit", submitted), ("post", posted), ("cancel", cancelled), ("create issue", issue) })
        {
            var found = MoneyScan.InJson(response.ShouldBeOk());
            Assert.True(found.Count == 0, $"{name} showed money to the Warehouse staff: {string.Join(", ", found)}");
        }
        Assert.Equal(DocumentStatus.Posted, posted.Read<DocumentDto>().Status);
    }

    [Fact]
    public async Task Every_document_command_answers_the_clerk_with_cost_on_the_document_she_works_on()
    {
        var clerk = _s.Clerk;
        var request = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(_product.Id, 3, 9m)).Build();

        var created = await clerk.Documents.CreateReceiptAsync(request);
        var id = created.Data<DocumentDto>().Id;
        var updated = await clerk.Documents.UpdateAsync(id, request);
        var submitted = await clerk.Documents.SubmitAsync(id);
        var toCancel = (await clerk.Documents.CreateReceiptAsync(request)).Data<DocumentDto>().Id;
        var cancelled = await clerk.Documents.CancelAsync(toCancel, "not needed");

        foreach (var (name, response) in new[] { ("create", created), ("update", updated), ("submit", submitted), ("cancel", cancelled) })
            Assert.True(MoneyScan.InJson(response.ShouldBeOk()).SetEquals(MoneyScan.Names("totalamount", "unitcost", "baseunitcost", "amount")), $"{name}: {string.Join(", ", MoneyScan.InJson(response))}");
        Assert.Equal(27m, created.Read<DocumentDto>().TotalAmount);
    }

    [Fact]
    public async Task Every_document_command_answers_the_supervisor_with_everything()
    {
        var held2 = await _s.SubmitAsync(_s.Staff, await _s.DraftIssueAsync(_s.Staff, Warehouse, new Line(_product.Id, 1)));
        var toReverse = await _s.ReceiveAsync(Warehouse, new Line(_product.Id, 3, 4m));

        var approved = await _s.Supervisor.Documents.ApproveAsync(_held.Id);
        var rejected = await _s.Supervisor.Documents.RejectAsync(held2.Id, "wrong customer");
        var reversed = await _s.Supervisor.Documents.ReverseAsync(toReverse.Id, "entered by mistake");
        var posted = await _s.Admin.Documents.PostAsync(_held.Id);

        Assert.True(MoneyScan.InJson(approved.ShouldBeOk()).SetEquals(MoneyScan.Names("totalamount", "baseunitcost", "amount", "threshold", "actual")));
        Assert.True(MoneyScan.InJson(rejected.ShouldBeOk()).SetEquals(MoneyScan.Names("totalamount", "baseunitcost", "amount")));
        Assert.True(MoneyScan.InJson(reversed.ShouldBeOk()).SetEquals(MoneyScan.Names("totalamount", "unitcost", "baseunitcost", "amount")));
        Assert.True(MoneyScan.InJson(posted.ShouldBeOk()).SetEquals(MoneyScan.Names("totalamount", "baseunitcost", "amount")));
    }

    // ----- approval flows without amounts -----
    [Fact]
    public async Task The_warehouse_staff_still_see_that_a_document_needs_approval_and_why_but_not_the_amount()
    {
        var asStaff = (await _s.Staff.Documents.GetAsync(_held.Id)).Data<DocumentDto>();
        var asSupervisor = (await _s.Supervisor.Documents.GetAsync(_held.Id)).Data<DocumentDto>();

        Assert.True(asStaff.RequiresApproval);
        Assert.Equal(DocumentStatus.Submitted, asStaff.Status);
        Assert.True(asStaff.AmountIsEstimate);
        Assert.Null(asStaff.TotalAmount);
        var trigger = Assert.Single(asStaff.ApprovalTriggers);
        Assert.Equal(ApprovalMetric.Amount, trigger.Metric);
        Assert.Equal(Roles.Supervisor, trigger.RoleRequired);
        Assert.Null(trigger.Threshold);
        Assert.Null(trigger.Actual);
        Assert.Contains(DocumentActions.Cancel, asStaff.AllowedActions); // the submitter may still recall it

        Assert.Equal(4m, asSupervisor.TotalAmount);
        var seen = Assert.Single(asSupervisor.ApprovalTriggers);
        Assert.Equal((1m, 4m), (seen.Threshold, seen.Actual));
        Assert.Contains(DocumentActions.Approve, asSupervisor.AllowedActions);
    }
}
