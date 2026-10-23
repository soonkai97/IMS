using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

/// <summary>PL2.3: the state machine of a sales order. Draft -> Submitted -> Approved, or straight to Approved when no rule fires; back to Draft by a rejection or a recall; Cancelled from the three open states.</summary>
public class SalesOrderWorkflowTests
{
    private const int Maker = 11;
    private const int Reviewer = 12;

    private static SalesOrder Draft(decimal price = 10m)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Maker, Now);
        order.AssignNumber("PO-WH1-2601-0001");
        order.ReplaceLines([Line(price)]);
        return order;
    }

    private static SalesOrder Submitted(bool large = false)
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, large, Now);
        return order;
    }

    private static SalesOrderLine Line(decimal price, int product = 1) => SalesOrderLine.Create(NewProduct(product, $"SKU-{product}"), 5m, null, price, null);

    // ----- submit -----
    [Fact]
    public void Submitting_with_a_rule_that_fired_waits_for_the_reviewer_and_records_the_submitter_and_the_tier()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: true, isLarge: true, Now);

        Assert.Equal((SalesOrderStatus.Submitted, true, true, (int?)Maker), (order.Status, order.RequiresApproval, order.IsLarge, order.SubmittedBy));
        Assert.Equal(Now, order.SubmittedAt);
        Assert.Null(order.ApprovedBy);
    }

    [Fact]
    public void Submitting_when_no_rule_fired_releases_the_order_at_once_with_no_approver()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: false, isLarge: true, Now);

        Assert.Equal((SalesOrderStatus.Approved, false, false), (order.Status, order.RequiresApproval, order.IsLarge)); // a tier only means something with a rule
        Assert.Equal(((int?)Maker, (int?)null, (DateTime?)Now), (order.SubmittedBy, order.ApprovedBy, order.ApprovedAt));
    }

    [Fact]
    public void A_line_without_a_price_stops_the_submit_and_names_every_such_line_while_the_draft_stays_as_it_was()
    {
        var order = Draft();
        order.ReplaceLines([Line(1m, 1), Line(0m, 2), Line(3m, 3), Line(0m, 4)]);

        var error = Assert.Throws<DomainException>(() => order.Submit(Maker, false, false, Now));

        Assert.Equal(Messages.SalesOrderLinesNeedPrice([2, 4]), error.Message);
        Assert.Contains("Lines 2, 4 have no price", error.Message);
        Assert.Equal((SalesOrderStatus.Draft, (int?)null), (order.Status, order.SubmittedBy));
        order.ReplaceLines([Line(0m)]); // and a draft may still hold 0.00
    }

    [Fact]
    public void One_unpriced_line_is_named_in_the_singular()
    {
        var order = Draft();
        order.ReplaceLines([Line(0m, 7)]);

        Assert.StartsWith("Line 1 has no price.", Assert.Throws<DomainException>(() => order.Submit(Maker, false, false, Now)).Message);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Submitted)]
    [InlineData(SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void Only_a_draft_can_be_submitted(SalesOrderStatus status)
    {
        var order = Draft().WithPrivate(nameof(SalesOrder.Status), status);

        Assert.Equal(Messages.SalesOrderOnlyDraftCanSubmit(status), Assert.Throws<DomainException>(() => order.Submit(Maker, false, false, Now)).Message);
    }

    // ----- approve -----
    [Fact]
    public void A_reviewer_approves_a_submitted_order_and_is_recorded()
    {
        var order = Submitted();

        order.Approve(Reviewer, allowSelfApproval: false, Now.AddHours(1));

        Assert.Equal((SalesOrderStatus.Approved, (int?)Reviewer, (DateTime?)Now.AddHours(1)), (order.Status, order.ApprovedBy, order.ApprovedAt));
    }

    [Fact]
    public void Nobody_approves_their_own_submission_unless_the_setting_allows_it()
    {
        var order = Submitted();

        Assert.Equal(Messages.SalesOrderSelfApproval, Assert.Throws<DomainException>(() => order.Approve(Maker, false, Now)).Message);
        Assert.Equal(SalesOrderStatus.Submitted, order.Status);
        order.Approve(Maker, allowSelfApproval: true, Now);
        Assert.Equal(SalesOrderStatus.Approved, order.Status);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void Only_a_submitted_order_can_be_approved_or_rejected(SalesOrderStatus status)
    {
        var order = Submitted().WithPrivate(nameof(SalesOrder.Status), status);

        Assert.Equal(Messages.SalesOrderOnlySubmittedCanApprove(status), Assert.Throws<DomainException>(() => order.Approve(Reviewer, false, Now)).Message);
        Assert.Equal(Messages.SalesOrderOnlySubmittedCanReject(status), Assert.Throws<DomainException>(() => order.Reject("not this customer", Reviewer, false, Now)).Message);
    }

    // ----- reject -----
    [Fact]
    public void A_rejection_returns_the_order_to_draft_with_the_reason_and_forgets_the_submission()
    {
        var order = Submitted(large: true);

        order.Reject("  price is too high  ", Reviewer, false, Now.AddHours(2));

        Assert.Equal((SalesOrderStatus.Draft, "price is too high", (int?)Reviewer, true), (order.Status, order.RejectionReason, order.RejectedBy, order.IsReturned));
        Assert.Equal(Now.AddHours(2), order.RejectedAt);
        Assert.Equal(((int?)null, (DateTime?)null, false, false), (order.SubmittedBy, order.SubmittedAt, order.RequiresApproval, order.IsLarge));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no")]
    [InlineData("  four  ")]
    public void A_rejection_needs_a_reason_of_five_characters(string? reason)
    {
        var order = Submitted();

        Assert.Throws<DomainException>(() => order.Reject(reason!, Reviewer, false, Now));
        Assert.Equal(SalesOrderStatus.Submitted, order.Status);
    }

    [Fact]
    public void The_submitter_cannot_reject_their_own_order_and_a_returned_draft_submits_again_from_scratch()
    {
        var order = Submitted();
        Assert.Equal(Messages.SalesOrderSelfRejection, Assert.Throws<DomainException>(() => order.Reject("take it back", Maker, false, Now)).Message);

        order.Reject("take it back", Reviewer, false, Now);
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now.AddDays(1));

        Assert.Equal((SalesOrderStatus.Submitted, (string?)null, (int?)null, false), (order.Status, order.RejectionReason, order.RejectedBy, order.IsReturned));
    }

    // ----- recall -----
    [Fact]
    public void A_recall_returns_a_submitted_order_to_draft_without_a_reason()
    {
        var order = Submitted(large: true);

        order.Recall();

        Assert.Equal((SalesOrderStatus.Draft, (string?)null, false, false, (int?)null), (order.Status, order.RejectionReason, order.IsReturned, order.RequiresApproval, order.SubmittedBy));
        Assert.False(order.IsLarge);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft)]
    [InlineData(SalesOrderStatus.Approved)]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void Only_a_submitted_order_can_be_recalled(SalesOrderStatus status)
    {
        var order = Submitted().WithPrivate(nameof(SalesOrder.Status), status);

        Assert.Equal(Messages.SalesOrderOnlySubmittedCanRecall(order.No, status), Assert.Throws<DomainException>(order.Recall).Message);
    }

    // ----- cancel -----
    [Theory]
    [InlineData(null)]
    [InlineData("changed our mind")]
    public void A_draft_or_a_submitted_order_is_cancelled_with_or_without_a_reason(string? reason)
    {
        var draft = Draft();
        var submitted = Submitted();

        draft.Cancel(reason);
        submitted.Cancel(reason);

        Assert.All(new[] { draft, submitted }, o => Assert.Equal((SalesOrderStatus.Cancelled, reason), (o.Status, o.CancelReason)));
    }

    [Fact]
    public void An_approved_order_is_cancelled_only_with_a_reason_of_five_characters_and_keeps_who_approved_it()
    {
        var order = Submitted();
        order.Approve(Reviewer, false, Now);

        Assert.Equal(Messages.SalesOrderCancelReasonRequired, Assert.Throws<DomainException>(() => order.Cancel(null)).Message);
        Assert.Equal(Messages.ReasonTooShort, Assert.Throws<DomainException>(() => order.Cancel(" ab ")).Message);
        order.Cancel("  customer closed down  ");

        Assert.Equal((SalesOrderStatus.Cancelled, "customer closed down", (int?)Reviewer), (order.Status, order.CancelReason, order.ApprovedBy));
    }

    [Theory]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void A_closed_or_cancelled_order_cannot_be_cancelled(SalesOrderStatus status)
    {
        var order = Draft().WithPrivate(nameof(SalesOrder.Status), status);

        Assert.Equal(Messages.SalesOrderCannotCancel(status), Assert.Throws<DomainException>(() => order.Cancel("whatever the reason")).Message);
    }

    [Fact]
    public void The_total_quantity_is_the_sum_of_the_base_quantities_that_the_quantity_rules_are_tested_against()
    {
        var order = Draft();
        order.ReplaceLines([Line(1m, 1), Line(2m, 2)]);

        Assert.Equal(10m, order.TotalQty);
    }
}
