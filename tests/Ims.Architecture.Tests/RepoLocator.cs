namespace Ims.Architecture.Tests;

/// <summary>Finds the repository root (the folder holding Ims.sln) by walking up from the test binaries.</summary>
public static class RepoLocator
{
    private const string SolutionFile = "Ims.sln";

    public static string Root { get; } = FindRoot();

    public static string Src => Path.Combine(Root, "src");

    public static string Project(string name) => Path.Combine(Src, name, name + ".csproj");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFile)))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"{SolutionFile} not found above {AppContext.BaseDirectory}");
    }
}
