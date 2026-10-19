using System.Globalization;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>Some messages write a decimal with the current culture; the fixtures are written and read with the invariant one, whatever machine runs them.</summary>
internal static class OracleCulture
{
    public static T Invariant<T>(Func<T> run)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return run();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
