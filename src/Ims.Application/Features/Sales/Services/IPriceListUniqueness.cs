using Ims.Domain;
namespace Ims.Application.Features.Sales;

public interface IPriceListUniqueness
{
    /// <summary>The checks that need the database, with a sentence for the person; the unique indexes stay the backstop for a race.</summary>
    /// <exception cref="ConflictException">Another list has the name, is the default (when this one is), or belongs to the customer (when this one does).</exception>
    Task EnsureAsync(PriceList list, CancellationToken ct);
}
