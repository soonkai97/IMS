namespace Ims.Application;

/// <summary>Brings the database to a usable state at start-up: applies migrations and seeds reference data.</summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(bool seedDemoData, CancellationToken ct = default);
}
