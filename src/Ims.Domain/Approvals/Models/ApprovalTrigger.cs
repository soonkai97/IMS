namespace Ims.Domain;

/// <summary>One reason a document needs approval: <paramref name="Actual"/> reached <paramref name="Threshold"/> of a rule that needs <paramref name="RoleRequired"/>.</summary>
public record ApprovalTrigger(ApprovalMetric Metric, decimal Threshold, decimal Actual, string RoleRequired);
