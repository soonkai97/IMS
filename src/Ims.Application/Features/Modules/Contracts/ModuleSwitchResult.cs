namespace Ims.Application.Features.Modules;

/// <summary>
/// What a module switch answers: the module as <c>GET /api/modules</c> lists it, plus how many roles got permissions back. A module's permissions are kept, hidden, on the roles that
/// hold them while it is off; turning it on gives them back at once, so this count is the warning. It is 0 when the module was turned off, was already in the state asked for, or
/// has no permissions.
/// </summary>
/// <param name="RolesRegainingGrants">Live roles that hold at least one permission of the module and so have it in force again.</param>
public record ModuleSwitchResult(string Code, string Name, string Description, bool Enabled, IReadOnlyList<string> Requires, int RolesRegainingGrants)
{
    public static ModuleSwitchResult From(ModuleDto module, int rolesRegainingGrants) =>
        new(module.Code, module.Name, module.Description, module.Enabled, module.Requires, rolesRegainingGrants);
}
