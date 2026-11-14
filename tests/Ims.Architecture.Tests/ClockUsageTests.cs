using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// The clock is read in one place: <c>SystemClock</c>, behind <c>IClock</c>. Code that reads <c>DateTime.UtcNow</c> (or Now, Today, DateTimeOffset.UtcNow or Now) directly cannot be tested with a fixed or
/// movable clock, and two reads in one operation can disagree. The Application and Domain layers get the time from <c>IClock</c> or from a value passed in.
/// </summary>
public class ClockUsageTests
{
    private static readonly string[] ClockTypes = ["DateTime", "DateTimeOffset"];
    private static readonly string[] ClockMembers = ["UtcNow", "Now", "Today"];

    /// <summary>The one file that may read the system clock.</summary>
    private const string SystemClockFile = "Ims.Infrastructure/Security/SystemClock.cs";

    [Fact]
    public void TheSystemClockFile_Exists()
    {
        Assert.Contains(SourceFiles.All, f => f.RelativePath == SystemClockFile);
    }

    [Fact]
    public void NoCode_ReadsTheSystemClockDirectly_ExceptSystemClock()
    {
        var offenders = SourceFiles.All
            .Where(f => f.RelativePath != SystemClockFile)
            .SelectMany(f => f.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Where(IsClockRead).Select(f.Line));

        Verify.NoOffenders(offenders, "Read the time through IClock (or take it as a parameter), never DateTime.UtcNow, Now, Today or DateTimeOffset.UtcNow, Now directly:");
    }

    private static bool IsClockRead(MemberAccessExpressionSyntax access) =>
        ClockMembers.Contains(access.Name.Identifier.Text)
        && access.Expression switch
        {
            IdentifierNameSyntax id => ClockTypes.Contains(id.Identifier.Text),
            MemberAccessExpressionSyntax qualified => ClockTypes.Contains(qualified.Name.Identifier.Text) && qualified.Expression.ToString() == "System",
            _ => false,
        };
}
