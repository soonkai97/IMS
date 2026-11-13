using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// Concrete classes and records are sealed unless they are meant to be inherited: a sealed type cannot be subclassed by accident, the JIT can devirtualise calls on it, and the codebase says one thing, not two.
/// Nothing in the solution mocks or proxies these types. CA1852 cannot enforce this for public types, so the rule is checked here on the source. Abstract and static classes, structs, records that are
/// structs, and the allow-list below are exempt. A new base class goes on the list on purpose, in the pull request that adds it.
/// </summary>
public class SealedClassesTests
{
    /// <summary>Types that are inherited from (the exception bases) or are an anchor for the test host (Program). Every entry needs a reason.</summary>
    private static readonly HashSet<string> AllowList =
    [
        "Program",               // partial class, the target of WebApplicationFactory in the integration tests
        "ConcurrencyException",  // base of the domain concurrency exceptions
        "ConflictException",     // base of the 409 exceptions (OverReturnException, ...)
        "DomainException",       // base of every domain rule exception
        "UnauthorizedException", // base of the permission exceptions
    ];

    [Fact]
    public void ConcreteClassesAndRecords_AreSealed()
    {
        var offenders = SourceFiles.All
            .SelectMany(f => f.Root.DescendantNodes().OfType<TypeDeclarationSyntax>().Select(t => (file: f, type: t)))
            .Where(x => IsClassLike(x.type) && IsVisibleOutsideItsFile(x.type) && !IsExempt(x.type))
            .Select(x => $"{x.file.Line(x.type)}  {x.type.Identifier.Text}");

        Verify.NoOffenders(offenders, "Concrete classes and records are `sealed` (or go on the allow-list in SealedClassesTests, with a reason):");
    }

    [Fact]
    public void AllowList_OnlyNamesTypesThatExist()
    {
        var declared = SourceFiles.All
            .SelectMany(f => f.Root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Select(t => t.Identifier.Text)
            .ToHashSet();

        Verify.NoOffenders(AllowList.Where(name => !declared.Contains(name)), "Allow-list entries that no longer name a type (remove them):");
    }

    private static bool IsClassLike(TypeDeclarationSyntax type) =>
        type.IsKind(SyntaxKind.ClassDeclaration) || type.IsKind(SyntaxKind.RecordDeclaration);

    // The same scope the sealing covered: public and internal types (nested ones included); private helpers are not part of the rule.
    private static bool IsVisibleOutsideItsFile(TypeDeclarationSyntax type) =>
        type.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

    private static bool IsExempt(TypeDeclarationSyntax type) =>
        type.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword) || m.IsKind(SyntaxKind.AbstractKeyword) || m.IsKind(SyntaxKind.StaticKeyword))
        || AllowList.Contains(type.Identifier.Text);
}
