using Xunit.Sdk;

namespace Ims.Architecture.Tests;

/// <summary>Failure helper that lists every offender in the message instead of stopping at the first.</summary>
public static class Verify
{
    public static void NoOffenders(IEnumerable<string> offenders, string rule)
    {
        var list = offenders.Distinct().Order().ToList();
        if (list.Count == 0)
        {
            return;
        }

        throw new XunitException($"{rule}{Environment.NewLine}{list.Count} offender(s):{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", list)}");
    }
}
