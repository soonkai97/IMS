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
