using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Common;

/// <summary>
/// DDD-11a: every event is stamped with a number from one counter for the whole process when it is raised, so the dispatcher can put the events of two aggregates in the order the domain
/// raised them. The bare <see cref="IAggregateRoot.DomainEvents"/> list stays as it was.
/// </summary>
public class RaisedEventSequenceTests
{
    private static SalesOrder NewOrder(string no)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, 11, Now);
        order.AssignNumber(no);
        return order;
    }

    private static SalesOrderLine Line() => SalesOrderLine.Create(NewProduct(), 5m, null, 10m, null);

    [Fact]
    public void Events_of_two_aggregates_sort_by_sequence_into_the_order_they_were_raised()
    {
        var first = NewOrder("SO-A");
        var second = NewOrder("SO-B");

        first.ReplaceLines([Line()]);                    // 1: A created
        second.ReplaceLines([Line()]);                   // 2: B created
        first.Submit(11, requiresApproval: false, isLarge: false, Now); // 3: A submitted

        var sorted = first.RaisedEvents.Concat(second.RaisedEvents).OrderBy(r => r.Sequence).Select(r => r.Event.GetType()).ToList();

        Assert.Equal([typeof(SalesOrderCreated), typeof(SalesOrderCreated), typeof(SalesOrderSubmitted)], sorted);
        Assert.Equal(typeof(SalesOrderSubmitted), first.RaisedEvents[1].Event.GetType());
        Assert.True(second.RaisedEvents[0].Sequence > first.RaisedEvents[0].Sequence);
        Assert.True(second.RaisedEvents[0].Sequence < first.RaisedEvents[1].Sequence);
    }

    [Fact]
    public void Sequences_are_positive_and_never_repeat()
    {
        var order = NewOrder("SO-C");
        order.ReplaceLines([Line()]);
        order.ReplaceLines([Line()]);
        order.ReplaceLines([Line()]);

        var sequences = order.RaisedEvents.Select(r => r.Sequence).ToList();

        Assert.Equal(3, sequences.Count);
        Assert.All(sequences, s => Assert.True(s > 0));
        Assert.Equal(sequences.Order().ToList(), sequences);
        Assert.Equal(3, sequences.Distinct().Count());
    }

    [Fact]
    public void DomainEvents_lists_the_same_events_in_the_same_order_as_RaisedEvents()
    {
        var order = NewOrder("SO-D");
        order.ReplaceLines([Line()]);
        order.Submit(11, requiresApproval: false, isLarge: false, Now);

        Assert.Equal(order.RaisedEvents.Select(r => r.Event), order.DomainEvents);
        Assert.Equal(2, order.DomainEvents.Count);
    }

    [Fact]
    public void Clearing_forgets_both_lists()
    {
        var order = NewOrder("SO-E");
        order.ReplaceLines([Line()]);

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
        Assert.Empty(order.RaisedEvents);
    }
}
