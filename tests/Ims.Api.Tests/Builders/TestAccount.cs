namespace Ims.Api.Tests.Builders;

/// <summary>A user created for one test, so locking it or changing it cannot disturb other tests.</summary>
public sealed record TestAccount(int Id, string Username, string Email, string Password, string Role, IReadOnlyList<Guid> WarehouseIds);
