namespace Ims.Api.Tests.Harness;

/// <summary>Hands out distinct fake client addresses (10.x.y.z) so each test client has its own rate-limit partition.</summary>
public static class ClientAddresses
{
    private static int _counter;

    public static string Next()
    {
        var n = Interlocked.Increment(ref _counter);
        return $"10.{(n >> 16) & 0xFF}.{(n >> 8) & 0xFF}.{n & 0xFF}";
    }
}
