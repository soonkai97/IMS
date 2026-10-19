using System.Reflection;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Ims.Api.Tests.Tests.Rbac;

/// <summary>Keeps the permission table honest: it must list every protected endpoint of the API, with the permission the code actually requires.</summary>
public partial class EndpointCoverageTests
{
    [Fact]
    public void The_endpoint_table_lists_every_protected_endpoint_with_its_permission()
    {
        var inCode = ProtectedEndpointsInCode();
        var inTable = EndpointCatalog.All
            .Select(c => $"{c.Method} {Normalize(c.Path(new ReferenceData(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, "X", Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty)))} -> {c.Permission}")
            .Order().ToList();

        Assert.Equal(inCode, inTable);
    }

    [Fact]
    public void Every_policy_an_endpoint_asks_for_is_a_permission_of_the_catalog_listed_once_or_an_any_of_policy()
    {
        var policies = ProtectedEndpointsInCode().Select(line => line[(line.LastIndexOf("-> ", StringComparison.Ordinal) + 3)..]).Distinct().ToList();

        Assert.NotEmpty(policies);
        foreach (var policy in policies.Where(p => !AuthPolicies.AnyOf.ContainsKey(p)))
            Assert.True(PermissionCatalog.All.Count(p => p.Code == policy) == 1, $"'{policy}' must be in the permission catalog exactly once");
    }

    [Fact]
    public void Only_these_actions_have_no_permission_policy()
    {
        // The sign-in family is open or asks only for a token; attachments decide inside the handler (the owner of a file is a document or a product, whose permission
        // is asked there). A new action without a policy must be added here on purpose, so it is never an oversight.
        var withoutPolicy = typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any() && m.GetCustomAttribute<AuthorizeAttribute>()?.Policy is null)
                .Select(m => $"{t.Name}.{m.Name}"))
            .Order().ToList();

        Assert.Equal(
            ["AttachmentsController.Delete", "AttachmentsController.Download", "AttachmentsController.List", "AttachmentsController.Upload",
             "AuthController.ChangePassword", "AuthController.Login", "AuthController.Logout", "AuthController.Me", "AuthController.Refresh",
             "ModulesController.List"],
            withoutPolicy);
    }

    [Fact]
    public void Only_attachments_and_roles_can_be_deleted_everything_else_is_deactivated()
    {
        // Attachments are files hung on a document or product, not master data, so they may be removed. A role is configuration with no history of its own beyond the audit trail; it is deleted only when nobody holds it (ADR roles-permissions 5).
        var verbs = typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.Name is not ("AttachmentsController" or "RolesController"))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .SelectMany(a => a.HttpMethods)
            .Distinct().Order().ToList();

        Assert.DoesNotContain("DELETE", verbs);
    }

    private static List<string> ProtectedEndpointsInCode()
    {
        var list = new List<string>();
        foreach (var controller in typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var prefix = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? "";
            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var policy = action.GetCustomAttribute<AuthorizeAttribute>()?.Policy;
                if (policy is null) continue; // anonymous or "any signed-in user" (auth/me) endpoints are tested separately
                foreach (var http in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var path = "/" + string.Join('/', new[] { prefix, http.Template }.Where(s => !string.IsNullOrEmpty(s)));
                    list.Add($"{http.HttpMethods.Single()} {Normalize(path)} -> {policy}");
                }
            }
        }
        return list.Order().ToList();
    }

    /// <summary>Turns a concrete or templated path into a comparable shape: ids and the template kind become {id}/{kind}.</summary>
    private static string Normalize(string path)
    {
        var withoutQuery = path.Split('?')[0];
        var generic = GuidSegment().Replace(IdSegment().Replace(withoutQuery, "/{id}"), "/{id}");
        generic = TemplateKind().Replace(generic, "/templates/{kind}");
        generic = ModuleCodeSegment().Replace(generic, "/modules/{code}");
        return Regex.Replace(generic, @"\{id:(int|guid)\}", "{id}");
    }

    [GeneratedRegex(@"/[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}(?=/|$)")]
    private static partial Regex GuidSegment();

    [GeneratedRegex(@"/\d+(?=/|$)")]
    private static partial Regex IdSegment();

    [GeneratedRegex(@"/templates/[a-z\-]+$")]
    private static partial Regex TemplateKind();

    [GeneratedRegex(@"/modules/([a-z]+|\{code\})$")]
    private static partial Regex ModuleCodeSegment();
}
