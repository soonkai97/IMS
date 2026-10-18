using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Ims.Architecture.Tests;

/// <summary>
/// A role is data (ADR roles-permissions, RP-4a and RP-4b): what a caller may do comes from the <c>perm</c>, <c>allwh</c> and <c>wh</c> claims of the token,
/// never from a role name. These tests fail the build if code in src/ asks whether a caller is in a role, reads the role claim, or compares a system role code
/// outside the short allow-lists below. Migrations are not scanned (they are frozen history). A new entry needs a reason, and an entry whose code no longer
/// uses the role is itself a failure, so the lists only shrink.
/// </summary>
public class NoRoleDecisionTests
{
    public enum RoleUse
    {
        /// <summary><c>IsInRole</c>, <c>RequireRole</c>, <c>[Authorize(Roles = ...)]</c>: a role decides access. Never allowed.</summary>
        RoleGate,

        /// <summary><c>ClaimTypes.Role</c> or the <c>role</c> claim name: reading or writing the role claim.</summary>
        RoleClaim,

        /// <summary><c>SystemRoleCodes</c> or a literal equal to one of its codes: naming a system role.</summary>
        RoleName
    }

    /// <summary>Files that may name the role claim, each with the reason it is not a decision.</summary>
    private static readonly Dictionary<string, string> RoleClaimAllowed = new()
    {
        ["Ims.Api/Common/CurrentUser.cs"] = "exposes the role code of the token for display (user info, audit); Rights() builds access from perm claims",
        ["Ims.Api/Composition/ConfigureJwtBearer.cs"] = "names the role claim type of the principal; nothing may call IsInRole (banned above)",
        ["Ims.Infrastructure/Security/TokenService.cs"] = "writes the role claim into the token, for display and audit only"
    };

    /// <summary>Files that may name a system role, each with the reason it is not a decision.</summary>
    private static readonly Dictionary<string, string> RoleNameAllowed = new()
    {
        ["Ims.Domain/Identity/Services/SystemRoleCodes.cs"] = "declares the five codes",
        ["Ims.Domain/Identity/Services/SystemRoleDefaults.cs"] = "the table of default grants of the system roles (read by tests; the migration seeds a frozen copy)",
        ["Ims.Infrastructure/SeedDefaults.cs"] = "names the roles of the demo users it seeds",
        ["Ims.Infrastructure/Persistence/Seeder.cs"] = "seeds the first administrator when none exists (bootstrap, not an access decision)",
        ["Ims.Application/Common/ApproverTierNames.cs"] = "shows the deprecated roleRequired text in the rule and trigger responses (output only); removed with those fields when the web stops printing them"
    };

    [Fact]
    public void NoCode_AsksWhetherACallerIsInARole()
    {
        var offenders = Scan(RoleUse.RoleGate, allowed: []);

        Verify.NoOffenders(offenders, "Access comes from permission claims: no IsInRole, RequireRole or [Authorize(Roles = ...)] in src/:");
    }

    [Fact]
    public void TheRoleClaim_IsNamedOnlyWhereListed()
    {
        var offenders = Scan(RoleUse.RoleClaim, RoleClaimAllowed.Keys);

        Verify.NoOffenders(offenders, "The role claim is for display and audit; decide from perm, allwh and wh. Add a file to RoleClaimAllowed only with a reason:");
    }

    [Fact]
    public void TheSystemRoleNames_AreNamedOnlyWhereListed()
    {
        var offenders = Scan(RoleUse.RoleName, RoleNameAllowed.Keys);

        Verify.NoOffenders(offenders, "Do not compare against a system role (SystemRoleCodes or its text): ask for a permission. Add a file to RoleNameAllowed only with a reason:");
    }

    [Fact]
    public void EveryAllowListEntry_ExistsAndStillNamesTheRole()
    {
        var offenders = RoleClaimAllowed.Keys.Select(f => (f, RoleUse.RoleClaim)).Concat(RoleNameAllowed.Keys.Select(f => (f, RoleUse.RoleName)))
            .Where(x => !SourceFiles.All.Any(file => file.RelativePath == x.f && Uses(file.Root).Any(u => u.Use == x.Item2)))
            .Select(x => $"{x.f} ({x.Item2})");

        Verify.NoOffenders(offenders, "Remove an allow-list entry whose file is gone or no longer names the role:");
    }

