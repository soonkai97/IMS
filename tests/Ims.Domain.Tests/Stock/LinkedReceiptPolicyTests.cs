namespace Ims.Domain.Tests.Stock;

/// <summary>What the link to a purchase order takes away from a receipt's actions: by permission and module, never by role name.</summary>
public class LinkedReceiptPolicyTests
{
    private const int Me = 42;

    private static ActorRights With(params string[] codes) => new(Me, EffectivePermissions.Compute(codes));

    [Theory]
    [InlineData(true, true, LinkedReceiptRefusal.None)]
    [InlineData(false, true, LinkedReceiptRefusal.NeedsPoRead)]
    [InlineData(true, false, LinkedReceiptRefusal.ModuleOff)]
    [InlineData(false, false, LinkedReceiptRefusal.NeedsPoRead)] // no po.read is answered first, as the save has always done
    public void Edit_NeedsPoReadAndTheModule(bool mayRead, bool moduleOn, LinkedReceiptRefusal expected) =>
        Assert.Equal(expected, LinkedReceiptPolicy.Refusal(DocumentActions.Edit, mayRead, moduleOn));

    [Theory]
    [InlineData(DocumentActions.Submit)]
    [InlineData(DocumentActions.Post)]
    public void SubmitAndPost_NeedTheModuleNotPoRead(string action)
    {
        Assert.Equal(LinkedReceiptRefusal.None, LinkedReceiptPolicy.Refusal(action, mayReadOrders: false, purchasingOn: true));
        Assert.Equal(LinkedReceiptRefusal.ModuleOff, LinkedReceiptPolicy.Refusal(action, mayReadOrders: true, purchasingOn: false));
    }

    [Theory]
    [InlineData(DocumentActions.Recall)]
    [InlineData(DocumentActions.Cancel)]
    [InlineData(DocumentActions.Approve)]
    [InlineData(DocumentActions.Reject)]
    [InlineData(DocumentActions.Reverse)]
    public void TheOtherActionsNeverLookAtTheOrder(string action) =>
        Assert.Equal(LinkedReceiptRefusal.None, LinkedReceiptPolicy.Refusal(action, mayReadOrders: false, purchasingOn: false));

    [Fact]
    public void AllowedActions_OfALinkedDraft_DropEditWithoutPoReadAndEditAndSubmitWhileTheModuleIsOff()
    {
        var draft = new DocumentActionContext(DocumentStatus.Draft, false, false, Me, null);
        string[] Offered(ActorRights actor, LinkedReceiptContext? link) => DocumentActionPolicy.AllowedActions(actor, draft, false, true, link).Order().ToArray();

        var blind = With(Permissions.DocumentCreate);
        var reader = With(Permissions.DocumentCreate, Permissions.PoRead);

        Assert.Equal([DocumentActions.Cancel, DocumentActions.Edit, DocumentActions.Submit], Offered(blind, null)); // not linked: unchanged
        Assert.Equal([DocumentActions.Cancel, DocumentActions.Submit], Offered(blind, new LinkedReceiptContext(true)));
        Assert.Equal([DocumentActions.Cancel, DocumentActions.Edit, DocumentActions.Submit], Offered(reader, new LinkedReceiptContext(true)));
        Assert.Equal([DocumentActions.Cancel], Offered(reader, new LinkedReceiptContext(false)));
        Assert.Equal([DocumentActions.Cancel], Offered(blind, new LinkedReceiptContext(false)));
    }
}
