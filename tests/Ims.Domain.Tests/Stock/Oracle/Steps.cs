namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>Short builders for the scripted scenarios, so a scenario reads like the posting log it stands for.</summary>
internal static class Steps
{
    public const string DefaultOverrideReason = "customer collecting today";
    public const string DefaultReversalReason = "Entered twice";

    public static OracleLine L(int product, decimal qty, decimal? cost = null, int? uom = null) => new(product, qty, cost, uom);

    public static OracleStep Receive(string doc, params OracleLine[] lines) => new(StepKind.Receipt, doc, lines);

    public static OracleStep Issue(string doc, params OracleLine[] lines) => new(StepKind.Issue, doc, lines);

    public static OracleStep Reverse(string doc, string reason = DefaultReversalReason) => new(StepKind.Reverse, doc, [], Reason: reason);

    /// <summary>Below-zero stock is allowed by the setting, the poster may override it, and the approver recorded a reason.</summary>
    public static OracleStep Overridden(this OracleStep step, string reason = DefaultOverrideReason) =>
        step with { AllowNegative = true, CanOverride = true, Approval = ApprovalState.Override, Reason = reason };

    public static OracleStep NegativeAllowed(this OracleStep step) => step with { AllowNegative = true };
}
