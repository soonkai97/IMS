namespace Ims.Domain;

/// <summary>One reason a document needs approval: <paramref name="Actual"/> reached <paramref name="Threshold"/> of a rule that asks for <paramref name="Tier"/>.</summary>
public record ApprovalTrigger(ApprovalMetric Metric, decimal Threshold, decimal Actual, ApproverTier Tier);
