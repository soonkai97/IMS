using System.Text.Json;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Settings;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Audit;

/// <summary>AUTH-03: logins, document status changes, master-data edits and permission changes are recorded with actor, time, object and before/after values.</summary>
[Collection(ApiCollection.Name)]
public class AuditTrailTests(ApiFixture fixture)
{
    private readonly AccountFactory _accounts = new(fixture);

    private async Task<(TestAccount Account, ApiClient Client)> SignedInAsync(string role)
    {
        var account = await _accounts.CreateAsync(role);
        var (client, _) = await _accounts.SignInAsync(account);
        return (account, client);
    }

    private async Task<List<AuditEntry>> EntriesAsync(string? actor = null, string? entityType = null)
    {
        var admin = await fixture.AsAsync(Roles.Admin);
        var page = (await admin.Audit.QueryAsync(actor, entityType)).Data<AuditPage>();
        return page.Items.OrderBy(e => e.Id).ToList();
    }

    private static int StatusIn(string? json) => JsonDocument.Parse(json ?? throw new Xunit.Sdk.XunitException("audit payload is missing")).RootElement.GetProperty("Status").GetInt32();

    // ---------- documents ----------

    [Fact]
    public async Task Every_document_status_change_is_recorded_with_who_did_it_and_the_old_and_new_status()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 100, 100m));
        var (clerk, clerkClient) = await SignedInAsync(Roles.Clerk);
        var (supervisor, supervisorClient) = await SignedInAsync(Roles.Supervisor);
        var (admin, adminClient) = await SignedInAsync(Roles.Admin);

        var draft = await s.DraftIssueAsync(clerkClient, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 60));
        await s.SubmitAsync(clerkClient, draft);
        (await supervisorClient.Documents.ApproveAsync(draft.Id)).ShouldBeOk();
        (await adminClient.Documents.PostAsync(draft.Id)).ShouldBeOk();
        (await adminClient.Documents.ReverseAsync(draft.Id, "Customer returned the goods")).ShouldBeOk();

        var all = await EntriesAsync(entityType: AuditEntities.StockDocument);
        var trail = all.Where(e => e.EntityId == draft.No).ToList();
        Assert.Equal(
            [AuditActions.DocumentCreated, AuditActions.DocumentSubmitted, AuditActions.DocumentApproved, AuditActions.DocumentPosted, AuditActions.DocumentReversed],
            trail.Select(e => e.Action));
        Assert.Equal([clerk.Username, clerk.Username, supervisor.Username, admin.Username, admin.Username], trail.Select(e => e.Actor));
        Assert.All(trail, e => Assert.True(Math.Abs((DateTime.UtcNow - e.At).TotalMinutes) < 10, $"At {e.At:o} is not recent UTC"));

        var statuses = trail.Skip(1).Select(e => (Before: StatusIn(e.Before), After: StatusIn(e.After))).ToList();
        Assert.Equal(
            [
                ((int)DocumentStatus.Draft, (int)DocumentStatus.Submitted),
                ((int)DocumentStatus.Submitted, (int)DocumentStatus.Approved),
                ((int)DocumentStatus.Approved, (int)DocumentStatus.Posted),   // posting an approved document
                ((int)DocumentStatus.Posted, (int)DocumentStatus.Reversed)
            ],
            statuses);
        Assert.Contains("Customer returned the goods", trail[^1].After);
    }

    [Fact]
    public async Task Rejecting_and_cancelling_are_recorded_with_the_reason_and_the_status_change()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(fixture.Reference.DefaultWarehouseId, new Line(product.Id, 100, 100m));
        var (_, clerkClient) = await SignedInAsync(Roles.Clerk);
        var (_, supervisorClient) = await SignedInAsync(Roles.Supervisor);
        var toReject = await s.SubmitAsync(clerkClient, await s.DraftIssueAsync(clerkClient, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 60)));
        var toCancel = await s.DraftReceiptAsync(clerkClient, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 1, 1m));
        (await supervisorClient.Documents.RejectAsync(toReject.Id, "Quantity looks wrong")).ShouldBeOk();
        (await clerkClient.Documents.CancelAsync(toCancel.Id, "Duplicate entry")).ShouldBeOk();

        var all = await EntriesAsync(entityType: AuditEntities.StockDocument);
        var rejected = all.Single(e => e.EntityId == toReject.No && e.Action == AuditActions.DocumentRejected);
        var cancelled = all.Single(e => e.EntityId == toCancel.No && e.Action == AuditActions.DocumentCancelled);

        Assert.Contains("Quantity looks wrong", rejected.After);
        Assert.Equal((int)DocumentStatus.Submitted, StatusIn(rejected.Before));
        Assert.Equal((int)DocumentStatus.Draft, StatusIn(rejected.After));
        Assert.Contains("Duplicate entry", cancelled.After);
        Assert.Equal((int)DocumentStatus.Draft, StatusIn(cancelled.Before));
        Assert.Equal((int)DocumentStatus.Cancelled, StatusIn(cancelled.After));
    }

    [Fact]
    public async Task A_negative_stock_override_is_audited_at_approval_with_the_approver_and_the_reason_and_at_posting_as_used()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var (approver, approverClient) = await SignedInAsync(Roles.Supervisor);
        var (_, adminClient) = await SignedInAsync(Roles.Admin);
        await using var _ = await SettingsOverride.ApplyAsync(s.Admin, allowNegativeStock: true);
        var issue = await s.SubmitAsync(s.Clerk, await s.DraftIssueAsync(s.Clerk, fixture.Reference.DefaultWarehouseId, new Line(product.Id, 2)));

        (await approverClient.Documents.ApproveWithReasonAsync(issue.Id, "Count pending")).ShouldBeOk();
        (await adminClient.Documents.PostAsync(issue.Id)).ShouldBeOk();

        var trail = (await EntriesAsync(entityType: AuditEntities.StockDocument)).Where(e => e.EntityId == issue.No).ToList();
        var approved = trail.Single(e => e.Action == AuditActions.DocumentApproved);
        Assert.Equal(approver.Username, approved.Actor);
        Assert.Contains("Count pending", approved.After);
        var posted = trail.Single(e => e.Action == AuditActions.DocumentPosted);
        Assert.Contains("OverrideUsed", posted.After);
        Assert.Contains("true", posted.After, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Count pending", posted.After);
    }

    // ---------- master data ----------

    [Fact]
    public async Task Creating_and_editing_a_product_records_the_row_with_before_and_after_values()
    {
        var (account, client) = await SignedInAsync(Roles.Admin);
        var builder = new ProductBuilder(fixture.Reference).WithSafetyStock(5);
        var product = await builder.CreateAsync(client);
        var edit = ProductBuilder.From(fixture.Reference, product).WithName("Audited rename").WithSafetyStock(8).Build();
        (await client.Products.UpdateAsync(product.Id, edit)).ShouldBeOk();

        var entries = (await EntriesAsync(account.Username, nameof(Product))).Where(e => e.EntityId == product.Id.ToString()).ToList();

        var created = Assert.Single(entries, e => e.Action == AuditActions.RowCreated);
        Assert.Contains(product.Sku, created.After);
        using (var createdPayload = JsonDocument.Parse(created.After!))
            Assert.Equal(product.Id, createdPayload.RootElement.GetProperty("Id").GetInt32()); // the generated id, not 0
        Assert.Null(created.Before);
        var updated = Assert.Single(entries, e => e.Action == AuditActions.RowUpdated);
        using var before = JsonDocument.Parse(updated.Before!);
        using var after = JsonDocument.Parse(updated.After!);
        Assert.Equal(product.Name, before.RootElement.GetProperty("Name").GetString());
        Assert.Equal("Audited rename", after.RootElement.GetProperty("Name").GetString());
        Assert.Equal(5m, before.RootElement.GetProperty("SafetyStock").GetDecimal());
        Assert.Equal(8m, after.RootElement.GetProperty("SafetyStock").GetDecimal());
        Assert.False(after.RootElement.TryGetProperty("Sku", out _), "Only changed fields belong in the update payload.");
    }

    [Fact]
    public async Task Warehouse_partner_category_and_unit_edits_are_audited_with_the_actor()
    {
        var (account, client) = await SignedInAsync(Roles.Admin);
        var category = (await client.Categories.CreateAsync(Unique.Name("AuditCat"))).Data<Ims.Application.Features.Catalog.CategoryDto>();
        (await client.Categories.UpdateAsync(category.Id, category.Name + " renamed")).ShouldBeOk();
        var warehouse = (await client.Warehouses.CreateAsync(new Ims.Application.Features.Warehouses.WarehouseSaveRequest(Unique.Code("A"), "Audit WH", null, null, true)))
            .Data<Ims.Application.Features.Warehouses.WarehouseDto>();
        (await client.Warehouses.UpdateAsync(warehouse.Id, new Ims.Application.Features.Warehouses.WarehouseSaveRequest(warehouse.Code, "Audit WH", null, null, false))).ShouldBeOk();

        var entries = await EntriesAsync(account.Username);

        Assert.Contains(entries, e => e.EntityType == nameof(Category) && e.Action == AuditActions.RowCreated && e.EntityId == category.Id.ToString());
        Assert.Contains(entries, e => e.EntityType == nameof(Category) && e.Action == AuditActions.RowUpdated && e.After!.Contains("renamed"));
        var deactivation = Assert.Single(entries, e => e.EntityType == nameof(Warehouse) && e.Action == AuditActions.RowUpdated);
        Assert.Contains("true", deactivation.Before!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false", deactivation.After!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creating_a_partner_is_audited_with_the_actor_for_clerks_and_supervisors()
    {
        foreach (var role in new[] { Roles.Clerk, Roles.Supervisor })
        {
            var (account, client) = await SignedInAsync(role);
            var name = Unique.Name("AuditPartner");

            var partner = (await client.Partners.CreateAsync(new Ims.Application.Features.Partners.PartnerSaveRequest(PartnerType.Supplier, name, "contact", true)))
                .Data<Ims.Application.Features.Partners.PartnerDto>();

            var entries = await EntriesAsync(account.Username, nameof(Partner));
            var created = Assert.Single(entries, e => e.Action == AuditActions.RowCreated && e.EntityId == partner.Id.ToString());
            Assert.Equal(account.Username, created.Actor);
            Assert.Contains(name, created.After);
            Assert.True(Math.Abs((DateTime.UtcNow - created.At).TotalMinutes) < 10, $"At {created.At:o} is not recent UTC");
        }
    }

    [Fact]
    public async Task A_refused_partner_edit_by_a_clerk_leaves_no_audit_entry()
    {
        var (account, client) = await SignedInAsync(Roles.Clerk);
        var partner = (await client.Partners.CreateAsync(new Ims.Application.Features.Partners.PartnerSaveRequest(PartnerType.Customer, Unique.Name("AuditPartner"), null, true)))
            .Data<Ims.Application.Features.Partners.PartnerDto>();

        (await client.Partners.UpdateAsync(partner.Id, new Ims.Application.Features.Partners.PartnerSaveRequest(PartnerType.Customer, partner.Name, null, false))).ShouldBe(System.Net.HttpStatusCode.Forbidden);

        var entries = await EntriesAsync(account.Username, nameof(Partner));
        Assert.DoesNotContain(entries, e => e.Action == AuditActions.RowUpdated && e.EntityId == partner.Id.ToString());
    }

    [Fact]
    public async Task Settings_and_approval_rule_changes_are_audited()
    {
        var (account, client) = await SignedInAsync(Roles.Admin);
        var original = (await client.Settings.GetAsync()).Data<SettingsDto>();

        try
        {
            (await client.Settings.UpdateAsync(original with { AllowSelfApproval = !original.AllowSelfApproval })).ShouldBeOk();
            var entries = await EntriesAsync(account.Username);

            var change = Assert.Single(entries, e => e.Action == AuditActions.SettingsUpdated);
            Assert.Contains(original.AllowSelfApproval.ToString(), change.Before, StringComparison.OrdinalIgnoreCase);
            Assert.Contains((!original.AllowSelfApproval).ToString(), change.After, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(entries, e => e.EntityType == nameof(SystemSetting) && e.Action == AuditActions.RowUpdated);
        }
        finally
        {
            (await client.Settings.UpdateAsync(original)).ShouldBeOk();
        }
    }

    [Fact]
    public async Task User_and_permission_changes_are_audited_and_never_contain_password_data()
    {
        var (adminAccount, adminClient) = await SignedInAsync(Roles.Admin);
        var target = await _accounts.CreateAsync(Roles.Clerk);
        // Change the role (a permission change) and the password.
        var promote = new Ims.Application.Features.Users.UserSaveRequest(target.Username, target.Email, target.Username, Roles.Supervisor, true,
            "Another-Passw0rd-1!", target.WarehouseIds.ToList());
        (await adminClient.Users.UpdateAsync(target.Id, promote)).ShouldBeOk();

        var entries = await EntriesAsync(entityType: AuditEntities.User);

        var businessEvent = Assert.Single(entries, e => e.Action == AuditActions.UserUpdated && e.EntityId == target.Id.ToString());
        Assert.Equal(adminAccount.Username, businessEvent.Actor);
        Assert.Contains(Roles.Clerk, businessEvent.Before);
        Assert.Contains(Roles.Supervisor, businessEvent.After);
        var allText = string.Join("\n", entries.Select(e => e.Before + "\n" + e.After));
        Assert.DoesNotContain("PasswordHash", allText);
        Assert.DoesNotContain(target.Password, allText);
        Assert.DoesNotContain("Another-Passw0rd-1!", allText);
    }

    [Fact]
    public async Task Imports_are_audited_as_a_bulk_event()
    {
        var (account, client) = await SignedInAsync(Roles.Admin);
        var file = Xlsx.Create("Products", ["SKU", "Name"], [Unique.Sku("AUD"), "Audited import"]);

        (await client.Import.ProductsAsync(file, commit: true)).ShouldBeOk();

        var entries = await EntriesAsync(account.Username);
        Assert.Contains(entries, e => e.Action == AuditActions.ImportProducts && e.EntityId == AuditEntities.Bulk);
    }

    // ---------- reading the log ----------

    [Fact]
    public async Task The_audit_log_can_be_filtered_by_actor_and_entity_and_pages_newest_first()
    {
        var (account, client) = await SignedInAsync(Roles.Admin);
        for (var i = 0; i < 3; i++) await new ProductBuilder(fixture.Reference).CreateAsync(client);

        var admin = await fixture.AsAsync(Roles.Admin);
        var firstPage = (await admin.Audit.QueryAsync(account.Username, nameof(Product), pageSize: 2)).Data<AuditPage>();

        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.Total >= 3);
        Assert.All(firstPage.Items, e => Assert.Equal((account.Username, nameof(Product)), (e.Actor, e.EntityType)));
        Assert.True(firstPage.Items[0].Id > firstPage.Items[1].Id, "Newest entry should come first.");
    }
}
