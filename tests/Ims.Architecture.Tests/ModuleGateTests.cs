using System.Reflection;
using Ims.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// A module switch hides a surface; it never changes what the ledger does (ADR modules-and-tracking-dimensions 4.3). So: an endpoint that belongs to a module says so with
/// <c>[RequiresModule]</c> (the gate answers 404 while the module is off), the attribute names a module of the catalog, and the posting code never reads a switch.
/// </summary>
public class ModuleGateTests
{
    private static readonly string[] SwitchNames =
        ["IModuleSwitches", "ModuleSwitches", "ModuleCatalog", "ModuleCodes", "ModuleDefinition", "ModulePrefix", "ForModule"];

    /// <summary>The code that posts or reverses a document and moves stock: the Domain stock area and the two use cases that drive it.</summary>
    private static readonly string[] PostingFolders =
        ["Ims.Domain/Stock/", "Ims.Application/Features/Documents/UseCases/PostDocument/", "Ims.Application/Features/Documents/UseCases/ReverseDocument/"];

    [Fact]
    public void EveryController_InTheFolderOfAModule_CarriesRequiresModuleForThatModule()
    {
        Verify.NoOffenders(ModuleControllerOffenders(SourceFiles.In("Ims.Api")), "A controller under Ims.Api/Features/<Module>/ needs [RequiresModule(<that module>)]:");
    }

    [Fact]
    public void EveryRequiresModule_NamesAModuleOfTheCatalog()
    {
        var offenders = SourceFiles.In("Ims.Api").SelectMany(f => RequiredModules(f.Root).Where(r => !ModuleCatalog.IsKnown(r.Code)).Select(r => $"{f.Line(r.Node)}  {r.Code}"));

        Verify.NoOffenders(offenders, "[RequiresModule] must name a code of ModuleCatalog (use the ModuleCodes constant):");
    }

