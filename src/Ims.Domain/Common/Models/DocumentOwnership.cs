namespace Ims.Domain;

/// <summary>
/// Whose a document or an order is to take back (recall). The one "mine" rule of the stock document, the purchase order and the sales order, which used to be written three times in the access
/// services of Application, each reading the persistence column <c>RecRevisor</c>. It is authorization input, so it must not drift: a person owns it when they submitted it; a Draft is theirs as well
/// when they made it or were the last to change it (a recall names no submitter afterwards, so their retry must still count). Anyone else owns nothing, in every status.
/// </summary>
/// <param name="CreatedBy">User id of the creator.</param>
/// <param name="SubmittedBy">User id of the submitter; null while nobody has submitted it.</param>
/// <param name="LastChangedBy">User id of whoever last changed the row (the audit column <c>RecRevisor</c>); null when the system did.</param>
/// <param name="IsDraft">Whether it is a Draft now.</param>
public readonly record struct DocumentOwnership(int CreatedBy, int? SubmittedBy, int? LastChangedBy, bool IsDraft)
{
    public bool IsMine(int userId) => SubmittedBy == userId || (IsDraft && (CreatedBy == userId || LastChangedBy == userId));
}
