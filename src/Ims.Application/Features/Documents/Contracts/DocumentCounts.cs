namespace Ims.Application.Features.Documents;

/// <summary>The numbers behind the Home tiles and menu badges, for the signed-in user, within the warehouses they may see.</summary>
/// <param name="ReturnedToMe">Drafts I created that a reviewer sent back.</param>
/// <param name="MyDrafts">All my Drafts (returned ones included).</param>
/// <param name="PostedToday">Documents posted since local midnight (Asia/Kuala_Lumpur, BR-10), whoever posted them.</param>
/// <param name="ApprovalsWaiting">Submitted documents held for approval that I could act on: not mine to approve when self-approval is off, only large ones for the Owner, none large for a Supervisor, zero for roles that cannot approve.</param>
public record DocumentCounts(int ReturnedToMe, int MyDrafts, int PostedToday, int ApprovalsWaiting);
