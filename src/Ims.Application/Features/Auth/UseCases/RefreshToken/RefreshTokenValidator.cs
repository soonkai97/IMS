using FluentValidation;
namespace Ims.Application.Features.Auth;

public class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator() => RuleFor(x => x.Body.RefreshToken).NotEmpty();
}
