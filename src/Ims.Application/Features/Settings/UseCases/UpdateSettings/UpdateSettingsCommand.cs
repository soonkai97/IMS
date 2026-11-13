namespace Ims.Application.Features.Settings;

public sealed record UpdateSettingsCommand(SettingsDto Data) : ICommand<SettingsDto>;
