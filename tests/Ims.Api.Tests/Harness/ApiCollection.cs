namespace Ims.Api.Tests.Harness;

/// <summary>
/// All API tests share one database and one host. They run one class at a time (a single xUnit collection),
/// and every test creates its own uniquely named data, so no test depends on another.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
