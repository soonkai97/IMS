using Ims.Domain;
namespace Ims.Application;

/// <summary>
/// The deprecated <c>roleRequired</c> text a rule and an approval trigger still show (ADR roles-permissions 2.7): the system role that holds a tier by default. It is read by clients only (the
/// request no longer takes it) and by nothing else in the application; it goes, with the two response fields, when the web stops printing it (P04, web part).
/// </summary>
public static class ApproverTierNames
{
    public static string RoleOf(ApproverTier tier) => tier == ApproverTier.Large ? SystemRoleCodes.Owner : SystemRoleCodes.Supervisor;
}
