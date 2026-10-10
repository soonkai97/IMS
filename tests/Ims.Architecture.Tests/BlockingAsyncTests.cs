using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>Deadlock rule: never block on a task. Always await (<c>.Result</c>, <c>.Wait()</c>, <c>.GetAwaiter().GetResult()</c> are banned).</summary>
public class BlockingAsyncTests
{
    private static readonly string[] BlockingMethods = ["Wait", "WaitAll", "WaitAny", "GetResult"];

    [Fact]
    public void Source_NeverBlocksOnTasks()
    {
        var offenders = SourceFiles.All.SelectMany(file => file.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
            .Where(IsBlockingCall)
            .Select(access => $"{file.Line(access)}  {access.Parent}".Replace(Environment.NewLine, " ")));

        Verify.NoOffenders(offenders, "Blocking async calls are banned (use await):");
    }

    [Theory]
    [InlineData("class C { int M(System.Threading.Tasks.Task<int> t) => t.Result; }", true)]
    [InlineData("class C { void M(System.Threading.Tasks.Task t) { t.Wait(); } }", true)]
    [InlineData("class C { void M(System.Threading.Tasks.Task t) { t.GetAwaiter().GetResult(); } }", true)]
    [InlineData("class C { void M(System.Threading.Tasks.Task[] t) { System.Threading.Tasks.Task.WaitAll(t); } }", true)]
    [InlineData("class C { async System.Threading.Tasks.Task M(System.Threading.Tasks.Task t) { await t; } }", false)]
    [InlineData("class C { string Result => \"\"; string M() => Result; }", false)]
    public void Detector_FlagsOnlyBlockingCalls(string code, bool expectedFlagged)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();

        var flagged = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(IsBlockingCall);

        Assert.Equal(expectedFlagged, flagged);
    }

    private static bool IsBlockingCall(MemberAccessExpressionSyntax access)
    {
        var name = access.Name.Identifier.Text;
        if (name == "Result")
        {
            // Property access, not an invocation or a nameof/declaration; `this.Result` / `Result` alone are other members.
            return access.Parent is not InvocationExpressionSyntax && access.Expression is not ThisExpressionSyntax;
        }

        return BlockingMethods.Contains(name) && access.Parent is InvocationExpressionSyntax;
    }
}
