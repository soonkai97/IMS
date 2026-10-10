using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>One public type per file and the folder-per-feature layout, so the tree stays navigable as the system grows.</summary>
public class SourceLayoutTests
{
    private static readonly string[] FeatureFolders = ["Contracts", "Services", "Specifications", "UseCases", "Validators"];
    private static readonly string[] DomainFolders = ["Models", "Rules", "Services", "Exceptions", "Constants"];

    [Fact]
    public void SourceFiles_AreDiscovered()
    {
        Assert.True(SourceFiles.All.Count > 100, $"Only {SourceFiles.All.Count} files found under {RepoLocator.Src}");
    }

    [Fact]
    public void EveryFile_DeclaresAtMostOneTopLevelType()
    {
        var offenders = SourceFiles.All
            .Select(f => (file: f, types: TopLevelTypes(f.Root).ToList()))
            .Where(x => x.types.Count > 1)
            .Select(x => $"{x.file.RelativePath}: {string.Join(", ", x.types)}");

        Verify.NoOffenders(offenders, "One top-level type per file:");
    }

    [Fact]
    public void EveryTypeFile_IsNamedAfterItsType()
    {
        var offenders = SourceFiles.All
            .Select(f => (file: f, types: TopLevelTypes(f.Root).ToList()))
            .Where(x => x.types.Count == 1 && !string.Equals(StripArity(x.types[0]), Path.GetFileNameWithoutExtension(x.file.FullPath), StringComparison.Ordinal))
            .Select(x => $"{x.file.RelativePath}: declares {x.types[0]}");

        Verify.NoOffenders(offenders, "A file must be named after the type it declares:");
    }

    // ----- Application: Features/<Feature>/<Contracts|Services|Specifications|UseCases|Validators>/... -----
    [Fact]
    public void ApplicationFeatures_FollowFeatureFolderLayout()
    {
        var offenders = SourceFiles.In("Ims.Application")
            .Where(f => f.Segments.Length > 1 && f.Segments[1] == "Features")
            .Where(f => f.Segments.Length < 5 || !FeatureFolders.Contains(f.Segments[3]))
            .Select(f => f.RelativePath);

        Verify.NoOffenders(offenders,
            $"Files under Features must live in Features/<Feature>/<{string.Join("|", FeatureFolders)}>/:");
    }

    [Fact]
    public void ApplicationFeatures_NamespaceMatchesTheFeatureFolder()
    {
        var offenders = SourceFiles.In("Ims.Application")
            .Where(f => f.Segments.Length > 4 && f.Segments[1] == "Features")
            .Where(f => NamespaceOf(f) != $"Ims.Application.Features.{f.Segments[2]}")
            .Select(f => $"{f.RelativePath}: namespace {NamespaceOf(f)}");

        Verify.NoOffenders(offenders, "Namespace must be Ims.Application.Features.<Feature>:");
    }

    [Fact]
    public void UseCases_LiveInOneFolderPerUseCase_WithAMatchingHandler()
    {
        var useCaseFiles = SourceFiles.In("Ims.Application")
            .Where(f => f.Segments.Length > 4 && f.Segments[1] == "Features" && f.Segments[3] == "UseCases")
            .ToList();

        var looseFiles = useCaseFiles.Where(f => f.Segments.Length != 6).Select(f => f.RelativePath);
        Verify.NoOffenders(looseFiles, "Use-case files must sit in UseCases/<UseCase>/ (one folder per use case):");

        var foldersWithoutHandler = useCaseFiles.Where(f => f.Segments.Length == 6)
            .GroupBy(f => string.Join('/', f.Segments.Take(5)))
            .Where(g => !g.Any(f => Path.GetFileNameWithoutExtension(f.FullPath) == g.First().Segments[4] + "Handler"))
            .Select(g => g.Key);
        Verify.NoOffenders(foldersWithoutHandler, "Every use-case folder needs a <UseCase>Handler:");

        var foldersWithoutRequest = useCaseFiles.Where(f => f.Segments.Length == 6)
            .GroupBy(f => string.Join('/', f.Segments.Take(5)))
            .Where(g => !g.Any(f => Path.GetFileNameWithoutExtension(f.FullPath) is var n
                                    && (n == g.First().Segments[4] + "Command" || n == g.First().Segments[4] + "Query")))
            .Select(g => g.Key);
        Verify.NoOffenders(foldersWithoutRequest, "Every use-case folder needs a <UseCase>Command or <UseCase>Query:");
    }

    // ----- Domain: <Area>/<Models|Rules|Services|Exceptions|Constants>/ -----
    [Fact]
    public void Domain_FollowsAreaThenKindLayout()
    {
        var offenders = SourceFiles.In("Ims.Domain")
            .Where(f => f.RelativePath != "Ims.Domain/Common/Entity.cs")
            .Where(f => f.Segments.Length != 4 || !DomainFolders.Contains(f.Segments[2]))
            .Select(f => f.RelativePath);

        Verify.NoOffenders(offenders, $"Domain files must be Ims.Domain/<Area>/<{string.Join("|", DomainFolders)}>/<File>.cs:");
    }

    [Fact]
    public void Domain_UsesTheSingleFlatNamespace()
    {
        var offenders = SourceFiles.In("Ims.Domain")
            .Where(f => NamespaceOf(f) != "Ims.Domain")
            .Select(f => $"{f.RelativePath}: namespace {NamespaceOf(f)}");

        Verify.NoOffenders(offenders, "All Domain types live in namespace Ims.Domain:");
    }

    // ----- helpers -----
    private static IEnumerable<string> TopLevelTypes(SyntaxNode root)
    {
        foreach (var member in root.ChildNodes())
        {
            foreach (var name in TypeNamesIn(member))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<string> TypeNamesIn(SyntaxNode node)
    {
        switch (node)
        {
            case BaseTypeDeclarationSyntax type:
                yield return type.Identifier.Text + Arity(type);
                break;
            case DelegateDeclarationSyntax del:
                yield return del.Identifier.Text;
                break;
            case BaseNamespaceDeclarationSyntax ns:
                foreach (var inner in ns.Members.SelectMany(TypeNamesIn))
                {
                    yield return inner;
                }

                break;
        }
    }

    private static string Arity(BaseTypeDeclarationSyntax type) =>
        type is TypeDeclarationSyntax { TypeParameterList: { } list } ? $"`{list.Parameters.Count}" : "";

    private static string StripArity(string name) => name.Split('`')[0];

    private static string NamespaceOf(SourceFile file) =>
        file.Root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
}
