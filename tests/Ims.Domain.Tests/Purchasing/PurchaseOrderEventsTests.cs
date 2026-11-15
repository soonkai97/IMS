using System.Globalization;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Purchasing;

/// <summary>
/// DDD-12: the purchase order is an aggregate root. It raises one domain event for each change the audit trail records today and carries exactly what the audit payload prints
/// (docs/architecture/ddd-roadmap.md 2.4, the twin of <c>SalesOrderEventsTests</c>); a refused change raises nothing. <c>PurchaseOrderAuditSubscriberTests</c> turns the events into the rows.
/// </summary>
public class PurchaseOrderEventsTests
{
    private const int Maker = 11;
    private const int Reviewer = 12;
    private const string Number = "PO-WH1-2601-0001";

    private static PurchaseOrderLine Line(decimal price, decimal qty = 5m, int product = 1) => PurchaseOrderLine.Create(NewProduct(product, $"SKU-{product}"), qty, null, price, null);

    /// <summary>An order as the create handler builds it, with the events of its creation cleared so that a test sees only what it does next.</summary>
    private static PurchaseOrder Draft(decimal price = 10m)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Maker, Now);
        order.AssignNumber(Number);
        order.ReplaceLines([Line(price)]);
        order.ClearDomainEvents();
        return order;
    }

    private static PurchaseOrder Submitted(bool large = false)
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, large, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static PurchaseOrder Approved()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: false, isLarge: false, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static PurchaseOrder Closed()
    {
        var order = Approved();
        order.CloseShort("supplier ships no more", Reviewer, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static TEvent Only<TEvent>(PurchaseOrder order) where TEvent : IDomainEvent => Assert.IsType<TEvent>(Assert.Single(order.DomainEvents));

    // ----- create and update -----
    [Fact]
    public void A_new_order_raises_Created_once_its_lines_are_in_and_carries_the_summary_the_audit_prints()
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), "urgent", Maker, Now);
        order.AssignNumber(Number);
        Assert.Empty(order.DomainEvents); // the audit row of a create names the number and counts the lines: both are known only now

        order.ReplaceLines([Line(price: 10m, qty: 5m, product: 1), Line(price: 2.5m, qty: 4m, product: 2)]);

        var created = Only<PurchaseOrderCreated>(order);
        Assert.Equal(Number, created.No);
        Assert.Equal(new PurchaseOrderSummary(PurchaseOrderStatus.Draft, WarehouseId: 1, SupplierId: 100, LineCount: 2, Amount: 60m), created.Summary);
    }

    [Theory]
    [InlineData("12", "12")]
    [InlineData("12.5", "12.5")]
    [InlineData("12.50", "12.50")]
    public void The_summary_keeps_the_scale_of_the_amount_as_computed_because_the_audit_row_prints_a_decimal_as_it_is(string price, string printed)
    {
        var order = PurchaseOrder.Create(NewWarehouse(), NewSupplier(), null, Maker, Now);
        order.AssignNumber(Number);

        order.ReplaceLines([Line(price: decimal.Parse(price, CultureInfo.InvariantCulture), qty: 1m)]);

        Assert.Equal(printed, Convert.ToString(Only<PurchaseOrderCreated>(order).Summary.Amount, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Replacing_the_lines_of_a_draft_raises_Updated_with_the_summary_before_and_after()
    {
        var order = Draft(price: 10m); // one line, 5 x 10.00 = 50.00

        order.ReplaceLines([Line(price: 1m, qty: 3m, product: 1), Line(price: 1m, qty: 4m, product: 2), Line(price: 1m, qty: 5m, product: 3)]);

        var updated = Only<PurchaseOrderUpdated>(order);
        Assert.Equal(Number, updated.No);
        Assert.Equal(new PurchaseOrderSummary(PurchaseOrderStatus.Draft, 1, 100, LineCount: 1, Amount: 50m), updated.Before);
        Assert.Equal(new PurchaseOrderSummary(PurchaseOrderStatus.Draft, 1, 100, LineCount: 3, Amount: 12m), updated.After);
    }

    [Fact]
    public void A_header_change_followed_by_the_lines_is_one_Updated_whose_before_still_names_the_old_supplier()
    {
        var order = Draft();

        order.UpdateHeader(NewSupplier(id: 101), "new remark");
        Assert.Empty(order.DomainEvents); // the edit is complete (and audited as one row) only when the lines are replaced too
        order.ReplaceLines([Line(price: 10m)]);

        var updated = Only<PurchaseOrderUpdated>(order);
        Assert.Equal((100, 101), (updated.Before.SupplierId, updated.After.SupplierId));
    }

    [Fact]
    public void Two_edits_in_a_row_each_raise_their_own_Updated_and_the_second_starts_from_the_first()
    {
        var order = Draft();

        order.ReplaceLines([Line(price: 20m)]);
        order.ReplaceLines([Line(price: 30m)]);

        var events = order.DomainEvents.Cast<PurchaseOrderUpdated>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal((50m, 100m), (events[0].Before.Amount, events[0].After.Amount));
        Assert.Equal(events[0].After, events[1].Before);
        Assert.Equal(150m, events[1].After.Amount);
    }

    // ----- the workflow -----
    [Fact]
    public void Submitting_with_a_rule_raises_one_Submitted_from_Draft_to_Submitted_that_requires_approval_and_is_large()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: true, isLarge: true, Now);

        var submitted = Only<PurchaseOrderSubmitted>(order);
        Assert.Equal(Number, submitted.No);
        Assert.Equal((PurchaseOrderStatus.Draft, PurchaseOrderStatus.Submitted), (submitted.StatusBefore, submitted.StatusAfter));
        Assert.True(submitted.RequiresApproval);
        Assert.True(submitted.IsLarge);
        Assert.Equal(Money.Rounded(50m), submitted.Amount);
    }

    [Fact]
    public void Submitting_when_no_rule_fired_raises_Submitted_that_ends_in_Approved_and_is_not_large_whatever_was_asked()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: false, isLarge: true, Now);

        var submitted = Only<PurchaseOrderSubmitted>(order);
        Assert.Equal((PurchaseOrderStatus.Draft, PurchaseOrderStatus.Approved, false, false), (submitted.StatusBefore, submitted.StatusAfter, submitted.RequiresApproval, submitted.IsLarge));
    }

    [Fact]
    public void Approving_raises_Approved_from_Submitted_with_the_tier_the_order_was_stored_with()
    {
        var order = Submitted(large: true);

        order.Approve(Reviewer, allowSelfApproval: false, Now);

        var approved = Only<PurchaseOrderApproved>(order);
        Assert.Equal(Number, approved.No);
        Assert.Equal((PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Approved, true), (approved.StatusBefore, approved.StatusAfter, approved.IsLarge));
    }

    [Fact]
    public void Rejecting_raises_Rejected_back_to_Draft_with_the_trimmed_reason()
    {
        var order = Submitted();

        order.Reject("  price too high  ", Reviewer, allowSelfApproval: false, Now);

        var rejected = Only<PurchaseOrderRejected>(order);
        Assert.Equal(Number, rejected.No);
        Assert.Equal((PurchaseOrderStatus.Submitted, PurchaseOrderStatus.Draft, "price too high"), (rejected.StatusBefore, rejected.StatusAfter, rejected.Reason));
    }

    [Fact]
    public void Recalling_raises_Recalled_and_remembers_that_the_order_had_waited_for_approval()
    {
        var order = Submitted();

        order.Recall();

        var recalled = Only<PurchaseOrderRecalled>(order);
        Assert.Equal(Number, recalled.No);
        Assert.Equal((PurchaseOrderStatus.Submitted, true, PurchaseOrderStatus.Draft), (recalled.StatusBefore, recalled.RequiresApprovalBefore, recalled.StatusAfter));
    }

    [Fact]
    public void Cancelling_an_approved_order_raises_Cancelled_with_the_trimmed_reason()
    {
        var order = Approved();

        order.Cancel("  supplier withdrew  ");

        var cancelled = Only<PurchaseOrderCancelled>(order);
        Assert.Equal(Number, cancelled.No);
        Assert.Equal((PurchaseOrderStatus.Approved, PurchaseOrderStatus.Cancelled, "supplier withdrew"), (cancelled.StatusBefore, cancelled.StatusAfter, cancelled.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Cancelling_a_draft_without_a_reason_raises_Cancelled_with_no_reason(string? reason)
    {
        var order = Draft();

        order.Cancel(reason);

        var cancelled = Only<PurchaseOrderCancelled>(order);
        Assert.Equal((PurchaseOrderStatus.Draft, PurchaseOrderStatus.Cancelled, (string?)null), (cancelled.StatusBefore, cancelled.StatusAfter, cancelled.Reason));
    }

    [Fact]
    public void Closing_short_raises_Closed_of_kind_Short_with_the_trimmed_reason_and_no_receipt()
    {
        var order = Approved();

        order.CloseShort("  supplier ships no more  ", Reviewer, Now);

        var closed = Only<PurchaseOrderClosed>(order);
        Assert.Equal(Number, closed.No);
        Assert.Equal((PurchaseOrderStatus.Approved, PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Short), (closed.StatusBefore, closed.StatusAfter, closed.Kind));
        Assert.Equal(("supplier ships no more", (string?)null), (closed.Reason, closed.Receipt));
    }

    [Fact]
    public void Closing_as_received_raises_Closed_of_kind_Received_that_names_the_receipt_and_has_no_reason()
    {
        var order = Approved();

        order.CloseReceived(Reviewer, "RCV-WH1-2601-0007", Now);

        var closed = Only<PurchaseOrderClosed>(order);
        Assert.Equal((PurchaseOrderStatus.Approved, PurchaseOrderStatus.Closed, PurchaseOrderCloseKind.Received), (closed.StatusBefore, closed.StatusAfter, closed.Kind));
        Assert.Equal(((string?)null, (string?)"RCV-WH1-2601-0007"), (closed.Reason, closed.Receipt));
    }

    [Fact]
    public void The_events_of_several_changes_stay_in_the_order_they_were_raised()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        order.Approve(Reviewer, allowSelfApproval: false, Now);
        order.CloseShort("supplier ships no more", Reviewer, Now);

        Assert.Equal([typeof(PurchaseOrderSubmitted), typeof(PurchaseOrderApproved), typeof(PurchaseOrderClosed)], order.DomainEvents.Select(e => e.GetType()));
    }

    // ----- a refused change raises nothing -----
    [Fact]
    public void A_submit_that_is_refused_raises_nothing_and_leaves_the_draft_as_it_was()
    {
        var order = Draft(price: 0m); // a 0.00 order may not be submitted

        Assert.Throws<DomainException>(() => order.Submit(Maker, requiresApproval: true, isLarge: false, Now));

        Assert.Empty(order.DomainEvents);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    public static TheoryData<string> Refusals =>
    [
        "submit a submitted order", "approve a draft", "approve own submission", "reject without a reason", "reject a draft", "recall a draft",
        "cancel a closed order", "cancel an approved order without a reason", "cancel another user's draft without a reason", "close short without a reason", "close a draft short",
        "close a draft as received", "update the header of a submitted order", "replace the lines of a submitted order", "replace the lines with none",
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_refused_transition_raises_nothing(string refusal)
    {
        var (order, act) = refusal switch
        {
            "submit a submitted order" => (Submitted(), (Action<PurchaseOrder>)(o => o.Submit(Maker, true, false, Now))),
            "approve a draft" => (Draft(), o => o.Approve(Reviewer, false, Now)),
            "approve own submission" => (Submitted(), o => o.Approve(Maker, allowSelfApproval: false, Now)),
            "reject without a reason" => (Submitted(), o => o.Reject("  ", Reviewer, false, Now)),
            "reject a draft" => (Draft(), o => o.Reject("price too high", Reviewer, false, Now)),
            "recall a draft" => (Draft(), o => o.Recall()),
            "cancel a closed order" => (Closed(), o => o.Cancel("too late")),
            "cancel an approved order without a reason" => (Approved(), o => o.Cancel(null)),
            "cancel another user's draft without a reason" => (Draft(), o => o.Cancel(null, byAnotherUser: true)),
            "close short without a reason" => (Approved(), o => o.CloseShort(null, Reviewer, Now)),
            "close a draft short" => (Draft(), o => o.CloseShort("supplier ships no more", Reviewer, Now)),
            "close a draft as received" => (Draft(), o => o.CloseReceived(Reviewer, "RCV-1", Now)),
            "update the header of a submitted order" => (Submitted(), o => o.UpdateHeader(NewSupplier(), null)),
            "replace the lines of a submitted order" => (Submitted(), o => o.ReplaceLines([Line(1m)])),
            "replace the lines with none" => (Draft(), o => o.ReplaceLines([])),
            _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
        };

        Assert.Throws<DomainException>(() => act(order));

        Assert.Empty(order.DomainEvents);
    }

    // ----- clearing -----
    [Fact]
    public void ClearDomainEvents_empties_the_list_and_the_next_change_raises_again()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        Assert.NotEmpty(order.DomainEvents);

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
        order.Recall();
        Only<PurchaseOrderRecalled>(order);
    }

    [Fact]
    public void A_copy_taken_before_clearing_keeps_its_events()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        var taken = order.DomainEvents.ToList();

        order.ClearDomainEvents();

        Assert.Single(taken);
    }

    [Fact]
    public void A_purchase_order_is_an_aggregate_root()
    {
        Assert.IsAssignableFrom<IAggregateRoot>(Draft());
        Assert.IsAssignableFrom<AggregateRoot>(Draft());
    }
}
