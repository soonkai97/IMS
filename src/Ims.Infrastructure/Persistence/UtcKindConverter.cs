using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace Ims.Infrastructure.Persistence;

/// <summary>
/// Every instant in the database is UTC (BR-10), but SQL Server's datetime2 carries no zone, so EF reads it back as
/// <see cref="DateTimeKind.Unspecified"/>. This converter stamps the kind on the way in and out, so a value is UTC everywhere in the
/// application and whatever is serialised from it says so.
/// </summary>
public sealed class UtcKindConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
