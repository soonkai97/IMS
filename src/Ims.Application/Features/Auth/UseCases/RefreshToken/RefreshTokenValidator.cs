using FluentValidation;
namespace Ims.Application.Features.Auth;

public sealed class RefreshTokenValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenValidator() => RuleFor(x => x.Body.RefreshToken).NotEmpty();
}
