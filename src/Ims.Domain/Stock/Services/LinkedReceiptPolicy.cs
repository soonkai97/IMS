namespace Ims.Domain;

/// <summary>
/// What the link to a purchase order takes away from a receipt's actions: the one rule behind both the <c>allowedActions</c> of a linked receipt (<see cref="DocumentActionPolicy.AllowedActions"/>) and the
/// guards of the handlers that refuse it, so the buttons and the API cannot disagree. Only the order-independent part is here (who may read orders, whether the module is on); that the order is still
/// Approved and has quantity left depends on data and is answered when the action is tried (409/400), as for any state-dependent refusal. It reads what the caller may do, never a role name.
/// </summary>
public static class LinkedReceiptPolicy
{
    /// <summary>
    /// Why a document action is refused for a linked receipt, or <see cref="LinkedReceiptRefusal.None"/>. Edit needs <c>po.read</c> (the link and the order's figures are shown to readers only, so anyone else could not even
    /// name the order's lines) and the module on. Submit and post need the module on, and nothing else: they change nothing of the order's lines and answer a caller without <c>po.read</c> in general words.
    /// Recall, cancel, approve, reject and reverse never look at the order.
    /// </summary>
    public static LinkedReceiptRefusal Refusal(string action, bool mayReadOrders, bool purchasingOn) => action switch
    {
        DocumentActions.Edit when !mayReadOrders => LinkedReceiptRefusal.NeedsPoRead,
        DocumentActions.Edit or DocumentActions.Submit or DocumentActions.Post when !purchasingOn => LinkedReceiptRefusal.ModuleOff,
        _ => LinkedReceiptRefusal.None
    };

    /// <summary>The actions of <paramref name="actions"/> that the API will not refuse for the order-independent reasons above.</summary>
    public static List<string> Offered(IEnumerable<string> actions, bool mayReadOrders, bool purchasingOn) =>
        actions.Where(a => Refusal(a, mayReadOrders, purchasingOn) == LinkedReceiptRefusal.None).ToList();
}
