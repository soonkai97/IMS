namespace Ims.Domain;

/// <summary>
/// The facts about a sales order that decide what a user may do with it (<see cref="SalesOrderActionPolicy"/>). The same few values describe the entity, the single-order DTO and a list row, so
/// all three ask the policy the same question. What is delivered against the order joins them with the deliveries (PL3.5 to PL3.7).
/// </summary>
/// <param name="CreatedBy">User id of the creator.</param>
/// <param name="SubmittedBy">User id of the submitter; null while the order is a Draft.</param>
public readonly record struct SalesOrderActionContext(SalesOrderStatus Status, bool RequiresApproval, bool IsLarge, int CreatedBy, int? SubmittedBy);