    [Theory]
    [InlineData("class C { bool M(System.Security.Claims.ClaimsPrincipal p) => p.IsInRole(\"Boss\"); }", RoleUse.RoleGate)]
    [InlineData("class C { bool M(System.Security.Claims.ClaimsPrincipal? p) => p?.IsInRole(x) == true; }", RoleUse.RoleGate)]
    [InlineData("class C { void M(Builder b) { b.RequireRole(\"Boss\"); } }", RoleUse.RoleGate)]
    [InlineData("[Authorize(Roles = \"Boss\")] class C { }", RoleUse.RoleGate)]
    [InlineData("[Microsoft.AspNetCore.Authorization.AuthorizeAttribute(Roles = Names.Boss)] class C { }", RoleUse.RoleGate)]
    [InlineData("class C { object M() => new AuthorizeAttribute { Roles = \"Boss\" }; }", RoleUse.RoleGate)]
    [InlineData("class C { string? M(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Role); }", RoleUse.RoleClaim)]
    [InlineData("class C { string? M(ClaimsPrincipal p) => p.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value; }", RoleUse.RoleClaim)]
    [InlineData("class C { string? M(ClaimsPrincipal p) => p.FindFirstValue(\"role\"); }", RoleUse.RoleClaim)]
    [InlineData("class C { string K = \"http://schemas.microsoft.com/ws/2008/06/identity/claims/role\"; }", RoleUse.RoleClaim)]
    [InlineData("using static System.Security.Claims.ClaimTypes; class C { }", RoleUse.RoleClaim)]
    [InlineData("class C { bool M(string role) => role == SystemRoleCodes.Admin; }", RoleUse.RoleName)]
    [InlineData("class C { bool M(string role) => role == Ims.Domain.SystemRoleCodes.Owner; }", RoleUse.RoleName)]
    [InlineData("using Roles = Ims.Domain.SystemRoleCodes; class C { }", RoleUse.RoleName)]
    [InlineData("class C { bool M(string role) => role == \"Admin\"; }", RoleUse.RoleName)]
    [InlineData("class C { bool M(string role) => role is \"supervisor\" or \"x\"; }", RoleUse.RoleName)]
    public void Detector_FlagsEachBannedPattern(string code, RoleUse expected)
    {
        var uses = Uses(CSharpSyntaxTree.ParseText(code).GetRoot()).Select(u => u.Use).Distinct().ToList();

        Assert.Equal([expected], uses);
    }

    [Theory]
    [InlineData("[Authorize(Policy = \"x\")] class C { }")]
    [InlineData("[Authorize] class C { }")]
    [InlineData("class C { bool M(ClaimsPrincipal p) => p.HasClaim(\"perm\", \"stock.read\"); }")]
    [InlineData("class C { string M(Role r) => r.Code; }")]
    [InlineData("class C { string M(ClaimsPrincipal p) => p.FindFirstValue(ClaimTypes.Name); }")]
    [InlineData("class C { string M(Role r) => \"Roles\" + r.Name; }")]
    public void Detector_LeavesPermissionCodeAlone(string code)
    {
        Assert.Empty(Uses(CSharpSyntaxTree.ParseText(code).GetRoot()));
    }

    // ----- helpers -----
    private static IEnumerable<string> Scan(RoleUse use, IEnumerable<string> allowed)
    {
        var skip = allowed.ToHashSet();
        return SourceFiles.All.Where(f => !skip.Contains(f.RelativePath))
            .SelectMany(f => Uses(f.Root).Where(u => u.Use == use).Select(u => $"{f.Line(u.Node)}  {Shorten(u.Node)}"));
    }

    private static IEnumerable<(SyntaxNode Node, RoleUse Use)> Uses(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            var use = node switch
            {
                InvocationExpressionSyntax call when CalledName(call) is "IsInRole" or "RequireRole" => RoleUse.RoleGate,
                AttributeArgumentSyntax { NameEquals.Name.Identifier.Text: "Roles", Parent.Parent: AttributeSyntax attribute } when IsAuthorize(attribute.Name) => RoleUse.RoleGate,
                AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.Text: "Roles" }, Parent.Parent: ObjectCreationExpressionSyntax creation } when IsAuthorize(creation.Type) => RoleUse.RoleGate,
                MemberAccessExpressionSyntax { Name.Identifier.Text: "Role" } access when LastName(access.Expression) == "ClaimTypes" => RoleUse.RoleClaim,
                UsingDirectiveSyntax { StaticKeyword.RawKind: not 0 } directive when LastName(directive.NamespaceOrType) == "ClaimTypes" => RoleUse.RoleClaim,
                IdentifierNameSyntax { Identifier.Text: "SystemRoleCodes" } => RoleUse.RoleName,
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => ClassifyLiteral(literal.Token.ValueText),
                _ => (RoleUse?)null
            };
            if (use is not null) yield return (node, use.Value);
        }
    }

    private static RoleUse? ClassifyLiteral(string text) => text switch
    {
        "role" => RoleUse.RoleClaim,
        _ when text.EndsWith("/claims/role", StringComparison.Ordinal) => RoleUse.RoleClaim,
        _ when Ims.Domain.SystemRoleCodes.All.Contains(text, StringComparer.OrdinalIgnoreCase) => RoleUse.RoleName,
        _ => null
    };

    private static string? CalledName(InvocationExpressionSyntax call) => call.Expression switch
    {
        IdentifierNameSyntax name => name.Identifier.Text,
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        _ => null
    };

    private static bool IsAuthorize(TypeSyntax? type) => LastName(type) is "Authorize" or "AuthorizeAttribute";

    private static string? LastName(SyntaxNode? node) => node switch
    {
        QualifiedNameSyntax q => q.Right.Identifier.Text,
        SimpleNameSyntax s => s.Identifier.Text,
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        _ => null
    };

    private static string Shorten(SyntaxNode node)
    {
        var text = node.ToString().ReplaceLineEndings(" ");
        return text.Length <= 100 ? text : text[..97] + "...";
    }
}
