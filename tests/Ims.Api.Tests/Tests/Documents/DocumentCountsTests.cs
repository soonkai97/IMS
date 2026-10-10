using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Application.Features.Documents;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Documents;

/// <summary>GET /api/documents/counts: the numbers behind the Home tiles and menu badges, in one call.</summary>
[Collection(ApiCollection.Name)]
public class DocumentCountsTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    private static async Task<DocumentCounts> CountsAsync(ApiClient client) => (await client.Documents.CountsAsync()).Data<DocumentCounts>();

    [Fact]
    public async Task A_user_with_no_documents_has_nothing_to_return_and_no_drafts()
    {
        var factory = new AccountFactory(fixture);
        var (clerk, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));

        var counts = await CountsAsync(clerk);

        Assert.Equal((0, 0, 0), (counts.ReturnedToMe, counts.MyDrafts, counts.ApprovalsWaiting));
    }

    [Fact]
    public async Task My_drafts_and_returned_to_me_count_only_my_own_drafts()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var factory = new AccountFactory(fixture);
        var (alice, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));
        var (bob, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));
        var product = await s.NewProductAsync();
        var one = await s.DraftReceiptAsync(alice, Warehouse, new Line(product.Id, 1, 1m));
        var two = await s.DraftReceiptAsync(alice, Warehouse, new Line(product.Id, 1, 1m));
        var three = await s.DraftReceiptAsync(alice, Warehouse, new Line(product.Id, 1, 1m));
        await s.DraftReceiptAsync(bob, Warehouse, new Line(product.Id, 1, 1m));
        var start = await CountsAsync(alice);
        Assert.Equal((0, 3), (start.ReturnedToMe, start.MyDrafts)); // bob's draft is not alice's

        // One goes to review and comes back, one is submitted and waits, one is cancelled.
        await s.SubmitAsync(alice, one);
        (await s.Supervisor.Documents.RejectAsync(one.Id, "Check the quantity")).ShouldBeOk();
        await s.SubmitAsync(alice, two);
        (await alice.Documents.CancelAsync(three.Id)).ShouldBeOk();

        var counts = await CountsAsync(alice);

        Assert.Equal(1, counts.ReturnedToMe);
        Assert.Equal(1, counts.MyDrafts); // the returned one is still a draft; the submitted and cancelled ones are not
    }

    [Fact]
    public async Task A_resubmitted_document_stops_counting_as_returned()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var factory = new AccountFactory(fixture);
        var (clerk, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk));
        var product = await s.NewProductAsync();
        var draft = await s.DraftReceiptAsync(clerk, Warehouse, new Line(product.Id, 1, 1m));
        await s.SubmitAsync(clerk, draft);
        (await s.Supervisor.Documents.RejectAsync(draft.Id, "Check the quantity")).ShouldBeOk();
        Assert.Equal(1, (await CountsAsync(clerk)).ReturnedToMe);

        await s.SubmitAsync(clerk, draft);

        var counts = await CountsAsync(clerk);
        Assert.Equal((0, 0), (counts.ReturnedToMe, counts.MyDrafts));
    }

    [Fact]
    public async Task Posted_today_counts_documents_posted_since_local_midnight_whoever_posted_them()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var before = (await CountsAsync(s.Supervisor)).PostedToday;

        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        await s.IssueAsync(Warehouse, new Line(product.Id, 2));
        await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, 1, 1m)); // a draft does not count

        Assert.Equal(before + 2, (await CountsAsync(s.Supervisor)).PostedToday);
        Assert.Equal(before + 2, (await CountsAsync(s.Clerk)).PostedToday);
    }

    [Fact]
    public async Task A_reversed_document_still_counts_as_posted_today()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        var received = await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));
        var before = (await CountsAsync(s.Admin)).PostedToday;

        (await s.Supervisor.Documents.ReverseAsync(received.Id, "Entered twice")).ShouldBeOk();

        Assert.Equal(before, (await CountsAsync(s.Admin)).PostedToday);
    }

    [Fact]
    public async Task Approvals_waiting_is_what_the_caller_could_approve_and_zero_for_roles_that_cannot_approve()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var supervisorRule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 500m, Roles.Supervisor);
        await using var ownerRule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 900m, Roles.Owner);
        var supervisorBefore = (await CountsAsync(s.Supervisor)).ApprovalsWaiting;
        var ownerBefore = (await CountsAsync(s.Owner)).ApprovalsWaiting;
        var adminBefore = (await CountsAsync(s.Admin)).ApprovalsWaiting;

        await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 600, 1m)));  // needs a supervisor
        await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1000, 1m))); // large: needs the owner
        await s.SubmitAsync(s.Clerk, await s.DraftReceiptAsync(s.Clerk, Warehouse, new Line(product.Id, 1, 1m)));    // no approval

        Assert.Equal(supervisorBefore + 1, (await CountsAsync(s.Supervisor)).ApprovalsWaiting);
        Assert.Equal(ownerBefore + 1, (await CountsAsync(s.Owner)).ApprovalsWaiting);
        Assert.Equal(adminBefore + 2, (await CountsAsync(s.Admin)).ApprovalsWaiting);
        Assert.Equal(0, (await CountsAsync(s.Clerk)).ApprovalsWaiting);
        Assert.Equal(0, (await CountsAsync(s.Staff)).ApprovalsWaiting);
    }

    [Fact]
    public async Task Approvals_waiting_leaves_out_my_own_submissions_while_self_approval_is_off()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await using var rule = await ApprovalRuleScope.AddAsync(s.Admin, DocumentType.Receipt, null, 500m, Roles.Supervisor);
        var before = (await CountsAsync(s.Supervisor)).ApprovalsWaiting;

        await s.SubmitAsync(s.Supervisor, await s.DraftReceiptAsync(s.Supervisor, Warehouse, new Line(product.Id, 600, 1m)));

        Assert.Equal(before, (await CountsAsync(s.Supervisor)).ApprovalsWaiting);

        await using (await SettingsOverride.ApplyAsync(s.Admin, allowSelfApproval: true))
            Assert.Equal(before + 1, (await CountsAsync(s.Supervisor)).ApprovalsWaiting);
    }

    [Fact]
    public async Task Counts_stay_inside_the_callers_warehouses()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var other = await s.NewWarehouseAsync();
        var factory = new AccountFactory(fixture);
        var (clerk, _) = await factory.SignInAsync(await factory.CreateAsync(Roles.Clerk, [Warehouse]));
        var product = await s.NewProductAsync();
        var before = (await CountsAsync(clerk)).PostedToday;
        var adminBefore = (await CountsAsync(s.Admin)).PostedToday;

        var draft = await s.DraftReceiptAsync(s.Admin, other.Id, new Line(product.Id, 1, 1m));
        (await s.Admin.Documents.SubmitAsync(draft.Id)).ShouldBeOk();
        (await s.Admin.Documents.PostAsync(draft.Id)).ShouldBeOk();

        Assert.Equal(adminBefore + 1, (await CountsAsync(s.Admin)).PostedToday);
        Assert.Equal(before, (await CountsAsync(clerk)).PostedToday);
    }

    [Fact]
    public async Task The_counts_need_a_sign_in()
    {
        (await fixture.Anonymous().Documents.CountsAsync()).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
