using FluentValidation;
namespace Ims.Application.Features.Settings;

public sealed class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsValidator() => RuleFor(x => x.Data).SetValidator(new SettingsDtoValidator());
}
