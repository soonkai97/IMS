namespace Ims.Application.Features.Modules;

public record SwitchModuleCommand(string Code, ModuleSwitchRequest Data) : ICommand<ModuleSwitchResult>;
