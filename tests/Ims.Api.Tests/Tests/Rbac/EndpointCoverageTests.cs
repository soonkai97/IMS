using System.Reflection;
using System.Text.RegularExpressions;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
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
            .Select(c => $"{c.Method} {Normalize(c.Path(new ReferenceData(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, "X", Guid.Empty, Guid.Empty, 1, 1)))} -> {c.Permission}")
            .Order().ToList();

        Assert.Equal(inCode, inTable);
    }

    [Fact]
    public void Only_attachments_can_be_deleted_everything_else_is_deactivated()
    {
        // Attachments are files hung on a document or product, not master data, so they may be removed.
        var verbs = typeof(Program).Assembly.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.Name != "AttachmentsController")
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
        return Regex.Replace(generic, @"\{id:(int|guid)\}", "{id}");
    }

    [GeneratedRegex(@"/[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}(?=/|$)")]
    private static partial Regex GuidSegment();

    [GeneratedRegex(@"/\d+(?=/|$)")]
    private static partial Regex IdSegment();

    [GeneratedRegex(@"/templates/[a-z\-]+$")]
    private static partial Regex TemplateKind();
}
