using FluentValidation;
namespace Ims.Application.Features.Settings;

public class UpdateSettingsValidator : AbstractValidator<UpdateSettingsCommand>
{
    public UpdateSettingsValidator() => RuleFor(x => x.Data).SetValidator(new SettingsDtoValidator());
}
