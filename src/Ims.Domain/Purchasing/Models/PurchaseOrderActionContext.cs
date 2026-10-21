namespace Ims.Domain;

/// <summary>
/// The facts about a purchase order that decide what a user may do with it (<see cref="PurchaseOrderActionPolicy"/>). The same few values describe the entity, the single-order DTO and a
/// list row, so all three ask the policy the same question.
/// </summary>
/// <param name="CreatedBy">User id of the creator.</param>
/// <param name="SubmittedBy">User id of the submitter; null while the order is a Draft.</param>
public readonly record struct PurchaseOrderActionContext(PurchaseOrderStatus Status, bool RequiresApproval, bool IsLarge, int CreatedBy, int? SubmittedBy);
