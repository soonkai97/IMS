namespace Ims.Api.Tests.Support;

/// <summary>Collision-free test data. Every value is unique across the run, so tests never depend on each other's rows.</summary>
public static class Unique
{
    private static long _counter;

    private static string Token() => $"{Interlocked.Increment(ref _counter):D4}{Guid.NewGuid().ToString("N")[..6]}".ToUpperInvariant();

    /// <summary>A SKU of up to 64 characters.</summary>
    public static string Sku(string prefix = "T") => $"{prefix}-{Token()}";

    public static string Name(string prefix = "Item") => $"{prefix} {Token()}";

    /// <summary>A warehouse or unit code (max 20 characters).</summary>
    public static string Code(string prefix = "W") => $"{prefix}{Token()}"[..Math.Min(20, prefix.Length + 10)];

    /// <summary>A 13-digit numeric barcode.</summary>
    public static string Barcode() => $"9{Random.Shared.NextInt64(100_000_000_000, 999_999_999_999)}";

    public static string Username(string prefix = "user") => $"{prefix}_{Token()}".ToLowerInvariant();

    public static string Email(string username) => $"{username}@example.test";
}
