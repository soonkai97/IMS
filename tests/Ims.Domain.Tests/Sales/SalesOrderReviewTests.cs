using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

/// <summary>
/// The corners the first sales-order tests left: totals of rounded lines, self-approval in both directions and on both tiers, a refusal that changes nothing, the limits at their exact edge, a
/// document type no prefix exists for, and the policy when nobody submitted the order. (PL3.2 and PL3.3 review.)
/// </summary>
public class SalesOrderReviewTests
{
    private const int Maker = 11;
    private const int Reviewer = 12;
    private const int Me = 42;

    private static SalesOrderLine Line(decimal qty, decimal price, int product = 1) => SalesOrderLine.Create(NewProduct(product, $"SKU-{product}"), qty, null, price, null);

    private static SalesOrder Draft(params SalesOrderLine[] lines)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Maker, Now);
        order.AssignNumber("SO-WH1-2601-0001");
        order.ReplaceLines(lines.Length == 0 ? [Line(1m, 10m)] : lines);
        return order;
    }

    private static SalesOrder Submitted()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: true, isLarge: false, Now);
        return order;
    }

    // ----- totals -----
    [Fact]
    public void The_total_is_the_sum_of_the_rounded_line_amounts_not_the_rounded_sum_of_the_exact_ones()
    {
        var order = Draft(Line(0.5m, 0.01m, 1), Line(0.5m, 0.01m, 2), Line(0.5m, 0.01m, 3)); // each line is 0.005 exactly, rounded half away from zero to 0.01

        Assert.Equal([0.01m, 0.01m, 0.01m], order.Lines.Select(l => l.SellingAmount));
        Assert.Equal(0.03m, order.TotalSellingAmount); // 3 x 0.005 = 0.015 would round to 0.02
    }

    // ----- submit -----
    [Fact]
    public void An_order_with_no_lines_cannot_be_submitted_and_stays_a_draft()
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, Maker, Now);
        order.AssignNumber("SO-WH1-2601-0002");

        var error = Assert.Throws<DomainException>(() => order.Submit(Maker, requiresApproval: false, isLarge: false, Now));

        Assert.Equal(Messages.SalesOrderNeedsLines, error.Message);
        Assert.Equal((SalesOrderStatus.Draft, (int?)null), (order.Status, order.SubmittedBy));
    }

    // ----- reject and self-approval -----
    [Fact]
    public void With_self_approval_on_the_submitter_may_reject_their_own_order_and_it_returns_to_draft_with_their_name()
    {
        var order = Submitted();

        order.Reject("changed my mind", Maker, allowSelfApproval: true, Now);

        Assert.Equal((SalesOrderStatus.Draft, "changed my mind", (int?)Maker), (order.Status, order.RejectionReason, order.RejectedBy));
        Assert.True(order.IsReturned);
    }

    [Fact]
    public void Without_self_approval_the_submitter_cannot_reject_and_nothing_changes()
    {
        var order = Submitted();

        Assert.Equal(Messages.SalesOrderSelfRejection, Assert.Throws<DomainException>(() => order.Reject("changed my mind", Maker, allowSelfApproval: false, Now)).Message);
        Assert.Equal((SalesOrderStatus.Submitted, (string?)null, (int?)null), (order.Status, order.RejectionReason, order.RejectedBy));
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Admin)]
    public void On_the_large_tier_approve_and_reject_of_ones_own_order_are_offered_only_when_self_approval_is_on_and_recall_always(string role)
    {
        var rights = SystemRoleDefaults.RightsFor(role, Me);
        var mine = new SalesOrderActionContext(SalesOrderStatus.Submitted, RequiresApproval: true, IsLarge: true, CreatedBy: Me, SubmittedBy: Me);

        var off = SalesOrderActionPolicy.AllowedActions(rights, mine, allowSelfApproval: false);
        var on = SalesOrderActionPolicy.AllowedActions(rights, mine, allowSelfApproval: true);

        Assert.DoesNotContain(DocumentActions.Approve, off);
        Assert.DoesNotContain(DocumentActions.Reject, off);
        Assert.Contains(DocumentActions.Approve, on);
        Assert.Contains(DocumentActions.Reject, on);
        Assert.Contains(DocumentActions.Recall, off);
        Assert.Contains(DocumentActions.Recall, on);
    }

    // ----- a refusal changes nothing -----
    [Fact]
    public void Rejecting_with_a_blank_reason_is_refused_with_the_reason_required_sentence_first_and_changes_nothing()
    {
        var order = Submitted();

        foreach (var blank in new string?[] { null, "", "   " })
            Assert.Equal(Messages.RejectReasonRequired, Assert.Throws<DomainException>(() => order.Reject(blank!, Reviewer, allowSelfApproval: false, Now)).Message);

        Assert.Equal((SalesOrderStatus.Submitted, (string?)null, (int?)null, true), (order.Status, order.RejectionReason, order.RejectedBy, order.RequiresApproval));
    }

    [Fact]
    public void Rejecting_with_a_reason_that_is_too_short_names_the_minimum_and_changes_nothing()
    {
        var order = Submitted();

        Assert.Equal(Messages.ReasonTooShort, Assert.Throws<DomainException>(() => order.Reject("no", Reviewer, allowSelfApproval: false, Now)).Message);

        Assert.Equal((SalesOrderStatus.Submitted, (string?)null), (order.Status, order.RejectionReason));
    }

    [Fact]
    public void Cancelling_an_approved_order_without_a_proper_reason_is_refused_and_changes_nothing()
    {
        var order = Draft();
        order.Submit(Maker, requiresApproval: false, isLarge: false, Now);

        Assert.Throws<DomainException>(() => order.Cancel(null));
        Assert.Throws<DomainException>(() => order.Cancel("  no "));

        Assert.Equal((SalesOrderStatus.Approved, (string?)null), (order.Status, order.CancelReason));
    }

    [Theory]
    [InlineData(SalesOrderStatus.Closed)]
    [InlineData(SalesOrderStatus.Cancelled)]
    public void Cancelling_a_closed_or_cancelled_order_is_refused_and_changes_nothing(SalesOrderStatus status)
    {
        var order = Draft().WithPrivate(nameof(SalesOrder.Status), status);

        Assert.Equal(Messages.SalesOrderCannotCancel(status), Assert.Throws<DomainException>(() => order.Cancel("no longer needed")).Message);

        Assert.Equal((status, (string?)null), (order.Status, order.CancelReason));
    }

    // ----- limits at their exact edge -----
    [Fact]
    public void A_quantity_a_price_and_an_amount_exactly_at_their_limits_are_accepted()
    {
        Assert.Equal(Limits.MaxUnitCost, Line(1m, Limits.MaxUnitCost).SellingPrice);
        Assert.Equal(Limits.MaxQuantity * 0.01m, Line(Limits.MaxQuantity, 0.01m).SellingAmount);
        Assert.Equal(Limits.MaxBaseQuantity, Line(Limits.MaxBaseQuantity, 0m).BaseQty);
    }

    [Fact]
    public void A_quantity_that_is_less_than_the_smallest_step_of_the_base_unit_once_converted_is_refused_not_stored_as_zero()
    {
        var product = NewProduct(cartonFactor: 0.1m); // one carton is a tenth of a piece

        Assert.Equal(Messages.QtyTooSmall, Assert.Throws<DomainException>(() => SalesOrderLine.Create(product, 0.0001m, CartonId, 1m, null)).Message); // 0.00001 pieces
        Assert.Equal(0.0001m, SalesOrderLine.Create(product, 0.001m, CartonId, 1m, null).BaseQty);                                                       // exactly the smallest step is fine
    }

    [Fact]
    public void One_step_past_the_limits_is_refused()
    {
        Assert.Throws<DomainException>(() => Line(1m, Limits.MaxUnitCost + 0.01m));
        Assert.Throws<DomainException>(() => Line(Limits.MaxBaseQuantity + 1m, 0m));
    }

    [Fact]
    public void The_document_limit_of_lines_is_accepted_exactly()
    {
        var order = Draft();

        order.ReplaceLines(Enumerable.Range(1, Limits.MaxDocumentLines).Select(i => Line(1m, 1m, i)));

        Assert.Equal(Limits.MaxDocumentLines, order.Lines.Count);
    }

    // ----- numbering -----
    [Fact]
    public void A_document_type_with_no_prefix_is_an_error_not_a_silent_adjustment_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentNumber.PrefixFor((DocumentType)999));
    }

    // ----- the policy when nobody submitted it -----
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Supervisor)]
    [InlineData(Roles.Clerk)]
    public void An_order_that_names_no_submitter_can_be_neither_taken_back_nor_cancelled_by_a_manager_as_its_submitter(string role)
    {
        var rights = SystemRoleDefaults.RightsFor(role, Me);
        var unsigned = new SalesOrderActionContext(SalesOrderStatus.Submitted, RequiresApproval: true, IsLarge: false, CreatedBy: Me, SubmittedBy: null);

        Assert.False(SalesOrderActionPolicy.MayRecall(rights, unsigned));
        Assert.False(SalesOrderActionPolicy.MayCancel(rights, unsigned));
    }
}

