using System.Reflection;

namespace Ims.Architecture.Tests;

/// <summary>Dependencies point inward: Domain knows nothing, Application knows only Domain, Infrastructure never reaches the API.</summary>
public class LayerDependencyTests
{
    private const string DomainName = "Ims.Domain";
    private const string ApplicationName = "Ims.Application";
    private const string InfrastructureName = "Ims.Infrastructure";
    private const string ApiName = "Ims.Api";

    private static readonly string[] BaseClassLibraryPrefixes = ["System", "mscorlib", "netstandard", "Microsoft.CSharp"];

    private static readonly string[] ForbiddenInApplication =
    [
        "Microsoft.EntityFrameworkCore", "ClosedXML", "Microsoft.AspNetCore", InfrastructureName, ApiName
    ];

    private static IEnumerable<string> ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");

    private static bool StartsWithAny(string name, IEnumerable<string> prefixes) =>
        prefixes.Any(p => name.Equals(p, StringComparison.Ordinal) || name.StartsWith(p + ".", StringComparison.Ordinal));

    // ----- Domain -----
    [Fact]
    public void Domain_ReferencesNothingButTheBaseClassLibrary()
    {
        var offenders = ReferencedNames(Layers.Domain).Where(n => !StartsWithAny(n, BaseClassLibraryPrefixes));

        Verify.NoOffenders(offenders, $"{DomainName} may reference only the BCL (System.*), but references:");
    }

    [Fact]
    public void Domain_ProjectFile_DeclaresNoProjectOrPackageReferences()
    {
        var project = ProjectFile.Of(DomainName);

        Verify.NoOffenders(project.ProjectReferences.Concat(project.PackageReferences),
            $"{DomainName}.csproj must not declare any ProjectReference or PackageReference:");
    }

    [Fact]
    public void Domain_UsesNoPersistenceAttributes()
    {
        // The reference tests above cannot see [NotMapped] and [Column]: they ship with the base library. The mapping belongs to Infrastructure (AppDbContext).
        var offenders = PersistenceAttributeRules.Offenders(Layers.TypesOf(Layers.Domain));

        Verify.NoOffenders(offenders, $"{DomainName} must not carry persistence attributes (Microsoft.EntityFrameworkCore, Ardalis, System.ComponentModel.DataAnnotations.Schema); map it in {InfrastructureName}:");
    }

    [Fact]
    public void Domain_UsesNoPersistenceNamespaceInItsSource()
    {
        var offenders = SourceFiles.In(DomainName)
            .SelectMany(f => f.Root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>()
                .Where(u => PersistenceAttributeRules.IsPersistenceNamespace(u.Name?.ToString()))
                .Select(u => $"{f.Line(u)}  using {u.Name}"));

        Verify.NoOffenders(offenders, $"{DomainName} must not import a persistence namespace:");
    }

