namespace Ims.Application.Features.Modules;

/// <summary>
/// The modules the switch handler refuses to turn ON. Production registers <see cref="Ims.Domain.ModuleCatalog.NotYetSwitchableOn"/>; it is a service only so the integration tests,
/// which turn accounting on through the API to test what it does, can register an empty list. Nothing else replaces it.
/// </summary>
public record NotYetSwitchableModules(IReadOnlyCollection<string> Codes);