/// <summary>PL3.7 review: the base quantity is rounded like the column that stores it, and "fully delivered" is decided on the entered quantity in the order line's unit.</summary>
public class SalesOrderRoundingTests
{
    private static Product Carton(decimal factor, int id = 1) => NewProduct(id, $"SKU-{id}", cartonFactor: factor);

    [Fact]
    public void A_base_quantity_is_rounded_to_four_decimals_half_away_from_zero_so_what_is_in_memory_is_what_is_stored()
    {
        var product = Carton(0.666667m);

        Assert.Equal(0.6667m, product.ToBaseQuantity(1m, CartonId));   // 0.666667
        Assert.Equal(2.0000m, product.ToBaseQuantity(3m, CartonId));   // 2.000001
        Assert.Equal(0.3333m, product.ToBaseQuantity(0.5m, CartonId)); // 0.3333335, rounded at the fourth decimal
        Assert.Equal(0.0001m, Carton(0.5m).ToBaseQuantity(0.0002m, CartonId)); // exactly the smallest step
    }

    [Fact]
    public void A_purchase_order_line_gets_the_same_rounded_base_quantity_the_column_would_store()
    {
        var line = PurchaseOrderLine.Create(Carton(0.666667m), 1m, CartonId, 2m, null);

        Assert.Equal(0.6667m, line.BaseQty);
    }

