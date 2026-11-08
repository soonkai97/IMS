namespace Ims.Domain;

/// <summary>
/// The facts about a sales order that decide what a user may do with it (<see cref="SalesOrderActionPolicy"/>). The same few values describe the entity, the single-order DTO and a list row, so
/// all three ask the policy the same question. What is delivered against the order joins them with the deliveries (PL3.5 to PL3.7).
/// </summary>
/// <param name="CreatedBy">User id of the creator.</param>
/// <param name="SubmittedBy">User id of the submitter; null while the order is a Draft.</param>
/// <param name="DeliveriesBlockCancel">Some delivery of the order is Draft, Submitted, Approved or Posted: cancelling the order is refused (409) while that is so.</param>
/// <param name="DeliveryOpen">Some delivery of the order is Draft, Submitted or Approved: closing the order short is refused (409) until it is posted or cancelled.</param>
/// <param name="AnythingDelivered">A Posted delivery has taken stock out against the order (some line has a delivered quantity above zero). It decides only whether an Approved order may be closed short; the entity does not know it, so it is false unless the caller says otherwise.</param>
/// <param name="AnythingRemaining">Some line still has a quantity left to deliver (ordered less what Posted deliveries took out). It decides only whether a delivery may be created from an Approved order; the entity does not know it (computed from the delivery side), so it is false unless the caller says otherwise.</param>
public readonly record struct SalesOrderActionContext(SalesOrderStatus Status, bool RequiresApproval, bool IsLarge, int CreatedBy, int? SubmittedBy, bool AnythingRemaining = false, bool AnythingDelivered = false, bool DeliveriesBlockCancel = false, bool DeliveryOpen = false);
