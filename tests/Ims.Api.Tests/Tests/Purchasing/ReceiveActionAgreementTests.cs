using Ims.Api.Tests.Clients;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using static Ims.Api.Tests.Tests.Purchasing.PurchasingScenario;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// PL2.9: the <c>receive</c> action and <c>POST /api/purchase-orders/{id}/receipt</c> agree. For every state of an order and every kind of caller, the endpoint answers 2xx exactly when the order's
/// <c>allowedActions</c>, read by that same caller, lists <c>receive</c>; a caller who cannot read the order is offered nothing and is refused. The web decides nothing by status or role.
/// </summary>
[Collection(ApiCollection.Name)]
public class ReceiveActionAgreementTests(ApiFixture fixture) : IAsyncLifetime
{
    private PurchasingScenario _p = null!;

    public async Task InitializeAsync() => _p = await PurchasingScenario.CreateAsync(fixture);

    public Task DisposeAsync() => PurchasingScenario.ResetAsync(fixture);

    private ApiClient Forged(params string[] permissions) => fixture.CreateClient(ForgedTokens.Create(fixture.Settings.JwtKey, Roles.Admin, permissions: permissions));

    private async Task<DocumentDto> PostedAsync(PurchaseOrderDto order, decimal qty)
    {
        var receipt = await _p.ReceiptFromAsync(order);
        var line = receipt.Lines.Single();
        var saved = (await _p.Clerk.Documents.UpdateAsync(receipt.Id, Save(receipt, LineFor(line.ProductId, qty, 1, uomId: line.UomId)), receipt.ConcurrencyToken)).Data<DocumentDto>();
        return await _p.PostAsync(saved, _p.Clerk);
    }

    [Fact]
    public async Task The_endpoint_answers_2xx_exactly_when_receive_is_offered_across_states_and_callers()
    {
        var product = await _p.NewProductAsync();
        var draft = await _p.CreateAsync(_p.Warehouse, product.Id, _p.Clerk);
        var submitted = await _p.SubmittedWorthAsync(6000m); // held for approval
        Assert.Equal(PurchaseOrderStatus.Submitted, submitted.Status);
        var approved = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        var partly = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        await PostedAsync(partly, 6m);
        var full = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        await PostedAsync(full, 10m); // everything received: the order closes by itself
        var cancelled = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        await _p.SetStatusAsync(cancelled, PurchaseOrderStatus.Cancelled);
        var closed = await _p.ApprovedAsync((product.Id, 10m, 2m, null));
        await _p.SetStatusAsync(closed, PurchaseOrderStatus.Closed);

        var orders = new (string State, Guid Id, bool Receivable)[]
        {
            ("Draft", draft.Id, false), ("Submitted", submitted.Id, false), ("Approved", approved.Id, true), ("Approved, partly received", partly.Id, true),
            ("Fully received", full.Id, false), ("Cancelled", cancelled.Id, false), ("Closed", closed.Id, false)
        };
        var callers = new (string Name, ApiClient Client, bool MayReceive)[]
        {
            ("Administrator", _p.Admin, true), ("Supervisor", _p.Supervisor, true), ("Clerk", _p.Clerk, true), ("Warehouse staff", _p.Staff, true), ("Owner", _p.Owner, false),
            ("document.create + po.read (custom role)", Forged(Permissions.DocumentCreate, Permissions.PoRead), true),
            ("po.read without document.create", Forged(Permissions.PoRead, Permissions.StockRead), false),
            ("document.create without po.read", Forged(Permissions.DocumentCreate, Permissions.StockRead), false)
        };

        Assert.Equal(PurchaseOrderStatus.Closed, (await _p.ReadAsync(full.Id)).Status);
        foreach (var (state, id, receivable) in orders)
            foreach (var (name, client, mayReceive) in callers)
            {
                var read = await client.PurchaseOrders.GetAsync(id);
                var offered = read.IsSuccess && read.Data<PurchaseOrderDto>().AllowedActions.Contains(DocumentActions.Receive);
                var answer = await client.PurchaseOrders.CreateReceiptAsync(id);

                Assert.True(offered == answer.IsSuccess, $"{state} / {name}: receive offered = {offered}, the endpoint answered {answer.StatusCode}");
                Assert.True(offered == (receivable && mayReceive), $"{state} / {name}: receive offered = {offered}");
            }
    }
}
