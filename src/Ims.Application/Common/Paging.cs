using Ims.Domain;
namespace Ims.Application;

public static class Paging
{
    /// <summary>Returned in <see cref="PagedResult{T}.Total"/> when the count was skipped.</summary>
    public const int TotalNotCounted = -1;

    /// <exception cref="ValidationException">The requested page starts beyond <see cref="Limits.MaxOffsetRows"/>.</exception>
    public static (int Page, int Size) Normalize(int page, int size, int max = Limits.MaxPageSize)
    {
        var p = Math.Clamp(page, 1, Limits.MaxPageNumber);
        var s = Math.Clamp(size, 1, max);
        if ((long)(p - 1) * s > Limits.MaxOffsetRows) throw new ValidationException(Messages.PageTooDeep);
        return (p, s);
    }

    /// <summary>The cursor for the next seek page: the id of the last row, when the page is full.</summary>
    public static long? NextCursor<T>(IReadOnlyList<T> items, int size, Func<T, long> id) => items.Count == size && items.Count > 0 ? id(items[^1]) : null;

    /// <summary>Start of a Malaysia-local day (UTC+8) expressed in UTC, for inclusive date filters.</summary>
    public static DateTime StartOfDayUtc(DateOnly d) => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-Limits.MalaysiaUtcOffsetHours);
    public static DateTime EndOfDayUtc(DateOnly d) => StartOfDayUtc(d).AddDays(1);
}
