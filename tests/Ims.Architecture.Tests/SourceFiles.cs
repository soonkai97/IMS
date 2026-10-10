namespace Ims.Architecture.Tests;

/// <summary>Every hand-written C# file under src/ (generated output and migrations excluded), parsed on demand.</summary>
public static class SourceFiles
{
    private static readonly string[] ExcludedSegments = ["obj", "bin", "Migrations"];

    private static readonly Lazy<IReadOnlyList<SourceFile>> AllFiles = new(Load);

    public static IReadOnlyList<SourceFile> All => AllFiles.Value;

    public static IEnumerable<SourceFile> In(string project) =>
        All.Where(f => f.Project.Equals(project, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<SourceFile> Load() =>
        Directory.EnumerateFiles(RepoLocator.Src, "*.cs", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, Path.GetRelativePath(RepoLocator.Src, path).Replace('\\', '/')))
            .Where(f => !f.Segments.Any(s => ExcludedSegments.Contains(s, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
}
