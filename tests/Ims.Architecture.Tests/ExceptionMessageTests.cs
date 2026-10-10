using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// No magic strings: user-facing sentences live in Ims.Domain/Common/Constants/Messages.cs only, so wording stays
/// consistent and translatable. Exceptions must be built from a <c>Messages.*</c> member, never a string literal.
/// </summary>
public class ExceptionMessageTests
{
    private static readonly string[] GuardedExceptions =
    [
        "DomainException", "ConflictException", "NotFoundException", "ValidationException",
        "ForbiddenException", "UnauthorizedException", "InsufficientStockException"
    ];

    [Fact]
    public void MessagesFile_Exists_AndIsTheOnlyHomeOfUserFacingSentences()
    {
        var messages = SourceFiles.All.Where(f => f.RelativePath == "Ims.Domain/Common/Constants/Messages.cs").ToList();

        Assert.Single(messages);
    }

    [Fact]
    public void Exceptions_AreNeverConstructedWithAStringLiteral()
    {
        var offenders = SourceFiles.All.SelectMany(file => file.Root.DescendantNodes()
            .Where(node => HasLiteralMessage(node))
            .Select(node => $"{file.Line(node)}  {Shorten(node)}"));

        Verify.NoOffenders(offenders, "Build exceptions from Messages.* members, not string literals:");
    }

    [Theory]
    [InlineData("class C { void M() { throw new DomainException(\"Nope.\"); } }", true)]
    [InlineData("class C { void M(int x) { throw new NotFoundException($\"Item {x} missing\"); } }", true)]
    [InlineData("class C { void M() { throw new ConflictException(\"a\" + \"b\"); } }", true)]
    [InlineData("class C : DomainException { C() : base(\"Nope.\") { } }", true)]
    [InlineData("class C(int x) : DomainException(\"Nope.\");", true)]
    [InlineData("class C { void M() { throw new DomainException(Messages.NoLines); } }", false)]
    [InlineData("class C { void M(int x) { throw new DomainException(Messages.NotEditable(x)); } }", false)]
    [InlineData("class C { void M() { throw new ArgumentException(\"fine, not a domain exception\"); } }", false)]
    public void Detector_FlagsOnlyLiteralMessagesOfGuardedExceptions(string code, bool expectedFlagged)
    {
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();

        Assert.Equal(expectedFlagged, root.DescendantNodes().Any(HasLiteralMessage));
    }

    private static bool HasLiteralMessage(SyntaxNode node) => node switch
    {
        ObjectCreationExpressionSyntax creation =>
            IsGuarded(creation.Type) && FirstArgumentIsLiteral(creation.ArgumentList),
        PrimaryConstructorBaseTypeSyntax primary =>
            IsGuarded(primary.Type) && FirstArgumentIsLiteral(primary.ArgumentList),
        ConstructorInitializerSyntax init =>
            init.ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword) && IsInGuardedType(init) && FirstArgumentIsLiteral(init.ArgumentList),
        _ => false
    };

    private static bool IsGuarded(TypeSyntax type) =>
        GuardedExceptions.Contains(type switch
        {
            QualifiedNameSyntax q => q.Right.Identifier.Text,
            SimpleNameSyntax s => s.Identifier.Text,
            _ => type.ToString()
        });

    private static bool IsInGuardedType(ConstructorInitializerSyntax init) =>
        init.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.BaseList?.Types
            .Any(t => IsGuarded(t.Type)) == true;

    /// <summary>True when the first argument is, or is built from, a string literal (plain, interpolated or concatenated).</summary>
    private static bool FirstArgumentIsLiteral(ArgumentListSyntax? arguments)
    {
        var first = arguments?.Arguments.FirstOrDefault()?.Expression;
        return first is not null && ContainsLiteral(first);
    }

    private static bool ContainsLiteral(ExpressionSyntax expression) => expression switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AddExpression) =>
            ContainsLiteral(binary.Left) || ContainsLiteral(binary.Right),
        ParenthesizedExpressionSyntax paren => ContainsLiteral(paren.Expression),
        _ => false
    };

    private static string Shorten(SyntaxNode node)
    {
        var text = node.ToString().ReplaceLineEndings(" ");
        return text.Length <= 120 ? text : text[..117] + "...";
    }
}