    [Theory]
    [InlineData(typeof(PersistenceFixtures.NotMappedProperty), "NotMappedAttribute")]
    [InlineData(typeof(PersistenceFixtures.ColumnOnProperty), "ColumnAttribute")]
    [InlineData(typeof(PersistenceFixtures.OwnedType), "OwnedAttribute")]
    public void ThePersistenceRule_FindsAnAttributeOnATypeOrAMember(Type type, string attribute)
    {
        Assert.Contains(PersistenceAttributeRules.Offenders([type]), o => o.Contains(attribute, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Metadata", true)]
    [InlineData("Ardalis.Specification", true)]
    [InlineData("System.ComponentModel.DataAnnotations.Schema", true)]
    [InlineData("System.ComponentModel.DataAnnotations", false)]   // [Required] and [MaxLength] are validation, not mapping
    [InlineData("System.Text.Json", false)]
    [InlineData(null, false)]
    public void ThePersistenceRule_KnowsTheNamespaces(string? name, bool persistence)
    {
        Assert.Equal(persistence, PersistenceAttributeRules.IsPersistenceNamespace(name));
    }

    [Fact]
    public void ThePersistenceRule_AcceptsAPlainType()
    {
        Assert.Empty(PersistenceAttributeRules.Offenders([typeof(PersistenceFixtures.Plain)]));
    }

    // ----- Application -----
    [Fact]
    public void Application_ReferencesOnlyDomainAmongTheSolutionsAssemblies()
    {
        var offenders = ReferencedNames(Layers.Application)
            .Where(n => n.StartsWith("Ims.", StringComparison.Ordinal) && n != DomainName);

        Verify.NoOffenders(offenders, $"{ApplicationName} may reference only {DomainName}, but references:");
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructureFrameworks()
    {
        var offenders = ReferencedNames(Layers.Application).Where(n => StartsWithAny(n, ForbiddenInApplication));

        Verify.NoOffenders(offenders,
            $"{ApplicationName} must stay free of EF Core, ClosedXML, ASP.NET Core, {InfrastructureName} and {ApiName}, but references:");
    }

    [Fact]
    public void Application_ProjectFile_ReferencesOnlyDomainAndNoForbiddenPackages()
    {
        var project = ProjectFile.Of(ApplicationName);

        var badProjects = project.ProjectReferences.Where(p => p != DomainName);
        var badPackages = project.PackageReferences.Where(p => StartsWithAny(p, ForbiddenInApplication));

        Verify.NoOffenders(badProjects.Concat(badPackages), $"{ApplicationName}.csproj declares a forbidden reference:");
    }

    // ----- Infrastructure -----
    [Fact]
    public void Infrastructure_DoesNotReferenceApi()
    {
        var offenders = ReferencedNames(Layers.Infrastructure).Where(n => StartsWithAny(n, [ApiName]));

        Verify.NoOffenders(offenders, $"{InfrastructureName} must not depend on {ApiName}, but references:");
    }

    [Fact]
    public void Infrastructure_ProjectFile_DoesNotReferenceApi()
    {
        var offenders = ProjectFile.Of(InfrastructureName).ProjectReferences.Where(p => p == ApiName);

        Verify.NoOffenders(offenders, $"{InfrastructureName}.csproj must not reference {ApiName}:");
    }
}

/// <summary>Finds persistence attributes on the types of an assembly and on their members.</summary>
internal static class PersistenceAttributeRules
{
    private static readonly string[] Namespaces = ["Microsoft.EntityFrameworkCore", "Ardalis", "System.ComponentModel.DataAnnotations.Schema"];

    private const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;

    public static bool IsPersistenceNamespace(string? name) =>
        name is not null && Namespaces.Any(ns => name.Equals(ns, StringComparison.Ordinal) || name.StartsWith(ns + ".", StringComparison.Ordinal));

    public static IEnumerable<string> Offenders(IEnumerable<Type> types)
    {
        foreach (var type in types)
        {
            foreach (var attribute in Attributes(type, type.GetMembers(All)))
            {
                yield return $"{type.Name}: {attribute}";
            }
        }
    }

    private static IEnumerable<string> Attributes(Type type, IEnumerable<System.Reflection.MemberInfo> members)
    {
        var onType = type.GetCustomAttributesData();
        var onMembers = members.SelectMany(m => m.GetCustomAttributesData());
        return onType.Concat(onMembers)
            .Where(a => IsPersistenceNamespace(a.AttributeType.Namespace))
            .Select(a => a.AttributeType.Name);
    }
}

/// <summary>Fixtures for the rule; they sit in the test assembly, which the real-assembly rule never scans.</summary>
internal static class PersistenceFixtures
{
    public sealed class NotMappedProperty
    {
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int Total => 1;
    }

    public sealed class ColumnOnProperty
    {
        [System.ComponentModel.DataAnnotations.Schema.Column("X")]
        public int Total { get; } = 1;
    }

    [Microsoft.EntityFrameworkCore.Owned]
    public sealed class OwnedType;

    public sealed class Plain
    {
        [System.ComponentModel.DataAnnotations.Required]
        public string Name { get; } = "";
    }
}
