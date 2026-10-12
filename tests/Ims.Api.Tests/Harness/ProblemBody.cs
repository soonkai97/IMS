namespace Ims.Api.Tests.Harness;

/// <summary>The error body the API returns (RFC 7807 plus the IMS extensions).</summary>
public sealed record ProblemBody
{
    public int? Status { get; init; }
    public string? Title { get; init; }
    public string? Detail { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, string[]>? Errors { get; init; }
    public Guid? ProductId { get; init; }
    public decimal? Available { get; init; }
    public decimal? Requested { get; init; }

    /// <summary>Messages of every field error whose property path ends with <paramref name="field"/> (e.g. "Sku" for "Data.Sku").</summary>
    public string[] ErrorsFor(string field) =>
        Errors?.Where(e => e.Key.EndsWith(field, StringComparison.OrdinalIgnoreCase)).SelectMany(e => e.Value).ToArray() ?? [];
}
