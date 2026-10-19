using FluentValidation;
using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Modules;

public class SwitchModuleValidator : AbstractValidator<SwitchModuleCommand>
{
    public SwitchModuleValidator()
    {
        RuleFor(x => x.Data.Enabled).NotNull().WithMessage(Messages.ModuleStateRequired);
        RuleFor(x => x.Data.Reason).MustBeAReason(Messages.ModuleReasonRequired);
    }
}
