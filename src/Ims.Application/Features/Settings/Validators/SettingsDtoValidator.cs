using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Settings;

public class SettingsDtoValidator : AbstractValidator<SettingsDto>
{
    public SettingsDtoValidator() => RuleFor(x => x.LowStockAlertCooldownHours).InclusiveBetween(0, Limits.MaxLowStockCooldownHours);
}
