namespace Ims.Api;

/// <summary>
/// Marks a controller (or one action) as part of a module. While the module is off, <see cref="ModuleGateMiddleware"/> answers 404 to every signed-in caller before
/// authorization runs, whatever their permissions. A module that is not in <c>ModuleCatalog</c> is off. When it sits on both the controller and the action, both modules must be on.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequiresModuleAttribute(string code) : Attribute
{
    public string Code { get; } = code;
}
