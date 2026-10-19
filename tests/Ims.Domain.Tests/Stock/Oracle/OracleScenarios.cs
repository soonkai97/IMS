using static Ims.Domain.Tests.Stock.Oracle.OracleProducts;
using static Ims.Domain.Tests.Stock.Oracle.Steps;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// The scripted sequences of the oracle: receipts, issues, reversals, negative-stock overrides, unit conversions and documents with several lines of the same product.
/// Each is a short story whose name says what it pins. Adding a scenario is fine (generate its rows and review them); editing or removing one is not, because the committed
/// files hold the answers of the engine as it was before the lot work and the pilot slices.
/// </summary>
internal static class OracleScenarios
{
    private const int Pcs = TestData.PcsId;
    private const int Carton = TestData.CartonId;

    public static readonly IReadOnlyList<OracleScenario> All =
    [
        new("receipt-single", [
            Receive("R1", L(Plain, 10, 2.50m)),
            Issue("I1", L(Plain, 4))]),

        new("weighted-average", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 10, 4m)),
            Issue("I1", L(Plain, 5)),
            Receive("R3", L(Plain, 5, 6m)),
            Issue("I2", L(Plain, 20))]),

        new("average-rounds-half-away-from-zero", [
            Receive("R1", L(Plain, 3, 1.00m)),
            Receive("R2", L(Plain, 4, 1.10m)),
            Receive("R3", L(Plain, 7, 0.01m)),
            Receive("R4", L(Plain, 1, 0.02m)),
            Receive("R5", L(Plain, 2, 0.01m)),
            Issue("I1", L(Plain, 5))]),

        new("average-at-an-exact-midpoint-rounds-away-from-zero", [
            Receive("R1", L(Carton7, 1, 1.00m, Carton)),
            Issue("I1", L(Carton7, 6)),
            Receive("R2", L(Carton7, 1, 0.01m))]),

        new("stock-coming-back-or-in-at-the-average-leaves-it", [
            Receive("R1", L(Plain, 3, 1.00m)),
            Receive("R2", L(Plain, 4, 1.10m)),
            Issue("I1", L(Plain, 3)),
            Reverse("I1"),
            Receive("R3", L(Plain, 5, 1.00m)),
            Issue("I2", L(Plain, 4)),
            Receive("R4", L(Plain, 1, 1.06m))]),

        new("receipt-into-empty-stock-takes-the-new-cost", [
            Receive("R1", L(Plain, 10, 2m)),
            Issue("I1", L(Plain, 10)),
            Receive("R2", L(Plain, 5, 7m)),
            Issue("I2", L(Plain, 5)),
            Receive("R3", L(Plain, 2, 9m))]),

        new("receipt-without-a-cost", [
            Receive("R1", L(Plain, 5)),
            Receive("R2", L(Plain, 5, 4m)),
            Issue("I1", L(Plain, 2))]),

        new("carton-converts-quantity-and-spreads-cost", [
            Receive("R1", L(Carton12, 3, 10m, Carton)),
            Issue("I1", L(Carton12, 6)),
            Issue("I2", L(Carton12, 1, uom: Carton)),
            Receive("R2", L(Carton12, 24, 0.90m, Pcs))]),

        new("carton-of-seven-has-no-exact-piece-cost", [
            Receive("R1", L(Carton7, 2, 3.33m, Carton)),
            Receive("R2", L(Carton7, 1, 10m, Carton)),
            Receive("R3", L(Carton7, 5, 1.01m)),
            Issue("I1", L(Carton7, 1, uom: Carton)),
            Issue("I2", L(Carton7, 3))]),

        new("unit-without-a-conversion-is-refused", [
            Receive("R1", L(Plain, 5, 1m, Carton)),
            Receive("R2", L(Plain, 5, 1m))]),

        new("lines-of-the-same-product-post-in-line-order", [
            Receive("R1", L(Plain, 5, 10m), L(Plain, 5, 20m), L(Plain, 10, 30m)),
            Issue("I1", L(Plain, 3), L(Plain, 4), L(Plain, 5)),
            Receive("R2", L(Plain, 1, 40m), L(Plain, 1, 50m))]),

        new("issue-lines-of-the-same-product-fail-together", [
            Receive("R1", L(Plain, 10, 2m)),
            Issue("I1", L(Plain, 4), L(Plain, 4), L(Plain, 4)),
            Issue("I2", L(Plain, 4), L(Plain, 4), L(Plain, 2))]),

        new("lines-post-in-ascending-product-order", [
            Receive("R1", L(Carton7, 1, 7m, Carton), L(Plain, 2, 3m), L(Carton12, 1, 12m, Carton), L(PlainB, 4, 1m)),
            Issue("I1", L(PlainB, 1), L(Carton12, 2), L(Plain, 1), L(Carton7, 3))]),

        new("insufficient-stock-is-refused-and-leaves-no-trace", [
            Receive("R1", L(Plain, 1, 5m)),
            Issue("I1", L(Plain, 4)),
            Issue("I2", L(Plain, 4)) with { CanOverride = true, Approval = ApprovalState.Override, Reason = DefaultOverrideReason },
            Issue("I3", L(Plain, 1))]),

        new("negative-stock-with-an-override", [
            Receive("R1", L(Plain, 2, 5m)),
            Issue("I1", L(Plain, 5)).Overridden(),
            Issue("I2", L(Plain, 1)).Overridden("recount pending"),
            Receive("R2", L(Plain, 10, 8m)),
            Issue("I3", L(Plain, 1))]),

        new("negative-stock-needs-a-supervisor", [
            Receive("R1", L(Plain, 2, 5m)),
            Issue("I1", L(Plain, 5)) with { AllowNegative = true, CanOverride = false, Approval = ApprovalState.Override, Reason = DefaultOverrideReason }]),

        new("negative-stock-needs-the-approvers-reason", [
            Receive("R1", L(Plain, 2, 5m)),
            Issue("I1", L(Plain, 5)) with { AllowNegative = true, CanOverride = true, Approval = ApprovalState.Approved },
            Issue("I2", L(Plain, 5)) with { AllowNegative = true, CanOverride = true }]),

        new("issuing-exactly-what-is-on-hand-needs-no-override", [
            Receive("R1", L(Plain, 4, 2.5m)),
            Issue("I1", L(Plain, 4)),
            Issue("I2", L(Plain, 1)).Overridden(),
            Issue("I3", L(Plain, 1), L(Plain, 1)).Overridden()]),

        new("receipt-into-negative-stock-takes-the-new-cost", [
            Receive("R1", L(Plain, 1, 3m)),
            Issue("I1", L(Plain, 4)).Overridden(),
            Receive("R2", L(Plain, 2, 9m)),
            Receive("R3", L(Plain, 5, 6m))]),

        new("a-document-waiting-for-approval-cannot-post", [
            Receive("R1", L(Plain, 5, 2m)) with { Approval = ApprovalState.Pending },
            Receive("R2", L(Plain, 5, 2m)) with { Approval = ApprovalState.Approved },
            Reverse("R1"),
            Reverse("R2")]),

        new("reversing-the-latest-receipt-restores-the-average", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 10, 4m)),
            Reverse("R2"),
            Issue("I1", L(Plain, 3)),
            Reverse("R1")]),

        new("reversing-an-earlier-receipt-shares-out-the-value", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 10, 4m)),
            Receive("R3", L(Plain, 10, 9m)),
            Reverse("R1"),
            Reverse("R3"),
            Reverse("R2")]),

        new("reversing-an-earlier-receipt-after-issues-can-lift-the-average", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 10, 8m)),
            Issue("I1", L(Plain, 2)),
            Reverse("R1"),
            Reverse("R2"),
            Issue("I2", L(Plain, 8))]),

        new("reversing-a-receipt-that-stock-already-left-is-blocked", [
            Receive("R1", L(Plain, 10, 2m), L(PlainB, 10, 3m)),
            Issue("I1", L(Plain, 8), L(PlainB, 9)),
            Reverse("R1"),
            Reverse("R1").NegativeAllowed(),
            Receive("R2", L(Plain, 1, 5m))]),

        new("reversing-an-issue-puts-stock-back-at-its-cost", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 10, 6m)),
            Issue("I1", L(Plain, 12)),
            Reverse("I1"),
            Reverse("R2"),
            Reverse("R1")]),

        new("reversing-a-receipt-with-two-lines-of-the-same-product", [
            Receive("R1", L(Plain, 10, 2m)),
            Receive("R2", L(Plain, 5, 10m), L(Plain, 5, 20m), L(Carton7, 1, 7m, Carton)),
            Reverse("R2"),
            Issue("I1", L(Plain, 10))]),

        new("reversing-a-product-that-never-had-a-cost", [
            Receive("R1", L(Plain, 5)),
            Issue("I1", L(Plain, 7)).Overridden(),
            Reverse("R1").NegativeAllowed(),
            Reverse("I1")]),

        new("reversal-is-refused-twice-for-a-short-reason-and-before-posting", [
            Receive("R1", L(Plain, 5, 2m)),
            Receive("R2", L(Plain, 5, 2m)) with { Approval = ApprovalState.Pending },
            Reverse("R1", "ab"),
            Reverse("R1", "     "),
            Reverse("R2"),
            Reverse("R1", "  Entered twice  "),
            Reverse("R1")]),

        new("two-products-keep-separate-balances", [
            Receive("R1", L(Plain, 4, 1m), L(PlainB, 8, 5m)),
            Receive("R2", L(PlainB, 2, 20m)),
            Issue("I1", L(Plain, 1), L(PlainC, 1)),
            Issue("I2", L(PlainB, 10)),
            Reverse("I2")]),

        new("lines-the-system-refuses-to-build", [
            Receive("R1", L(Plain, 0, 1m)),
            Receive("R2", L(Plain, -1, 1m)),
            Receive("R3", L(Plain, 1, -1m)),
            Receive("R4", L(Plain, 1, 1.005m)),
            Receive("R5", L(Plain, 1, 1_000_000_000.01m)),
            Receive("R6", L(Plain, 100_000_000_000_000m, 1m)),
            Receive("R7", L(Plain, 99_999_999_999_999m, 1_000_000_000m)),
            Receive("R8", L(Plain, 1, 1m), L(Plain, 1, 1m, Carton)),
            Receive("R9", L(Plain, 1, 1_000_000_000m))]),

        new("an-issue-is-valued-at-the-average-and-capped", [
            Receive("R1", L(Plain, 999_999, 1_000_000_000m)),
            Receive("R2", L(Plain, 999_999, 1_000_000_000m)),
            Issue("I1", L(Plain, 1_999_998)),
            Receive("R3", L(Plain, 3, 0.01m)),
            Issue("I2", L(Plain, 3))]),

        new("many-small-receipts-and-issues-drift-nowhere", [
            Receive("R1", L(Carton7, 1, 1.00m, Carton)),
            Receive("R2", L(Carton7, 2, 1.01m, Carton)),
            Receive("R3", L(Carton7, 3, 1.02m, Carton)),
            Issue("I1", L(Carton7, 4)),
            Receive("R4", L(Carton7, 1, 0.99m, Carton)),
            Issue("I2", L(Carton7, 9)),
            Receive("R5", L(Carton7, 13, 0.01m)),
            Reverse("R4"),
            Reverse("R3"),
            Reverse("I1")])
    ];
}
