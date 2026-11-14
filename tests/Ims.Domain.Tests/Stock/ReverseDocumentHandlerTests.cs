using Ims.Application;
using Ims.Application.Features.Documents;

using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09, characterization: <c>ReverseDocumentHandler</c> as it is on <c>main</c> before "can it be returned" moves into <c>StockDocument.CanBeReturned</c>. Green before the change and green after it:
/// for every kind of document the locks the handler takes and the query it makes (the steps, in order) and the answer when a Posted return exists. The condition decides whether a lock and a query
/// happen, so the steps are the point: they must not move (<c>ITransactionLocker</c>: the sales order, then the original, then nothing else before the reversal reads settings).
/// The statement counts in <c>PostingStatementCountTests</c> and <c>ReturnStatementCountTests</c> prove the same on the wire.
/// </summary>
public class ReverseDocumentHandlerTests
{
    public static TheoryData<string> Names() => new(ReturnableTable.All().Select(r => r.Name));

    [Theory]
    [MemberData(nameof(Names))]
    public async Task ForEveryKindOfDocument_TheLocksAndTheQueryAreTheOnesTheTableWrites(string name)
    {
        var row = ReturnableTable.All().Single(r => r.Name == name);
        var doc = row.Build();
        var posted = doc.Status == DocumentStatus.Posted;                                   // read before the handler moves it

        var (journal, thrown) = await RunAsync(doc);

        Assert.Equal(row.Steps, journal.Where(s => s != "settings"));                      // the steps of the decision, in order
        Assert.IsType(posted ? typeof(StopHere) : typeof(DomainException), thrown);        // a Posted document goes on to read the settings; any other is refused by the domain
        if (!posted) Assert.Equal(Messages.OnlyPostedCanReverse, thrown.Message);
        Assert.Equal(posted, journal.Contains("settings"));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task AReturnableDocumentUnderAPostedReturn_IsRefusedWithAConflict_AfterItsLockAndTheQuery(string name)
    {
        var row = ReturnableTable.All().Single(r => r.Name == name);
        if (!row.Returnable) return;                                                        // no return can exist of the others

        var doc = row.Build();
        var (journal, thrown) = await RunAsync(doc, ReturnableTable.PostedReturnOf(doc, id: 11));

        Assert.IsType<ConflictException>(thrown);
        Assert.Equal(Messages.ReverseHasReturns(doc.No), thrown.Message);
        Assert.Equal(row.Steps, journal);                                                   // nothing after the query: no settings, no save
    }

    [Fact]
    public async Task AReversedReturnAndACancelledOneAreNotCountedAsReturns()
    {
        var doc = ReturnableTable.Posted(NewReceipt(ReturnableTable.Id, (NewProduct(), 5m, 2m)));
        var returned = ReturnableTable.PostedReturnOf(doc, id: 11);
        returned.MarkReversed("entered twice", Now, Reverser);

        var (journal, thrown) = await RunAsync(doc, returned);

        Assert.IsType<StopHere>(thrown);
        Assert.Equal([$"lock ret {doc.Id}", "query", "settings"], journal);
    }

    private static async Task<(Journal Journal, Exception Thrown)> RunAsync(StockDocument doc, params StockDocument[] others)
    {
        var journal = new Journal();
        var access = new OpenAccess();
        var handler = new ReverseDocumentHandler(new FakeRepository<StockDocument>([doc, .. others], journal), ledger: null!, products: null!, balances: null!, engine: null!,
            new FakeSettings(journal, stopAfterReading: true), access, new FakeReader(), access, new RecordingLocker(journal), new FakeUser(Reverser), new FakeClock(),
            new RecordingWork(journal), new RecordingAudit(journal));
        try
        {
            await handler.HandleAsync(new ReverseDocumentCommand(doc.PublicId, new ReverseRequest("entered twice")), CancellationToken.None);
            throw new InvalidOperationException("The handler was expected to stop.");
        }
        catch (Exception e) when (e is StopHere or DomainException or ConflictException)
        {
            return (journal, e);
        }
    }
}
