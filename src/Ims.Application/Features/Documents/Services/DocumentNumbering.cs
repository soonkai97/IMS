using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentNumbering(IRepository<DocumentSequence> sequences, ITransactionLocker locker, IClock clock) : IDocumentNumbering, IAppService
{
    public async Task<string> NextAsync(DocumentType type, Warehouse warehouse, CancellationToken ct)
    {
        var prefix = DocumentNumber.PrefixFor(type);
        var yearMonth = DocumentNumber.YearMonthOf(clock.UtcNow);
        // One allocation at a time per sequence: concurrent creates queue here instead of colliding and retrying.
        await locker.LockDocumentSequenceAsync(prefix, yearMonth, warehouse.Id, ct);
        var sequence = await sequences.FirstOrDefaultAsync(new DocumentSequenceSpec(prefix, yearMonth, warehouse.Id), ct);
        if (sequence is null)
        {
            sequence = DocumentSequence.Start(prefix, yearMonth, warehouse.Id);
            sequences.Add(sequence);
        }
        return DocumentNumber.Format(prefix, warehouse.Code, yearMonth, sequence.Next());
    }
}
