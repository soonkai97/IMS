namespace Ims.Api.Tests.Support;

/// <summary>
/// The scope the tests give a user of a system role, as the seeded users have it: Admin and Owner see every warehouse, the others work in assigned warehouses. Only a test
/// convention: nothing in the application derives a scope from a role, it is the per-person flag <c>AllWarehouses</c>.
/// </summary>
public static class TestScope
{
    public static bool SeesAllWarehouses(string role) => role is Roles.Admin or Roles.Owner;
}
