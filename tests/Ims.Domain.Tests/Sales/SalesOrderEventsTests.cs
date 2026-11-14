using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

/// <summary>
/// DDD-06: the sales order is the first aggregate root. It raises one domain event for each change the audit trail records today and carries exactly what the audit payload prints
/// (docs/architecture/ddd-roadmap.md 2.4); a refused change raises nothing. Nobody listens yet: the dispatcher and the audit subscriber come with DDD-11.
/// </summary>
public class SalesOrderEventsTests
{
    private const int Maker = 11;
    private const int Reviewer = 12;
    private const string Number = "SO-WH1-2601-0001";

    private static SalesOrderLine Line(decimal price, decimal qty = 5m, int product = 1) => SalesOrderLine.Create(NewProduct(product, $"SKU-{product}"), qty, null, price, null);

    /// <summary>An order as the create handler builds it, with the events of its creation cleared so that a test sees only what it does next.</summary>
    private static SalesOrder Draft(decimal price = 10m)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Maker, Now);
        order.AssignNumber(Number);
        order.ReplaceLines([Line(price)]);
        order.ClearDomainEvents();
        return order;
    }

    private static SalesOrder Submitted(bool large = false)
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, large, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static SalesOrder Approved()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: false, isLarge: false, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static SalesOrder Closed()
    {
        var order = Approved();
        order.CloseShort("customer took less", Reviewer, Now);
        order.ClearDomainEvents();
        return order;
    }

    private static TEvent Only<TEvent>(SalesOrder order) where TEvent : IDomainEvent => Assert.IsType<TEvent>(Assert.Single(order.DomainEvents));

    // ----- create and update -----
    [Fact]
    public void A_new_order_raises_Created_once_its_lines_are_in_and_carries_the_summary_the_audit_prints()
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), "urgent", Maker, Now);
        order.AssignNumber(Number);
        Assert.Empty(order.DomainEvents); // the audit row of a create names the number and counts the lines: both are known only now

        order.ReplaceLines([Line(price: 10m, qty: 5m, product: 1), Line(price: 2.5m, qty: 4m, product: 2)]);

        var created = Only<SalesOrderCreated>(order);
        Assert.Equal(Number, created.No);
        Assert.Equal(new SalesOrderSummary(SalesOrderStatus.Draft, WarehouseId: 1, CustomerId: 200, LineCount: 2, SellingAmount: Money.Rounded(60m)), created.Summary);
    }

    [Fact]
    public void Replacing_the_lines_of_a_draft_raises_Updated_with_the_summary_before_and_after()
    {
        var order = Draft(price: 10m); // one line, 5 x 10.00 = 50.00

        order.ReplaceLines([Line(price: 1m, qty: 3m, product: 1), Line(price: 1m, qty: 4m, product: 2), Line(price: 1m, qty: 5m, product: 3)]);

        var updated = Only<SalesOrderUpdated>(order);
        Assert.Equal(Number, updated.No);
        Assert.Equal(new SalesOrderSummary(SalesOrderStatus.Draft, 1, 200, LineCount: 1, SellingAmount: Money.Rounded(50m)), updated.Before);
        Assert.Equal(new SalesOrderSummary(SalesOrderStatus.Draft, 1, 200, LineCount: 3, SellingAmount: Money.Rounded(12m)), updated.After);
    }

    [Fact]
    public void A_header_change_followed_by_the_lines_is_one_Updated_whose_before_still_names_the_old_customer()
    {
        var order = Draft();

        order.UpdateHeader(NewCustomer(id: 201), "new remark");
        Assert.Empty(order.DomainEvents); // the edit is complete (and audited as one row) only when the lines are replaced too
        order.ReplaceLines([Line(price: 10m)]);

        var updated = Only<SalesOrderUpdated>(order);
        Assert.Equal((200, 201), (updated.Before.CustomerId, updated.After.CustomerId));
    }

    [Fact]
    public void Two_edits_in_a_row_each_raise_their_own_Updated_and_the_second_starts_from_the_first()
    {
        var order = Draft();

        order.ReplaceLines([Line(price: 20m)]);
        order.ReplaceLines([Line(price: 30m)]);

        var events = order.DomainEvents.Cast<SalesOrderUpdated>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal((Money.Rounded(50m), Money.Rounded(100m)), (events[0].Before.SellingAmount, events[0].After.SellingAmount));
        Assert.Equal(events[0].After, events[1].Before);
        Assert.Equal(Money.Rounded(150m), events[1].After.SellingAmount);
    }

    // ----- the workflow -----
    [Fact]
    public void Submitting_with_a_rule_raises_one_Submitted_from_Draft_to_Submitted_that_requires_approval_and_is_large()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: true, isLarge: true, Now);

        var submitted = Only<SalesOrderSubmitted>(order);
        Assert.Equal(Number, submitted.No);
        Assert.Equal((SalesOrderStatus.Draft, SalesOrderStatus.Submitted), (submitted.StatusBefore, submitted.StatusAfter));
        Assert.True(submitted.RequiresApproval);
        Assert.True(submitted.IsLarge);
        Assert.Equal(Money.Rounded(50m), submitted.SellingAmount);
    }

    [Fact]
    public void Submitting_when_no_rule_fired_raises_Submitted_that_ends_in_Approved_and_is_not_large_whatever_was_asked()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: false, isLarge: true, Now);

        var submitted = Only<SalesOrderSubmitted>(order);
        Assert.Equal((SalesOrderStatus.Draft, SalesOrderStatus.Approved, false, false), (submitted.StatusBefore, submitted.StatusAfter, submitted.RequiresApproval, submitted.IsLarge));
    }

    [Fact]
    public void Approving_raises_Approved_from_Submitted_with_the_tier_the_order_was_stored_with()
    {
        var order = Submitted(large: true);

        order.Approve(Reviewer, allowSelfApproval: false, Now);

        var approved = Only<SalesOrderApproved>(order);
        Assert.Equal(Number, approved.No);
        Assert.Equal((SalesOrderStatus.Submitted, SalesOrderStatus.Approved, true), (approved.StatusBefore, approved.StatusAfter, approved.IsLarge));
    }

    [Fact]
    public void Rejecting_raises_Rejected_back_to_Draft_with_the_trimmed_reason()
    {
        var order = Submitted();

        order.Reject("  price too low  ", Reviewer, allowSelfApproval: false, Now);

        var rejected = Only<SalesOrderRejected>(order);
        Assert.Equal(Number, rejected.No);
        Assert.Equal((SalesOrderStatus.Submitted, SalesOrderStatus.Draft, "price too low"), (rejected.StatusBefore, rejected.StatusAfter, rejected.Reason));
    }

    [Fact]
    public void Recalling_raises_Recalled_and_remembers_that_the_order_had_waited_for_approval()
    {
        var order = Submitted();

        order.Recall();

        var recalled = Only<SalesOrderRecalled>(order);
        Assert.Equal(Number, recalled.No);
        Assert.Equal((SalesOrderStatus.Submitted, true, SalesOrderStatus.Draft), (recalled.StatusBefore, recalled.RequiresApprovalBefore, recalled.StatusAfter));
    }

    [Fact]
    public void Cancelling_an_approved_order_raises_Cancelled_with_the_trimmed_reason()
    {
        var order = Approved();

        order.Cancel("  customer withdrew  ");

        var cancelled = Only<SalesOrderCancelled>(order);
        Assert.Equal(Number, cancelled.No);
        Assert.Equal((SalesOrderStatus.Approved, SalesOrderStatus.Cancelled, "customer withdrew"), (cancelled.StatusBefore, cancelled.StatusAfter, cancelled.Reason));
    }

    [Fact]
    public void Cancelling_a_draft_without_a_reason_raises_Cancelled_with_no_reason()
    {
        var order = Draft();

        order.Cancel(null);

        var cancelled = Only<SalesOrderCancelled>(order);
        Assert.Equal((SalesOrderStatus.Draft, SalesOrderStatus.Cancelled, (string?)null), (cancelled.StatusBefore, cancelled.StatusAfter, cancelled.Reason));
    }

    [Fact]
    public void Closing_short_raises_Closed_of_kind_Short_with_the_trimmed_reason_and_no_delivery()
    {
        var order = Approved();

        order.CloseShort("  customer took less  ", Reviewer, Now);

        var closed = Only<SalesOrderClosed>(order);
        Assert.Equal(Number, closed.No);
        Assert.Equal((SalesOrderStatus.Approved, SalesOrderStatus.Closed, SalesOrderCloseKind.Short), (closed.StatusBefore, closed.StatusAfter, closed.Kind));
        Assert.Equal(("customer took less", (string?)null), (closed.Reason, closed.Delivery));
    }

    [Fact]
    public void Closing_as_delivered_raises_Closed_of_kind_Delivered_that_names_the_delivery_and_has_no_reason()
    {
        var order = Approved();

        order.CloseDelivered(Reviewer, "DLV-WH1-2601-0007", Now);

        var closed = Only<SalesOrderClosed>(order);
        Assert.Equal((SalesOrderStatus.Approved, SalesOrderStatus.Closed, SalesOrderCloseKind.Delivered), (closed.StatusBefore, closed.StatusAfter, closed.Kind));
        Assert.Equal(((string?)null, (string?)"DLV-WH1-2601-0007"), (closed.Reason, closed.Delivery));
    }

    [Fact]
    public void The_events_of_several_changes_stay_in_the_order_they_were_raised()
    {
        var order = Draft();

        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        order.Approve(Reviewer, allowSelfApproval: false, Now);
        order.CloseShort("customer took less", Reviewer, Now);

        Assert.Equal([typeof(SalesOrderSubmitted), typeof(SalesOrderApproved), typeof(SalesOrderClosed)], order.DomainEvents.Select(e => e.GetType()));
    }

    // ----- a refused change raises nothing -----
    [Fact]
    public void A_submit_that_is_refused_raises_nothing_and_leaves_the_draft_as_it_was()
    {
        var order = Draft(price: 0m); // a 0.00 order may not be submitted

        Assert.Throws<DomainException>(() => order.Submit(Maker, requiresApproval: true, isLarge: false, Now));

        Assert.Empty(order.DomainEvents);
        Assert.Equal(SalesOrderStatus.Draft, order.Status);
    }

    public static TheoryData<string> Refusals =>
    [
        "submit a submitted order", "approve a draft", "approve own submission", "reject without a reason", "reject a draft", "recall a draft",
        "cancel a closed order", "cancel an approved order without a reason", "close short without a reason", "close a draft short", "close a draft as delivered",
        "update the header of a submitted order", "replace the lines of a submitted order", "replace the lines with none",
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_refused_transition_raises_nothing(string refusal)
    {
        var (order, act) = refusal switch
        {
            "submit a submitted order" => (Submitted(), (Action<SalesOrder>)(o => o.Submit(Maker, true, false, Now))),
            "approve a draft" => (Draft(), o => o.Approve(Reviewer, false, Now)),
            "approve own submission" => (Submitted(), o => o.Approve(Maker, allowSelfApproval: false, Now)),
            "reject without a reason" => (Submitted(), o => o.Reject("  ", Reviewer, false, Now)),
            "reject a draft" => (Draft(), o => o.Reject("price too low", Reviewer, false, Now)),
            "recall a draft" => (Draft(), o => o.Recall()),
            "cancel a closed order" => (Closed(), o => o.Cancel("too late")),
            "cancel an approved order without a reason" => (Approved(), o => o.Cancel(null)),
            "close short without a reason" => (Approved(), o => o.CloseShort(null, Reviewer, Now)),
            "close a draft short" => (Draft(), o => o.CloseShort("customer took less", Reviewer, Now)),
            "close a draft as delivered" => (Draft(), o => o.CloseDelivered(Reviewer, "DLV-1", Now)),
            "update the header of a submitted order" => (Submitted(), o => o.UpdateHeader(NewCustomer(), null)),
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
        Only<SalesOrderRecalled>(order);
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
    public void A_sales_order_is_an_aggregate_root()
    {
        Assert.IsAssignableFrom<IAggregateRoot>(Draft());
        Assert.IsAssignableFrom<AggregateRoot>(Draft());
    }
}
