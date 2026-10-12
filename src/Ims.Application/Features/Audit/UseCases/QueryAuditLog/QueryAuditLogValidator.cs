using FluentValidation;
using Ims.Domain;
namespace Ims.Application.Features.Audit;

public class QueryAuditLogValidator : AbstractValidator<QueryAuditLogQuery>
{
    /// <summary>No audit text column is longer than <see cref="Limits.AuditTextMaxLength"/>, so a longer filter cannot match anything.</summary>
    public QueryAuditLogValidator()
    {
        RuleFor(x => x.Filter.EntityType).MaximumLength(Limits.AuditTextMaxLength).WithMessage(Messages.SearchTooLong);
        RuleFor(x => x.Filter.EntityId).MaximumLength(Limits.AuditTextMaxLength).WithMessage(Messages.SearchTooLong);
        RuleFor(x => x.Filter.Actor).MaximumLength(Limits.AuditTextMaxLength).WithMessage(Messages.SearchTooLong);
        RuleFor(x => x.Filter.Action).MaximumLength(Limits.AuditTextMaxLength).WithMessage(Messages.SearchTooLong);
        RuleFor(x => x.Filter.Search).MaximumLength(Limits.AuditTextMaxLength).WithMessage(Messages.SearchTooLong);
        RuleFor(x => x.Filter.Cursor).MustBeAValidCursor();
        RuleFor(x => x.Filter.To).MustNotBeBefore(x => x.Filter.From);
    }
}
