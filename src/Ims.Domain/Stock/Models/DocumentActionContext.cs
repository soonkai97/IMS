namespace Ims.Domain;

/// <summary>
/// The facts about a document that decide what a user may do with it (<see cref="DocumentActionPolicy"/>). The same few values describe
/// the entity, the single-document DTO and a list row, so all three ask the policy the same question.
/// </summary>
/// <param name="CreatedBy">User id of the creator ("own" document).</param>
/// <param name="SubmittedBy">User id of the submitter; null while the document is a Draft.</param>
public readonly record struct DocumentActionContext(DocumentStatus Status, bool RequiresApproval, bool IsLarge, int CreatedBy, int? SubmittedBy);
