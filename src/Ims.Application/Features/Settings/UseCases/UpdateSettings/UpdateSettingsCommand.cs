namespace Ims.Application.Features.Settings;

public record UpdateSettingsCommand(SettingsDto Data) : ICommand<SettingsDto>;
