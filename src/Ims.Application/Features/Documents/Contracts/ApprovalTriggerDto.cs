using System.Text.Json.Serialization;
using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>
/// One reason a document needs approval: <paramref name="Actual"/> reached <paramref name="Threshold"/> of a rule that asks for <paramref name="ApproverTier"/>.
/// For an amount rule the two numbers are money, so a caller without <c>cost.view</c> (the Warehouse staff) gets them as null: they still see that
/// the document needs approval and which kind of rule fired (<paramref name="Metric"/>), not the amount. A quantity rule is not hidden.
/// <paramref name="RoleRequired"/> is deprecated: the system role that holds the tier by default (<see cref="ApproverTierNames.RoleOf"/>).
/// </summary>
public record ApprovalTriggerDto(ApprovalMetric Metric,
    [property: VisibleWith(Permissions.CostView, OnlyWhen = nameof(ApprovalTriggerDto.IsAmount))] decimal? Threshold,
    [property: VisibleWith(Permissions.CostView, OnlyWhen = nameof(ApprovalTriggerDto.IsAmount))] decimal? Actual,
    ApproverTier ApproverTier,
    string RoleRequired)
{
    [JsonIgnore]
    public bool IsAmount => Metric == ApprovalMetric.Amount;

    public static ApprovalTriggerDto From(ApprovalTrigger trigger) =>
        new(trigger.Metric, trigger.Threshold, trigger.Actual, trigger.Tier, ApproverTierNames.RoleOf(trigger.Tier));
}
