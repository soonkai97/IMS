using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Ims.Architecture.Tests;

public sealed class SourceFile(string fullPath, string relativePath)
{
    private readonly Lazy<SyntaxTree> _tree = new(() => CSharpSyntaxTree.ParseText(File.ReadAllText(fullPath), path: fullPath));

    public string FullPath => fullPath;

    /// <summary>Path relative to src/, with forward slashes (for example <c>Ims.Domain/Stock/Models/StockBalance.cs</c>).</summary>
    public string RelativePath => relativePath;

    public string[] Segments => relativePath.Split('/');

    public string Project => Segments[0];

    public SyntaxTree Tree => _tree.Value;

    public SyntaxNode Root => Tree.GetRoot();

    public string Line(SyntaxNode node) =>
        $"{relativePath}:{Tree.GetLineSpan(node.Span).StartLinePosition.Line + 1}";
}
