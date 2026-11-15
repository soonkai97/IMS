using System.Reflection;
using System.Text.RegularExpressions;
using Ims.Domain;

namespace Ims.Architecture.Tests;

/// <summary>
/// DDD-15, docs/architecture/ddd-roadmap.md 2.4: the events of <c>Role</c> and <c>User</c> carry ids and non-secret facts only. A password, a hash, a token or a salt in an event would end up in whatever a subscriber
/// does with it (an audit row, a log line), so no member of an event, nor of the value records an event carries, may be named like one. The subscribers keep the literal <c>audit.Record(</c> so the cost-visibility scan
/// still reads their payloads. The rule is a pure function that the fixtures prove, so it is not vacuous.
/// </summary>
public partial class IdentityEventsTests
{
    private static readonly string[] Subscribers =
    [
        "Ims.Application/Features/Roles/EventHandlers/RoleAuditSubscriber.cs",
        "Ims.Application/Features/Users/EventHandlers/UserAuditSubscriber.cs",
    ];

    [GeneratedRegex("password|hash|secret|token|salt|credential", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    /// <summary>The members, at any depth through the types an event carries, whose name looks like a secret.</summary>
    internal static IEnumerable<string> SecretMembers(Type type, HashSet<Type>? seen = null)
    {
        seen ??= [];
        if (!seen.Add(type)) yield break;
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (SecretName().IsMatch(property.Name)) yield return $"{type.Name}.{property.Name}";
            var inner = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments().FirstOrDefault() ?? property.PropertyType : property.PropertyType;
            if (inner is { IsClass: true } && inner.Namespace?.StartsWith("System", StringComparison.Ordinal) == false)
                foreach (var nested in SecretMembers(inner, seen)) yield return nested;
        }
    }

    [Fact]
    public void NoDomainEvent_CarriesASecret()
    {
        var events = EventRules.ImplementersIn(Layers.Domain).ToList();

        Verify.NoOffenders(events.SelectMany(e => SecretMembers(e)), "A domain event (or a record it carries) has a member named like a secret; carry ids and non-secret facts only (IdentityEventsTests):");
        Assert.Contains(events, e => e.Name == "UserUpdated");   // the rule is not vacuous: the events of the user are read
    }

    [Fact]
    public void TheIdentitySubscribers_StageTheirRowsWithTheSameLiteral()
    {
        foreach (var path in Subscribers)
        {
            var file = SourceFiles.All.Single(f => f.RelativePath == path);
            Assert.Contains("audit.Record(", file.Root.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(typeof(Fixtures.NamedPassword), 1)]
    [InlineData(typeof(Fixtures.CarriesASecretRecord), 1)]
    [InlineData(typeof(Fixtures.CarriesAListOfSecretRecords), 1)]
    [InlineData(typeof(Fixtures.Clean), 0)]
    public void TheDetector_FindsASecretNamedMemberAtAnyDepth(Type type, int expected) => Assert.Equal(expected, SecretMembers(type).Count());

    private static class Fixtures
    {
        public sealed record NamedPassword(string Username, string PasswordHash);

        public sealed record Inner(string RefreshToken);

        public sealed record CarriesASecretRecord(Inner After);

        public sealed record CarriesAListOfSecretRecords(IReadOnlyList<Inner> Rows);

        public sealed record Clean(Guid UserId, string Username, IReadOnlyList<int> WarehouseIds);
    }
}
