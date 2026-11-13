namespace Ims.Application.Features.Modules;

public sealed record SwitchModuleCommand(string Code, ModuleSwitchRequest Data) : ICommand<ModuleSwitchResult>;
