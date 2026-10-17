using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Access;

/// <summary>
/// RP-4a: the <c>role</c> claim decides nothing, not even the approval tier or the warehouse scope. A validly signed token is judged by its <c>perm</c>, <c>allwh</c> and <c>wh</c>
/// claims alone, so a role name that means something to the old code (Admin, Owner) grants nothing the claims do not, and a role code the code has never heard of works.
/// </summary>
[Collection(ApiCollection.Name)]
public class RoleClaimDecidesNothingTests(ApiFixture fixture)
{
    private const string CustomRole = "Night-shift-lead";
    private const decimal Threshold = 500m;
    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    private string Token(string role, IEnumerable<string> permissions, bool allWarehouses = true, IEnumerable<int>? warehouseIds = null) =>
        ForgedTokens.Create(fixture.Settings.JwtKey, role, permissions: permissions, allWarehouses: allWarehouses, warehouseIds: warehouseIds);

    private static string[] Holding(params string[] tiers) => [.. tiers, Permissions.StockRead, Permissions.MasterDataRead];

    /// <summary>A receipt of 600 units submitted by the clerk, so a rule at 500 units fires on it.</summary>
    private async Task<DocumentDto> SubmitAsync(StockScenario s)
    {
        var product = await s.NewProductAsync();
        return await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 600, 1m)));
    }

    private async Task<DocumentStatus> StatusAsync(StockScenario s, DocumentDto doc) => (await s.GetAsync(doc.Id)).Status;

    // ---------- the approval tier ----------

    [Fact]
    public async Task A_token_named_Admin_that_holds_only_the_standard_tier_cannot_approve_or_reject_a_large_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Owner);
        var large = await SubmitAsync(s);
        Assert.True(large.IsLarge);
        var client = fixture.CreateClient(Token(Roles.Admin, Holding(Permissions.DocumentApprove)));

        (await client.Documents.ApproveAsync(large.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await client.Documents.RejectAsync(large.Id, "Not for me")).ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, await StatusAsync(s, large));
    }

    [Fact]
    public async Task A_token_named_Admin_that_holds_only_the_large_tier_cannot_review_a_standard_document()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Supervisor);
        var standard = await SubmitAsync(s);
        Assert.False(standard.IsLarge);
        var client = fixture.CreateClient(Token(Roles.Admin, Holding(Permissions.DocumentApproveLarge)));

        (await client.Documents.ApproveAsync(standard.Id)).ShouldBe(HttpStatusCode.Forbidden);
        Assert.Equal(DocumentStatus.Submitted, await StatusAsync(s, standard));
    }

    [Fact]
    public async Task A_custom_role_with_the_standard_tier_approves_a_standard_document_and_with_the_large_tier_a_large_one()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await using (await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Supervisor))
        {
            var standard = await SubmitAsync(s);
            (await fixture.CreateClient(Token(CustomRole, Holding(Permissions.DocumentApprove))).Documents.ApproveAsync(standard.Id)).ShouldBeOk();
            Assert.Equal(DocumentStatus.Approved, await StatusAsync(s, standard));
        }
        await using (await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Owner))
        {
            var large = await SubmitAsync(s);
            (await fixture.CreateClient(Token(CustomRole, Holding(Permissions.DocumentApproveLarge))).Documents.ApproveAsync(large.Id)).ShouldBeOk();
            Assert.Equal(DocumentStatus.Approved, await StatusAsync(s, large));
        }
    }

    [Fact]
    public async Task A_rule_that_names_the_Admin_needs_both_tiers_whatever_the_token_is_called()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Admin);
        var doc = await SubmitAsync(s);
        Assert.True(doc.IsLarge);

        (await fixture.CreateClient(Token(Roles.Owner, Holding(Permissions.DocumentApproveLarge))).Documents.ApproveAsync(doc.Id)).ShouldBe(HttpStatusCode.Forbidden);
        (await fixture.CreateClient(Token(CustomRole, Holding(Permissions.DocumentApprove, Permissions.DocumentApproveLarge))).Documents.ApproveAsync(doc.Id)).ShouldBeOk();
    }

    // ---------- the warehouse scope ----------

    [Fact]
    public async Task A_token_named_Admin_that_does_not_see_every_warehouse_reads_only_the_warehouses_it_lists()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var doc = await SubmitAsync(s);
        var warehouseId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", Warehouse));
        var otherId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", (await s.NewWarehouseAsync()).Id));

        var listsNone = fixture.CreateClient(Token(Roles.Admin, [Permissions.StockRead], allWarehouses: false));
        var listsOther = fixture.CreateClient(Token(Roles.Admin, [Permissions.StockRead], allWarehouses: false, warehouseIds: [otherId]));
        var listsIt = fixture.CreateClient(Token(Roles.Admin, [Permissions.StockRead], allWarehouses: false, warehouseIds: [warehouseId]));

        (await listsNone.Documents.GetAsync(doc.Id)).ShouldBe(HttpStatusCode.NotFound);
        (await listsOther.Documents.GetAsync(doc.Id)).ShouldBe(HttpStatusCode.NotFound);
        (await listsIt.Documents.GetAsync(doc.Id)).ShouldBeOk();
    }

    [Fact]
    public async Task A_scoped_token_sees_only_its_warehouses_in_the_list_and_a_custom_role_that_sees_all_sees_them_all()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var doc = await SubmitAsync(s);
        var otherId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", (await s.NewWarehouseAsync()).Id));

        var scoped = fixture.CreateClient(Token(Roles.Admin, [Permissions.StockRead], allWarehouses: false, warehouseIds: [otherId]));
        var sees = fixture.CreateClient(Token(CustomRole, [Permissions.StockRead], allWarehouses: true));

        Assert.DoesNotContain(doc.Id, (await scoped.Documents.ListAsync(search: doc.No)).Data<PagedResult<DocumentListItem>>().Items.Select(i => i.Id));
        Assert.Contains(doc.Id, (await sees.Documents.ListAsync(search: doc.No)).Data<PagedResult<DocumentListItem>>().Items.Select(i => i.Id));
    }

    [Fact]
    public async Task A_custom_role_with_a_warehouse_list_approves_inside_it_and_not_outside()
    {
        var s = await StockScenario.CreateAsync(fixture);
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, Threshold, Roles.Supervisor);
        var doc = await SubmitAsync(s);
        var warehouseId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", Warehouse));
        var otherId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", (await s.NewWarehouseAsync()).Id));

        (await fixture.CreateClient(Token(CustomRole, Holding(Permissions.DocumentApprove), allWarehouses: false, warehouseIds: [otherId])).Documents.ApproveAsync(doc.Id))
            .ShouldBe(HttpStatusCode.NotFound);
        (await fixture.CreateClient(Token(CustomRole, Holding(Permissions.DocumentApprove), allWarehouses: false, warehouseIds: [warehouseId])).Documents.ApproveAsync(doc.Id))
            .ShouldBeOk();
    }
}