    [Fact]
    public void A_stock_document_line_whose_quantity_rounds_to_nothing_is_refused()
    {
        var product = Carton(0.1m);

        Assert.Equal(Messages.QtyTooSmall, Assert.Throws<DomainException>(() => StockDocumentLine.Create(product, DocumentType.Issue, 0.0001m, CartonId, null, null, costFromAverage: true)).Message);
    }

    [Fact]
    public void Three_deliveries_of_one_against_an_order_of_three_cartons_are_never_over_and_leave_nothing_whatever_the_factor_does_to_the_base_quantity()
    {
        var product = Carton(0.666667m);
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, 11, Now);
        order.AssignNumber("SO-WH1-2601-0009");
        order.ReplaceLines([SalesOrderLine.Create(product, 3m, CartonId, 2m, null)]);
        var line = order.Lines[0];
        var delivered = new Dictionary<int, DeliveredQuantity>();

        for (var i = 1; i <= 3; i++)
        {
            Assert.Empty(SalesOrderQuantities.OverDeliveries(order.Lines, delivered, [new DeliveryQuantity(1, 1m, product.ToBaseQuantity(1m, CartonId))]));
            var before = delivered.GetValueOrDefault(1);
            delivered[1] = new DeliveredQuantity(before.Qty + 1m, before.BaseQty + product.ToBaseQuantity(1m, CartonId));
        }

        Assert.Equal(0m, SalesOrderQuantities.RemainingQty(line, delivered[1]));                 // the measure of every decision
        Assert.Equal(2.0001m, delivered[1].BaseQty);                                              // the booked base quantity is 0.0001 more than the order's 2.0000: it is shown, never compared
        Assert.Single(SalesOrderQuantities.OverDeliveries(order.Lines, delivered, [new DeliveryQuantity(1, 0.0001m, 0.0001m)]));
    }

    [Fact]
    public void Cancel_and_close_are_not_listed_while_a_delivery_stands_in_their_way()
    {
        var actor = SystemRoleDefaults.RightsFor(Roles.Admin, 1);
        var open = new SalesOrderActionContext(SalesOrderStatus.Approved, false, false, 2, 2, AnythingRemaining: true, AnythingDelivered: true, DeliveriesBlockCancel: true, DeliveryOpen: true);
        var posted = open with { DeliveryOpen = false };
        var none = open with { DeliveriesBlockCancel = false, DeliveryOpen = false };

        string[] Listed(SalesOrderActionContext c) => [.. SalesOrderActionPolicy.AllowedActions(actor, c, allowSelfApproval: false)];

        Assert.DoesNotContain(DocumentActions.Cancel, Listed(open));
        Assert.DoesNotContain(DocumentActions.Close, Listed(open));
        Assert.DoesNotContain(DocumentActions.Cancel, Listed(posted)); // a posted delivery blocks cancel (reverse it first) ...
        Assert.Contains(DocumentActions.Close, Listed(posted));        // ... but not a close short
        Assert.Contains(DocumentActions.Cancel, Listed(none));
        Assert.Contains(DocumentActions.Close, Listed(none));
    }
}
