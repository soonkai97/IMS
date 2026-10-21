namespace Ims.Domain;

/// <summary>
/// The facts about a purchase order that decide what a user may do with it (<see cref="PurchaseOrderActionPolicy"/>). The same few values describe the entity, the single-order DTO and a
/// list row, so all three ask the policy the same question.
/// </summary>
/// <param name="CreatedBy">User id of the creator.</param>
/// <param name="SubmittedBy">User id of the submitter; null while the order is a Draft.</param>
/// <param name="AnythingReceived">A Posted receipt has brought stock in against the order (some line has a received quantity above zero). It decides only whether an Approved order may be closed short; the entity does not know it (computed from the ledger side), so it is false unless the caller says otherwise.</param>
public readonly record struct PurchaseOrderActionContext(PurchaseOrderStatus Status, bool RequiresApproval, bool IsLarge, int CreatedBy, int? SubmittedBy, bool AnythingReceived = false);
