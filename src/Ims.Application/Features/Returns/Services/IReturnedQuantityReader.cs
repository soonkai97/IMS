using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>The posted returned quantities of a document per original line position, summed by the database (the answer is a few rows however many returns there are). It runs under the document ret lock when a return posts.</summary>
public interface IReturnedQuantityReader
{
    Task<IReadOnlyDictionary<int, ReturnedQuantity>> PostedByLineAsync(int originalKey, CancellationToken ct);
}
