using System.Xml.Linq;

namespace Ims.Architecture.Tests;

/// <summary>The declared references of one src/ project, read from its .csproj.</summary>
public sealed class ProjectFile
{
    private readonly XDocument _document;

    private ProjectFile(string name)
    {
        _document = XDocument.Load(RepoLocator.Project(name));
    }

    public static ProjectFile Of(string name) => new(name);

    public IReadOnlyList<string> ProjectReferences => Includes("ProjectReference")
        .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
        .ToList();

    public IReadOnlyList<string> PackageReferences => Includes("PackageReference").ToList();

    private IEnumerable<string> Includes(string element) =>
        _document.Descendants(element).Select(e => (string?)e.Attribute("Include")).OfType<string>();
}