    [Fact]
    public void ThePostingCode_NeverReadsAModuleSwitch()
    {
        var files = SourceFiles.All.Where(f => PostingFolders.Any(p => f.RelativePath.StartsWith(p, StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(files);

        var offenders = files.SelectMany(f => f.Root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(n => SwitchNames.Contains(n.Identifier.Text)).Select(n => $"{f.Line(n)}  {n.Identifier.Text}"));

        Verify.NoOffenders(offenders, "A switch only shows or hides a surface. Posting and reversal read the product, never a module switch:");
    }

    [Fact]
    public void TheModuleGate_IsWiredBetweenAuthenticationAndAuthorization()
    {
        var pipeline = File.ReadAllText(Path.Combine(RepoLocator.Src, "Ims.Api", "Composition", "PipelineSetup.cs"));
        var authentication = pipeline.IndexOf("app.UseAuthentication()", StringComparison.Ordinal);
        var gate = pipeline.IndexOf("UseMiddleware<ModuleGateMiddleware>", StringComparison.Ordinal);
        var authorization = pipeline.IndexOf("app.UseAuthorization()", StringComparison.Ordinal);

        Assert.True(authentication >= 0 && gate > authentication && authorization > gate,
            "ModuleGateMiddleware must run after UseAuthentication and before UseAuthorization, or an off module answers 403 to some callers (a probe).");
    }

    [Theory]
    [InlineData("namespace A; [RequiresModule(ModuleCodes.Purchasing)] class PurchasingController { }", 0)]
    [InlineData("namespace A; [RequiresModule(\"purchasing\")] class PurchasingController { }", 0)]
    [InlineData("namespace A; [Route(\"x\")] [RequiresModule(ModuleCodes.Purchasing)] class PurchasingController { }", 0)]
    [InlineData("namespace A; class PurchasingController { }", 1)]
    [InlineData("namespace A; [RequiresModule(\"other\")] class PurchasingController { }", 1)]
    [InlineData("namespace A; class Helper { }", 0)]
    public void Detector_FlagsAModuleControllerWithoutItsAttribute(string code, int expectedOffenders)
    {
        var file = new[] { (Path: "Ims.Api/Features/Purchasing/PurchasingController.cs", Root: CSharpSyntaxTree.ParseText(code).GetRoot()) };

        Assert.Equal(expectedOffenders, ModuleControllerOffenders(file).Count());
    }

    [Fact]
    public void EveryEndpoint_GuardedByAModulePermission_AlsoCarriesRequiresModuleForThatModule()
    {
        Verify.NoOffenders(ModulePermissionOffenders(SourceFiles.In("Ims.Api").Select(f => (Path: f.RelativePath, f.Root))),
            "An endpoint guarded by a permission that belongs to a module needs [RequiresModule(<that module>)] on the action or its controller, wherever the file sits:");
    }

    [Theory]
    [InlineData("class C { [HttpGet, Authorize(Policy = Permissions.PoRead)] public void A() { } }", 1)]
    [InlineData("[RequiresModule(ModuleCodes.Purchasing)] class C { [HttpGet, Authorize(Policy = Permissions.PoRead)] public void A() { } }", 0)]
    [InlineData("class C { [HttpGet, RequiresModule(ModuleCodes.Purchasing), Authorize(Policy = Permissions.PoRead)] public void A() { } }", 0)]
    [InlineData("class C { [HttpGet, Authorize(Policy = \"po.read\")] public void A() { } }", 1)]
    [InlineData("[Authorize(Policy = Permissions.PoRead)] class C { }", 1)]
    [InlineData("[RequiresModule(\"other\")] class C { [Authorize(Policy = Permissions.PoRead)] public void A() { } }", 1)]
    [InlineData("class C { [HttpGet, Authorize(Policy = Permissions.StockRead)] public void A() { } }", 0)]
    public void Detector_FlagsAModulePermissionOutsideItsGate(string code, int expectedOffenders)
    {
        var file = new[] { (Path: "Ims.Api/Features/Reports/C.cs", Root: (SyntaxNode)CSharpSyntaxTree.ParseText(code).GetRoot()) };

        Assert.Equal(expectedOffenders, ModulePermissionOffenders(file).Count());
    }

    // ----- helpers -----
    private static IEnumerable<string> ModuleControllerOffenders(IEnumerable<SourceFile> files) =>
        ModuleControllerOffenders(files.Select(f => (Path: f.RelativePath, f.Root)));

    private static IEnumerable<string> ModuleControllerOffenders(IEnumerable<(string Path, SyntaxNode Root)> files)
    {
        foreach (var (path, root) in files)
        {
            var segments = path.Split('/');
            if (segments.Length < 4 || segments[1] != "Features") continue;
            var module = ModuleCatalog.All.FirstOrDefault(m => string.Equals(m.Code, segments[2], StringComparison.OrdinalIgnoreCase));
            if (module is null) continue;

            foreach (var controller in root.DescendantNodes().OfType<ClassDeclarationSyntax>().Where(c => c.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)))
                if (!RequiredModules(controller).Any(r => r.Code == module.Code))
                    yield return $"{path}  {controller.Identifier.Text} needs [RequiresModule({module.Code})]";
        }
    }

    /// <summary>Every <c>[Authorize(Policy = X)]</c> whose permission X belongs to a module, on a member or class that has no <c>[RequiresModule]</c> for that module (itself or an enclosing class).</summary>
    private static IEnumerable<string> ModulePermissionOffenders(IEnumerable<(string Path, SyntaxNode Root)> files)
    {
        foreach (var (path, root) in files)
            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>().Where(a => LastName(a.Name) is "Authorize" or "AuthorizeAttribute"))
            {
                var policy = attribute.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == "Policy")?.Expression;
                var code = policy switch
                {
                    LiteralExpressionSyntax literal => literal.Token.ValueText,
                    MemberAccessExpressionSyntax { Expression: var owner } access when LastName(owner) == nameof(Permissions) =>
                        typeof(Permissions).GetField(access.Name.Identifier.Text, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string,
                    _ => null
                };
                if (code is null || PermissionCatalog.Find(code)?.Module is not { } module) continue;

                var owners = attribute.Ancestors().OfType<MemberDeclarationSyntax>(); // the action, then the classes around it
                var gated = owners.SelectMany(o => o.AttributeLists).Any(list => RequiredModules(list).Any(r => r.Code == module));
                if (!gated) yield return $"{path}  Authorize({code}) needs [RequiresModule({module})]";
            }
    }

    /// <summary>The attributes <c>[RequiresModule(..)]</c> under <paramref name="root"/> with the code they name: a string literal, or a constant of <see cref="ModuleCodes"/>.</summary>
    private static IEnumerable<(SyntaxNode Node, string Code)> RequiredModules(SyntaxNode root)
    {
        foreach (var attribute in root.DescendantNodesAndSelf().OfType<AttributeSyntax>().Where(a => LastName(a.Name) is "RequiresModule" or "RequiresModuleAttribute"))
        {
            var argument = attribute.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
            var code = argument switch
            {
                LiteralExpressionSyntax literal => literal.Token.ValueText,
                MemberAccessExpressionSyntax { Expression: var owner } access when LastName(owner) == nameof(ModuleCodes) => ConstantValue(access.Name.Identifier.Text),
                _ => null
            };
            yield return (attribute, code ?? "<not a constant>");
        }
    }

    private static string? ConstantValue(string name) =>
        typeof(ModuleCodes).GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string ?? $"<unknown ModuleCodes.{name}>";

    private static string LastName(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        _ => ""
    };

    private static string LastName(NameSyntax name) => name switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        _ => ""
    };
}
