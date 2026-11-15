using Ims.Application.Features.Sales;

namespace Ims.Infrastructure.Persistence;

/// <summary>STUB (DDD-13 red commit): finds nothing yet.</summary>
public sealed class EfPriceListLabelReader(AppDbContext db) : IPriceListLabelReader
{
    private readonly AppDbContext _db = db;

    public Task<PriceListLabels> ReadAsync(IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> uomIds, CancellationToken ct) =>
        Task.FromResult(new PriceListLabels(new Dictionary<int, string>(), new Dictionary<int, string>()));
}
