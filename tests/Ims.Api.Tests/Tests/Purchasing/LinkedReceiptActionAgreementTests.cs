using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Domain;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// A receipt created from a purchase order: the document endpoints answer 2xx exactly when the document's <c>allowedActions</c>, read by that same caller, lists the action. What the link takes away is
/// decided in one place (<see cref="LinkedReceiptPolicy"/>): edit needs po.read and the Purchasing module on; submit and post need the module on; recall and cancel never look at the order. An ordinary receipt
/// is offered exactly what it was before the link existed. The web decides nothing by role or permission, only by <c>allowedActions</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public class LinkedReceiptActionAgreementTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    /// <param name="Creator">Who makes the drafts: the caller when the caller may (holds po.read and document.create), otherwise the Clerk.</param>
    /// <param name="MayReadOrders">Holds po.read.</param>
    /// <param name="MayChangeThem">May edit, submit and cancel a draft the Creator made (its owner, or document.edit_any).</param>
    /// <param name="IsTheCreator">Is the one who made and submits the drafts (so the only one who may recall).</param>
    private sealed record Caller(string Name, ApiClient Client, ApiClient Creator, bool MayReadOrders, bool MayChangeThem, bool IsTheCreator);

    private Caller[] Callers() =>
    [
        new("Administrator", _p.Admin, _p.Admin, true, true, true),
        new("Supervisor", _p.Supervisor, _p.Supervisor, true, true, true),
        new("Clerk", _p.Clerk, _p.Clerk, true, true, true),
        new("Warehouse staff", _p.Staff, _p.Staff, true, true, true),
        new("custom role: document.edit_any + po.read", Forged(Permissions.StockRead, Permissions.DocumentCreate, Permissions.DocumentEditAny, Permissions.DocumentPost, Permissions.PoRead), _p.Clerk, true, true, false),
        new("custom role: document.edit_any, no po.read", Forged(Permissions.StockRead, Permissions.DocumentCreate, Permissions.DocumentEditAny, Permissions.DocumentPost), _p.Clerk, false, true, false),
        new("custom role: document.create only, no po.read", Forged(Permissions.StockRead, Permissions.DocumentCreate, Permissions.DocumentPost), _p.Clerk, false, false, false)
    ];

    private sealed record Drafts(DocumentDto ToEdit, DocumentDto ToCancel, DocumentDto ToRecall, DocumentDto ToPost);

    /// <summary>Four linked drafts of one fresh order, made by the caller's creator; the third and the fourth are submitted by the creator, so recall and post have a document to act on.</summary>
    private async Task<Drafts> LinkedDraftsAsync(Caller who, Guid productId)
    {
        var order = await _p.ApprovedAsync((productId, 10m, 2m, null));
        var toEdit = await _p.ReceiptFromAsync(order, who.Creator);
        var toCancel = await _p.ReceiptFromAsync(order, who.Creator);
        var toRecall = await _p.ReceiptFromAsync(order, who.Creator);
        var toPost = await _p.ReceiptFromAsync(order, who.Creator);
        toRecall = (await who.Creator.Documents.SubmitAsync(toRecall.Id)).Data<DocumentDto>();
        toPost = (await who.Creator.Documents.SubmitAsync(toPost.Id)).Data<DocumentDto>();
        return new Drafts(toEdit, toCancel, toRecall, toPost);
    }

    private static async Task<IReadOnlyList<string>> OfferedAsync(ApiClient client, Guid id) => (await client.Documents.GetAsync(id)).Data<DocumentDto>().AllowedActions;

    private static async Task AgreeAsync(string context, string action, ApiClient client, Guid id, Func<Task<ApiResponse>> attempt)
    {
        var offered = (await OfferedAsync(client, id)).Contains(action);
        var answer = await attempt();
        Assert.True(offered == answer.IsSuccess, $"{context}: '{action}' offered = {offered}, the endpoint answered {answer.StatusCode}");
    }

    private static DocumentSaveRequest SameLines(DocumentDto draft) =>
        Save(draft, draft.Lines.Select(l => LineFor(l.ProductId, l.Qty, l.PurchaseOrderLineNo, uomId: l.UomId)).ToArray());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_linked_receipts_endpoints_answer_2xx_exactly_when_the_action_is_offered(bool moduleOn)
    {
        var product = await _p.NewProductAsync();
        var callers = Callers();
        var drafts = new List<Drafts>();
        foreach (var who in callers) drafts.Add(await LinkedDraftsAsync(who, product.Id));
        if (!moduleOn) await _p.TurnOffAsync();

        for (var i = 0; i < callers.Length; i++)
        {
            var (who, d) = (callers[i], drafts[i]);
            var context = $"linked draft / {who.Name} / module {(moduleOn ? "on" : "off")}";

            // What is offered on the Draft: edit needs po.read and the module; submit needs the module; cancel needs neither.
            var onDraft = await OfferedAsync(who.Client, d.ToEdit.Id);
            Assert.True(onDraft.Contains(DocumentActions.Edit) == (who.MayChangeThem && who.MayReadOrders && moduleOn), $"{context}: edit offered = {onDraft.Contains(DocumentActions.Edit)}");
            Assert.True(onDraft.Contains(DocumentActions.Submit) == (who.MayChangeThem && moduleOn), $"{context}: submit offered = {onDraft.Contains(DocumentActions.Submit)}");
            Assert.True(onDraft.Contains(DocumentActions.Cancel) == who.MayChangeThem, $"{context}: cancel offered = {onDraft.Contains(DocumentActions.Cancel)}");

            await AgreeAsync(context, DocumentActions.Edit, who.Client, d.ToEdit.Id, () => who.Client.Documents.UpdateAsync(d.ToEdit.Id, SameLines(d.ToEdit), d.ToEdit.ConcurrencyToken));
            await AgreeAsync(context, DocumentActions.Submit, who.Client, d.ToEdit.Id, () => who.Client.Documents.SubmitAsync(d.ToEdit.Id));
            await AgreeAsync(context, DocumentActions.Cancel, who.Client, d.ToCancel.Id, () => who.Client.Documents.CancelAsync(d.ToCancel.Id, "not needed"));

            // Recall is the submitter's alone and does not look at the order; post needs the module on.
            var onSubmitted = await OfferedAsync(who.Client, d.ToRecall.Id);
            Assert.True(onSubmitted.Contains(DocumentActions.Recall) == who.IsTheCreator, $"{context}: recall offered = {onSubmitted.Contains(DocumentActions.Recall)}");
            await AgreeAsync(context, DocumentActions.Recall, who.Client, d.ToRecall.Id, () => who.Client.Documents.RecallAsync(d.ToRecall.Id));
            if (!moduleOn) Assert.DoesNotContain(DocumentActions.Post, await OfferedAsync(who.Client, d.ToPost.Id));
            await AgreeAsync(context, DocumentActions.Post, who.Client, d.ToPost.Id, () => who.Client.Documents.PostAsync(d.ToPost.Id));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_ordinary_receipt_is_offered_what_it_always_was_whatever_the_module_and_po_read(bool moduleOn)
    {
        var product = await _p.NewProductAsync();
        var callers = Callers();
        var plain = new List<DocumentDto>();
        foreach (var who in callers) plain.Add(await _p.Stock.DraftReceiptAsync(who.Creator, _p.Warehouse, new Line(product.Id, 2m, 1m)));
        if (!moduleOn) await _p.TurnOffAsync();

        for (var i = 0; i < callers.Length; i++)
        {
            var (who, draft) = (callers[i], plain[i]);
            var context = $"ordinary draft / {who.Name} / module {(moduleOn ? "on" : "off")}";
            var offered = await OfferedAsync(who.Client, draft.Id);
            Assert.True(offered.Contains(DocumentActions.Edit) == who.MayChangeThem, $"{context}: edit offered = {offered.Contains(DocumentActions.Edit)}");
            Assert.True(offered.Contains(DocumentActions.Submit) == who.MayChangeThem, $"{context}: submit offered = {offered.Contains(DocumentActions.Submit)}");

            var save = Save(draft, LineFor(product.Id, 3m, null, unitCost: 1m));
            await AgreeAsync(context, DocumentActions.Edit, who.Client, draft.Id, () => who.Client.Documents.UpdateAsync(draft.Id, save, draft.ConcurrencyToken));
            await AgreeAsync(context, DocumentActions.Submit, who.Client, draft.Id, () => who.Client.Documents.SubmitAsync(draft.Id));
        }
    }

    [Fact]
    public async Task A_caller_without_po_read_who_may_edit_is_not_offered_edit_on_a_linked_draft_and_the_save_is_still_refused()
    {
        var product = await _p.NewProductAsync();
        var receipt = await _p.ReceiptFromAsync(await _p.ApprovedAsync((product.Id, 10m, 2m, null)));
        var blind = Forged(Permissions.StockRead, Permissions.DocumentCreate, Permissions.DocumentEditAny);

        var seen = (await blind.Documents.GetAsync(receipt.Id)).Data<DocumentDto>();

        Assert.Null(seen.PurchaseOrder); // the link stays hidden from them, as before
        Assert.Equal([DocumentActions.Cancel, DocumentActions.Submit], seen.AllowedActions.Order().ToArray());
        (await blind.Documents.UpdateAsync(receipt.Id, SameLines(receipt), receipt.ConcurrencyToken)).ShouldBe(HttpStatusCode.Forbidden);
    }
}
